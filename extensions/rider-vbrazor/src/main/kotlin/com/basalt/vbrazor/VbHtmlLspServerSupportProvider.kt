package com.basalt.vbrazor

import com.intellij.execution.configurations.GeneralCommandLine
import com.intellij.openapi.project.Project
import com.intellij.openapi.vfs.VirtualFile
import com.intellij.platform.lsp.api.LspServerSupportProvider
import com.intellij.ide.plugins.PluginManagerCore
import com.intellij.openapi.extensions.PluginId
import com.intellij.platform.lsp.api.ProjectWideLspServerDescriptor
import com.intellij.platform.lsp.api.customization.LspFormattingSupport
import com.intellij.platform.lsp.api.customization.LspSemanticTokensSupport
import java.io.File

/**
 * Starts the language server for .vbhtml files.
 *
 * The same server the VS Code extension uses and the same parser Basalt and the
 * build-time generator use: one implementation, so an editor and a build
 * cannot disagree about what a view means.
 */
class VbHtmlLspServerSupportProvider : LspServerSupportProvider {

    override fun fileOpened(
        project: Project,
        file: VirtualFile,
        serverStarter: LspServerSupportProvider.LspServerStarter
    ) {
        // Both extensions: a .vbp page is answered by the same server, and
        // guarding on the view alone left pages with a registered file type,
        // a grammar, and no server behind them.
        if (file.extension !in setOf("vbhtml", "vbp", "vb")) return

        // Without the server the file still opens; only the language features
        // are missing, which is better than refusing to show it.
        if (findServer() == null) return

        serverStarter.ensureServerStarted(VbHtmlLspServerDescriptor(project))
    }

    companion object {

        /**
         * Where the server is.
         *
         * A configured path wins so a developer can point at a build of their
         * own; otherwise the copy shipped with the plugin is used.
         */
        fun findServer(): File? {
            System.getProperty("vbrazor.server.path")?.let { configured ->
                val file = File(configured)
                if (file.isFile) return file
            }

            val name =
                if (System.getProperty("os.name").startsWith("Windows")) "vbrazor-langserver.exe"
                else "vbrazor-langserver"

            // Through the plugin's own descriptor, not the jar's location:
            // Rider loads plugins with a class loader whose CodeSource has no
            // location at all, so asking for it threw a
            // NullPointerException before any file was even looked for —
            // "Cannot invoke URL.toURI() because getLocation() is null", in
            // the log, on every .vbhtml opened.
            val pluginPath = PluginManagerCore
                .getPlugin(PluginId.getId("com.basalt.vbrazor"))
                ?.pluginPath

            val bundled = pluginPath?.resolve("lib")?.resolve("server")?.resolve(name)?.toFile()

            return bundled?.takeIf { it.isFile }?.also { ensureExecutable(it) }
        }

        /**
         * Makes the bundled server runnable.
         *
         * A plugin is delivered as a zip, and a zip does not reliably carry
         * the executable bit: the file arrives read-only, the plugin finds it
         * and cannot start it, and nothing is coloured or completed with no
         * word about why.
         */
        private fun ensureExecutable(file: File) {
            if (file.canExecute()) return

            // Best effort: a read-only plugins folder is a real arrangement,
            // and failing to start is better than failing to load.
            runCatching { file.setExecutable(true, false) }
        }
    }
}

/** How the platform talks to the server. */
private class VbHtmlLspServerDescriptor(project: Project) :
    ProjectWideLspServerDescriptor(project, "Razor (Visual Basic)") {

    override fun isSupportedFile(file: VirtualFile): Boolean =
        file.extension == "vbhtml" || file.extension == "vbp"

    /**
     * Colouring, from the server rather than from a grammar here.
     *
     * The platform asks for semantic tokens only if the descriptor says it
     * wants them; without this the file type is registered, the server runs
     * and answers, and the text stays black — which reads as a plugin that
     * does nothing.
     *
     * The server knows where the markup ends and the Visual Basic begins,
     * because it is the same parser that compiles the view. A TextMate
     * grammar here would be a second opinion on the same question, free to
     * disagree with the first.
     */
    override val lspSemanticTokensSupport: LspSemanticTokensSupport = LspSemanticTokensSupport()

    /**
     * Reformat Code, and the tidying that happens while typing.
     *
     * Declared here for the same reason the semantic tokens are: the platform
     * asks the server only for what the descriptor says it wants. Without
     * this the server answers formatting requests nobody ever sends, and
     * Reformat Code on a .vbhtml quietly does nothing.
     *
     * This is the half that made Visual Basic feel like Visual Basic: "end if"
     * becomes "End If" and "x=1" becomes "x = 1" as the line is left, from the
     * same formatter the IDE runs — so a file does not change shape depending
     * on which editor last touched it.
     */
    override val lspFormattingSupport: LspFormattingSupport = object : LspFormattingSupport() {
        override fun shouldFormatThisFileExclusivelyByServer(
            file: VirtualFile,
            ideCanFormatThisFileItself: Boolean,
            serverExplicitlyWantsToFormatThisFile: Boolean
        ) =
            // Templates only. Rider has no formatter for them at all, so the
            // server is the only thing that can lay them out.
            //
            // A .vb file is deliberately not claimed exclusively: Rider does
            // format those, and taking Reformat Code away from it to replace
            // it with ours would be a downgrade. What Rider does not do there
            // is the typing behaviour, and that arrives through the caret
            // listener without needing exclusivity.
            file.extension == "vbhtml" || file.extension == "vbp"
    }

    override fun createCommandLine(): GeneralCommandLine {
        val server = VbHtmlLspServerSupportProvider.findServer()
            ?: error("The Razor for Visual Basic language server was not found.")

        // Started in the project's own folder. Rider sends neither rootUri
        // nor workspaceFolders, so without this the server had no idea which
        // solution it was serving: it logged "looking for a solution under
        // (nowhere)", loaded no project, and answered every completion and
        // hover from the markup alone — the file coloured correctly and knew
        // no types at all.
        return GeneralCommandLine(server.absolutePath)
            .withWorkDirectory(project.basePath)
    }
}
