# Razor for Visual Basic — Visual Studio extension

Editing support for `.vbhtml` views in Visual Studio 2022 and later.

## What it does

Registers `.vbhtml` as a content type and starts the same language server the
VS Code extension and the Rider plugin use, so all three editors read a view
the same way.

The compiler is separate: add the
[Basalt.Razor.Vb](https://www.nuget.org/packages/Basalt.Razor.Vb) package to a
project and its views are compiled by a source generator into Visual Basic.

## What you need first

**Windows.** The VSIX tooling and the `Microsoft.VisualStudio.SDK` package are
Windows-only, and Visual Studio does not run elsewhere.

**Visual Studio 2022** with the **Visual Studio extension development**
workload — that is what supplies the VSIX project type and the SDK. Add it
through the Visual Studio Installer, under *Other Toolsets*.

**The .NET 10 SDK**, to publish the language server.

## Building it

There is no project file here — see below for why — so make one first, in this
folder:

    dotnet new vsix -n VbRazor.VisualStudio

Then add the two sources and the manifest to it, and build from a developer
command prompt:

    msbuild VbRazor.VisualStudio.csproj /p:Configuration=Release

The language server must be published into `server/` first:

    dotnet publish ..\..\src\Basalt.Razor.Vb.LanguageServer ^
        -c Release -r win-x64 --self-contained -o server

The result is a `.vsix` under `bin\Release`, installable by double-clicking it.

## When it does not work

**Nothing happens on opening a `.vbhtml`.** Visual Studio logs what an
extension did on startup: run `devenv /log` and read the XML it names, usually
under `%AppData%\Microsoft\VisualStudio\<version>\ActivityLog.xml`.

**The extension loads but suggests nothing.** The language server was not
published into `server/`, or it exited. It writes to the **Output** pane under
its own heading.

## Why the project file is not here

A VSIX project needs the Visual Studio SDK, which cannot be restored on macOS
or Linux, so committing a project file that fails to restore everywhere but
Windows would be worse than leaving the sources with instructions. The two
source files and the manifest are the extension; the project file that binds
them is one `dotnet new vsix` away on a Windows machine.
