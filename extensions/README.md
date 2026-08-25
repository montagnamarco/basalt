# Basalt in other editors

Basalt is not the only place a `.vbhtml` file gets opened. These three
extensions bring the same editing to Visual Studio, Rider and VS Code, all
speaking to the same language server — `vbrazor-langserver` — so an answer
does not depend on where the file was opened.

## What the NuGet package gives you already

Without any extension at all:

- the views compile, into ordinary classes in your assembly
- errors are reported at the right line and column of the `.vbhtml`
- those errors appear in the IDE, because Visual Studio and Rider run source
  generators as you type

That covers correctness. What it cannot cover is editing: a NuGet package has
no way to teach an editor a new file type, so syntax colouring and IntelliSense
need an extension.

## What the server answers

These are the server's own capabilities, checked against it directly. Each
extension is a client that asks for them; all three negotiate over LSP rather
than declaring a list of their own, so what an editor shows depends on what it
supports.

| | |
|---|---|
| Completion, including the model's members | yes |
| Hover | yes |
| Diagnostics | yes |
| Go to definition | yes |
| Document symbols, folding, highlight | yes |
| Signature help | yes |
| Find references, rename | not yet |
| Code actions | not yet |

## The extensions themselves

| | VS Code | Rider | Visual Studio |
|---|---|---|---|
| Starts the server | yes | yes | yes |
| Syntax colouring | yes, TextMate grammar | file type registered | content type registered |
| Watches `.vb` files for model changes | yes | not yet | not yet |
| Published to a marketplace | not yet | not yet | not yet |
| Tried end to end in the editor | not yet | not yet | not yet |

The last row is the honest one: the pieces are in place and the server is
tested directly, but none of the three has been run inside its editor. Until
that happens the rows above are what the code says, not what a user saw.

The server needs a solution or `.vbproj` under the folder the editor opens. It
finds one on its own; while it loads, answers come from the markup alone, and
if it finds none it says so rather than answering as though it had.

## Building them

Each folder has its own README with the prerequisites, which differ a good
deal. In short:

| | Needs | Build |
|---|---|---|
| VS Code | Node.js 20+ | `npm install && npm run compile` |
| Rider | JDK 21+ — Rider's own will do | `./gradlew buildPlugin` (builds, verified) |
| Visual Studio | Windows, VS 2022 with the extension development workload | `msbuild` from a developer prompt |

Two things worth knowing before you start:

- **Rider already has the JDK you need.** Point `JAVA_HOME` at
  `Rider.app/Contents/jbr/Contents/Home` and nothing else has to be installed.
- **The Visual Studio folder has no project file**, deliberately: a VSIX
  project cannot be restored anywhere but Windows, so committing one would
  break the build for everyone else. Its README says how to make it.

## The language server

All three start the same server, and none of them works without it. Publish it
for the machine the editor runs on:

```
dotnet publish src/Basalt.Razor.Vb.LanguageServer \
    -c Release -r osx-arm64 --self-contained \
    -o extensions/vscode-vbrazor/server
```

The runtime identifier is `osx-arm64`, `osx-x64`, `linux-x64` or `win-x64`.
Each extension expects it in its own folder — `server/` for VS Code and Visual
Studio, `src/main/resources/server/` for Rider — or anywhere on the PATH under
the name `vbrazor-langserver`.

A self-contained publish needs no .NET on the machine that runs the editor,
which is why it is worth the size.
