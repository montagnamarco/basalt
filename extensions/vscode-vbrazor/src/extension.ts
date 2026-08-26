import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
    TransportKind
} from 'vscode-languageclient/node';
import { createProject, templates } from './templates';

let client: LanguageClient | undefined;

export async function activate(context: vscode.ExtensionContext): Promise<void> {
    const server = resolveServer(context);

    if (!server) {
        // Colouring still works without the server, since the grammar is part
        // of the extension; saying what is missing is better than failing
        // silently and looking broken.
        vscode.window.showWarningMessage(
            'The Razor for Visual Basic language server was not found. '
            + 'Syntax colouring works; completion and diagnostics need the server. '
            + 'Set "vbrazor.server.path" to point at it.');
        return;
    }

    const serverOptions: ServerOptions = {
        run: { command: server, transport: TransportKind.stdio },
        debug: { command: server, transport: TransportKind.stdio }
    };

    const clientOptions: LanguageClientOptions = {
        // Both: a .vbpage page is the same Visual Basic behind different
        // delimiters, and the server answers about either.
        documentSelector: [
            { scheme: 'file', language: 'vbhtml' },
            { scheme: 'file', language: 'vbpage' }
        ],
        synchronize: {
            // The .vb files too: a view's model lives in one, and the server
            // answers about the model from a compilation of the project. A
            // watcher on views alone leaves it describing a class that has
            // since changed.
            fileEvents: [
                vscode.workspace.createFileSystemWatcher('**/*.vbhtml'),
                vscode.workspace.createFileSystemWatcher('**/*.vbpage'),
                vscode.workspace.createFileSystemWatcher('**/*.vb'),
                vscode.workspace.createFileSystemWatcher('**/*.vbproj')
            ]
        },
        outputChannelName: 'Razor for Visual Basic'
    };

    client = new LanguageClient('vbrazor', 'Basalt', serverOptions, clientOptions);

    await client.start();

    context.subscriptions.push({ dispose: () => { void client?.stop(); } });

    registerCommands(context);
    formatLinesOnLeaving(context);
}

/**
 * Tidies a line as soon as the caret leaves it, however it leaves.
 *
 * The protocol cannot express this: onTypeFormatting fires on characters the
 * author types, so Enter reaches the server and arrowing off a line or
 * clicking elsewhere does not — which in practice is most of the times a line
 * is finished.
 *
 * The edit is asked of the same server that answers everything else, so what
 * happens here and what Format Document does cannot disagree.
 */
function formatLinesOnLeaving(context: vscode.ExtensionContext): void {
    let lastLine = -1;

    context.subscriptions.push(vscode.window.onDidChangeTextEditorSelection(async event => {
        const editor = event.textEditor;
        const document = editor.document;

        if (document.languageId !== 'vbhtml' && document.languageId !== 'vbpage') return;

        const line = editor.selection.active.line;
        const previous = lastLine;
        lastLine = line;

        // The first position seen formats nothing: opening a file puts the
        // caret somewhere, and rewriting that line before the author has
        // touched it edits a file nobody asked to change.
        if (previous < 0 || previous === line) return;
        if (previous >= document.lineCount) return;

        const text = document.lineAt(previous);

        // An empty line carries no token to correct.
        if (text.isEmptyOrWhitespace) return;

        const version = document.version;

        const edits = await vscode.commands.executeCommand<vscode.TextEdit[]>(
            'vscode.executeFormatOnTypeProvider',
            document.uri,
            text.range.end,
            '\n',
            { tabSize: 4, insertSpaces: true });

        if (!edits?.length) return;

        // The document may have been edited while the request was in flight,
        // and applying an edit computed against text that no longer exists
        // corrupts it.
        if (document.version !== version) return;

        const change = new vscode.WorkspaceEdit();
        for (const edit of edits) change.replace(document.uri, edit.range, edit.newText);

        await vscode.workspace.applyEdit(change);
    }));
}

/** The commands the extension adds to the palette. */
function registerCommands(context: vscode.ExtensionContext): void {
    context.subscriptions.push(vscode.commands.registerCommand(
        'vbrazor.newProject', createProjectFromTemplate));

    context.subscriptions.push(vscode.commands.registerCommand(
        'vbrazor.restartServer', async () => {
            await client?.restart();
            vscode.window.showInformationMessage('The language server was restarted.');
        }));
}

/**
 * Asks what to create and where, then writes it.
 *
 * The project is opened afterwards: a template the user then has to go and
 * find is half a feature.
 */
async function createProjectFromTemplate(): Promise<void> {
    const template = await vscode.window.showQuickPick(
        templates.map(t => ({ label: t.label, detail: t.description, id: t.id })),
        { title: 'Which kind of project?' });

    if (!template) return;

    const name = await vscode.window.showInputBox({
        title: 'Project name',
        value: 'MyWebApp',
        validateInput: value => /^[A-Za-z_][A-Za-z0-9_.]*$/.test(value)
            ? undefined
            : 'A project name starts with a letter and holds letters, digits, dots and underscores.'
    });

    if (!name) return;

    const folders = await vscode.window.showOpenDialog({
        canSelectFolders: true,
        canSelectFiles: false,
        canSelectMany: false,
        title: 'Where should the project go?'
    });

    if (!folders || folders.length === 0) return;

    const directory = path.join(folders[0].fsPath, name);

    try {
        const projectPath = await createProject(template.id, directory, name);

        await vscode.commands.executeCommand(
            'vscode.openFolder', vscode.Uri.file(directory), { forceNewWindow: false });

        vscode.window.showInformationMessage(`Created ${path.basename(projectPath)}.`);
    } catch (error) {
        vscode.window.showErrorMessage(
            `The project could not be created: ${error instanceof Error ? error.message : error}`);
    }
}

export async function deactivate(): Promise<void> {
    await client?.stop();
}

/**
 * Finds the language server.
 *
 * A configured path wins, so a developer can point at a build of their own;
 * otherwise the copy shipped inside the extension is used.
 */
function resolveServer(context: vscode.ExtensionContext): string | undefined {
    const configured = vscode.workspace
        .getConfiguration('vbrazor')
        .get<string>('server.path');

    if (configured && configured.length > 0) {
        return fs.existsSync(configured) ? configured : undefined;
    }

    const name = process.platform === 'win32'
        ? 'vbrazor-langserver.exe'
        : 'vbrazor-langserver';

    const bundled = path.join(context.extensionPath, 'server', name);

    return fs.existsSync(bundled) ? bundled : undefined;
}
