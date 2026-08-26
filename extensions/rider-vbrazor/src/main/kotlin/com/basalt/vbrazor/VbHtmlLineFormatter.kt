package com.basalt.vbrazor

import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.command.WriteCommandAction
import com.intellij.openapi.editor.Document
import com.intellij.openapi.editor.Editor
import com.intellij.openapi.editor.event.CaretEvent
import com.intellij.openapi.editor.event.CaretListener
import com.intellij.openapi.editor.event.EditorFactoryEvent
import com.intellij.openapi.editor.event.DocumentEvent
import com.intellij.openapi.editor.event.DocumentListener
import com.intellij.openapi.editor.event.EditorFactoryListener
import com.intellij.openapi.fileEditor.FileDocumentManager
import com.intellij.openapi.project.Project
import com.intellij.openapi.util.TextRange
import com.intellij.openapi.vfs.VirtualFile
import com.intellij.platform.lsp.api.LspServer
import com.intellij.platform.lsp.api.LspServerManager
import com.intellij.platform.lsp.api.LspServerState
import org.eclipse.lsp4j.DocumentOnTypeFormattingParams
import org.eclipse.lsp4j.FormattingOptions
import org.eclipse.lsp4j.Position
import org.eclipse.lsp4j.TextEdit

/**
 * Tidies a line as soon as the caret leaves it, however it leaves.
 *
 * This is the half that made Visual Basic feel like Visual Basic, and the
 * protocol cannot express it: textDocument/onTypeFormatting fires on
 * characters the author *types*, so pressing Enter reaches the server and
 * arrowing down, clicking elsewhere or paging away does not. A line left by
 * any other means stayed exactly as written — which in practice is most of
 * them, because moving off a line without typing is the ordinary way to
 * finish it.
 *
 * The line is sent to the same server that answers every other request, so
 * what happens here and what Reformat Code does cannot disagree.
 */
class VbHtmlLineFormatter : EditorFactoryListener {

    override fun editorCreated(event: EditorFactoryEvent) {
        val editor = event.editor
        val project = editor.project ?: return
        val file = FileDocumentManager.getInstance().getFile(editor.document) ?: return

        // Plain .vb as well. Rider ships Roslyn's Visual Basic assemblies but
        // wires none of the typing behaviour to them: a block does not close
        // itself, "end if" stays lower case, and a new line lands at the left
        // margin. Completion and navigation are left to ReSharper, which does
        // them well — only formatting is claimed here.
        if (file.extension !in setOf("vbhtml", "vbp", "vbrazor", "vb")) return

        val watcher = LineWatcher(project, editor, file)

        editor.caretModel.addCaretListener(watcher)

        // And the document, because typing does not raise a caret event.
        // Inserting text moves the caret as a side effect of the edit, so a
        // line finished by typing the next one never reached the watcher: the
        // formatting only happened when the caret was moved deliberately,
        // which is not how anyone writes a block.
        editor.document.addDocumentListener(watcher)
    }

    /**
     * Watches which line the caret is on, and tidies the one it left.
     */
    private class LineWatcher(
        private val project: Project,
        private val editor: Editor,
        private val file: VirtualFile
    ) : CaretListener, DocumentListener {

        /**
         * The line the caret was on when it last moved.
         *
         * Starts at -1 so the first position seen formats nothing: opening a
         * file puts the caret somewhere, and rewriting that line before the
         * author has touched it edits a file nobody asked to change.
         */
        private var lastLine = -1

        /**
         * Whether the edit being reported is one of ours.
         *
         * Applying a formatting edit changes the document, which raises
         * documentChanged again: without this the watcher answers its own
         * edit, asks the server about the line it just wrote, and the file is
         * formatted in a loop for as long as anything keeps changing.
         */
        private var applyingOurOwnEdit = false

        override fun caretPositionChanged(event: CaretEvent) {
            moved(event.newPosition.line)
        }

        override fun documentChanged(event: DocumentEvent) {
            if (applyingOurOwnEdit) return

            // Where the edit left the caret, which is what the author is now
            // typing on. Read from the event rather than the caret model: the
            // model is not updated yet while the change is being dispatched.
            val line = event.document.getLineNumber(
                minOf(event.offset + event.newLength, event.document.textLength))

            // After the change has finished, not during it: reading the
            // document to compare against later is only meaningful once it
            // has settled, and a request started mid-change captured a state
            // that no longer existed by the time the answer came back.
            ApplicationManager.getApplication().invokeLater { moved(line) }
        }

        /**
         * Notes the line the caret is on, and tidies the one it left.
         */
        private fun moved(line: Int) {
            val previous = lastLine
            lastLine = line

            if (previous < 0 || previous == line) return

            format(previous)
        }

        private fun format(line: Int) {
            val document = editor.document

            if (line >= document.lineCount) return

            val start = document.getLineStartOffset(line)
            val end = document.getLineEndOffset(line)

            // An empty line carries no token to correct, and asking about one
            // is a round trip for a guaranteed no-op.
            if (document.getText(TextRange(start, end)).isBlank()) return

            val server = LspServerManager.getInstance(project)
                .getServersForProvider(VbHtmlLspServerSupportProvider::class.java)
                .firstOrNull { it.state == LspServerState.Running }
                ?: return

            // The whole document as it was asked about. The server answers
            // with a replacement for all of it, so all of it has to be
            // unchanged for that replacement to be safe — applying it over
            // text the author has edited meanwhile would throw their work
            // away.
            val asked = document.text

            // Off the UI thread, then applied back on it: the request crosses
            // a process boundary, and waiting for it inline is felt as the
            // caret sticking on every line change.
            ApplicationManager.getApplication().executeOnPooledThread {
                val edits = request(server, document, line, end) ?: return@executeOnPooledThread

                if (edits.isEmpty()) return@executeOnPooledThread

                ApplicationManager.getApplication().invokeLater {
                    // Compared by content rather than by modification stamp:
                    // a stamp moves for changes that leave the text identical,
                    // and it had already moved by the time the answer came
                    // back, so every edit was discarded in silence.
                    if (document.text != asked) return@invokeLater

                    applyingOurOwnEdit = true

                    try {
                        WriteCommandAction.runWriteCommandAction(project, "Format Line", null, {
                            apply(document, edits)
                        })
                    } finally {
                        applyingOurOwnEdit = false
                    }
                }
            }
        }

        private fun request(
            server: LspServer,
            document: Document,
            line: Int,
            end: Int
        ): List<TextEdit>? = try {
            val params = DocumentOnTypeFormattingParams(
                server.getDocumentIdentifier(file),
                FormattingOptions(4, true),
                Position(line, end - document.getLineStartOffset(line)),

                // The same trigger Enter sends. The server formats the line
                // the position names and does not care which key moved the
                // caret off it.
                "\n")

            server.sendRequestSync(LspServer.DEFAULT_REQUEST_TIMEOUT_MS) { lsp ->
                lsp.textDocumentService.onTypeFormatting(params)
            }
        } catch (_: Exception) {
            // A server restarting, or a request cancelled behind us: the line
            // stays as written, which is what happened before this existed.
            null
        }

        private fun apply(document: Document, edits: List<TextEdit>) {
            // Last first, so an edit does not shift the ones before it.
            for (edit in edits.sortedByDescending { it.range.start.line }) {
                document.replaceString(
                    offsetOf(document, edit.range.start),
                    offsetOf(document, edit.range.end),
                    edit.newText)
            }
        }

        private fun offsetOf(document: Document, position: Position): Int {
            if (position.line >= document.lineCount) return document.textLength

            return minOf(
                document.getLineStartOffset(position.line) + position.character,
                document.getLineEndOffset(position.line))
        }
    }
}
