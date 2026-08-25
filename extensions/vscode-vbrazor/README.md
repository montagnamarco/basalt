# Razor for Visual Basic

Editing support for `.vbhtml` views in Visual Studio Code.

ASP.NET Core's Razor compiler emits C# and only C#, so `.vbhtml` — a real file
type in ASP.NET, before Core — has no compiler and no editor support. This
extension supplies the editing half; the
[Basalt.Razor.Vb](https://www.nuget.org/packages/Basalt.Razor.Vb) package supplies
the compiler.

## What it does

- **Colouring** for markup, Razor transitions and the Visual Basic behind them.
- **Diagnostics** as you type, reported against the view rather than against
  generated code.
- **Completion** for directives, model members and HTML tags.
- **Go to definition** from a name in the markup to where a `@Code` block
  declares it.
- **Signature help** inside expressions.

## Getting the compiler too

Editing support alone will not build a project. Add the package:

```xml
<PackageReference Include="Basalt.Razor.Vb" Version="1.0.0" />
```

Every `.vbhtml` file is then compiled by a source generator into Visual Basic,
which the VB compiler builds like any other source file.

## Settings

| Setting | Meaning |
| --- | --- |
| `vbrazor.server.path` | Path to the language server. Empty uses the bundled copy. |
| `vbrazor.trace.server` | Logs the conversation with the server: `off`, `messages`, `verbose`. |

## What you need first

**Node.js 20 or later**, which brings npm with it:

    node --version

    brew install node                 # macOS
    sudo apt install nodejs npm       # Debian, Ubuntu
    winget install OpenJS.NodeJS.LTS  # Windows

**vsce**, only if you want to package a `.vsix`. It is not a dependency of
this folder, because it is a tool rather than something the extension uses:

    npm install --global @vscode/vsce

**The .NET 10 SDK**, to publish the language server.

## Building it

    npm install
    npm run compile

That is enough to run it: press **F5** in VS Code with this folder open, and a
second window starts with the extension loaded.

To build an installable `.vsix`:

    npm run package

The language server must be published into `server/` before packaging:

    dotnet publish ../../src/Basalt.Razor.Vb.LanguageServer \
        -c Release -r <runtime-identifier> --self-contained \
        -o server

The runtime identifier is the machine the extension will run on: `osx-arm64`,
`osx-x64`, `linux-x64` or `win-x64`. A `.vsix` carries one, so publishing for
several means one package each.

Without that folder the extension still loads, and looks for
`vbrazor-langserver` on the PATH instead — which is what
`vbrazor.server.path` overrides.

## When it does not work

**Nothing is coloured, nothing is suggested.** The server did not start. Open
**Output → Razor for Visual Basic** to see why; the usual reason is that the
server was never published and is not on the PATH either.

**Completion knows nothing about the model.** The server needs a solution or a
`.vbproj` in the folder you opened. It finds one on its own, but only inside
the workspace root — opening a single file gives you markup support and
nothing more.
