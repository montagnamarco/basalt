# Basalt

**A cross-platform IDE for VB.NET, built on Avalonia.**

Basalt is an integrated development environment for Visual Basic on modern
.NET. It runs on macOS, Windows and Linux (x64 and arm64) from one source tree.
It builds with the real .NET SDK, answers IntelliSense questions with Roslyn
inside its own process, debugs with netcoredbg, and types the way Visual Basic
does on Windows: `end if` becomes `End If` as you type.

The repository also holds a set of packages that stand on their own: Razor for
Visual Basic (`.vbhtml`), Blazor components in Visual Basic (`.vbrazor`),
`.vbpage` pages, and Windows Forms for .NET on all three desktop systems.
The packages do not depend on the IDE. They are described in
[Part II](#part-ii-razor-for-visual-basic-blazor-and-the-packages).

Everything is MIT licensed.

---

## Table of contents

**Part I: the IDE**

- [Why Basalt](#why-basalt)
- [Feature tour](#feature-tour)
- [Prerequisites](#prerequisites)
- [Install or build from source](#install-or-build-from-source)
- [Your first project](#your-first-project)
- [Keyboard shortcuts](#keyboard-shortcuts)
- [Troubleshooting](#troubleshooting)
- [Known limitations of the IDE](#known-limitations-of-the-ide)
- [FAQ](#faq)

**Part II: Razor for Visual Basic, Blazor and the packages**

- [The `.vbhtml` story](#the-vbhtml-story)
- [Quick start](#quick-start)
- [The packages](#the-packages)
- [How a view becomes a class](#how-a-view-becomes-a-class)
- [The language](#the-language)
- [Serving views under ASP.NET Core](#serving-views-under-aspnet-core)
- [Blazor components](#blazor-components)
- [`.vbpage` pages: Classic ASP, modernised](#vbpage-pages-classic-asp-modernised)
- [IDE support for `.vbhtml`](#ide-support-for-vbhtml)
- [Project templates](#project-templates)
- [QuickBASIC, for the fun of it](#quickbasic-for-the-fun-of-it)
- [Building the packages and the extensions](#building-the-packages-and-the-extensions)
- [Repository layout](#repository-layout)
- [Design decisions](#design-decisions)
- [Known limitations](#known-limitations)
- [Contributing](#contributing)
- [Licence](#licence)

---

# Part I: the IDE

## Why Basalt

Visual Basic is alive on .NET, but its tooling lives almost entirely in Visual
Studio on Windows. Basalt is an IDE where VB.NET is the first language, not an
afterthought, and where the platform does not matter.

| Advantage | What it means concretely |
|---|---|
| **VB.NET first, on modern .NET** | New Solution offers Visual Basic .NET projects on .NET 10: Windows Forms, Avalonia, console, class library, test project, Web API, web application, Razor Pages, MVC and Blazor. |
| **Cross-platform** | One IDE for macOS, Windows and Linux. It also builds self-contained programs for the six desktop runtimes (Windows, macOS, Linux; x64 and arm64) from File > Make executable. |
| **Roslyn in-process** | Completion, quick info, diagnostics, go to definition, find references and refactorings come from Roslyn running inside the IDE, with the project's real compilation loaded through MSBuild. There is no language server to configure. |
| **Real MSBuild** | Build and Run call `dotnet build` and `dotnet run`. If it builds in Basalt, it builds in a terminal and in CI. Errors are parsed into clickable entries in the Problems panel. |
| **Typing that behaves like VB** | Keywords and identifiers are corrected as you type, blocks close themselves on Enter, `Implements` and `Overrides` write the members for you. |
| **Visual Studio muscle memory** | Shortcuts follow Visual Studio's wherever they could, with a Visual Studio and a VS Code keyboard scheme in Settings. |
| **Windows Forms anywhere** | Forms made with the `.vb` / `.Designer.vb` pair open in a visual designer and run on macOS and Linux through the Basalt.WinForms package. |
| **Web in Visual Basic** | `.vbhtml` views, `.vbrazor` Blazor components and `.vbpage` pages compile, are highlighted and complete in the editor, and stop at breakpoints. |

Basalt is not a Visual Studio clone. [Known limitations](#known-limitations-of-the-ide)
lists what it does not do.

---

## Feature tour

Menu names below are the ones in the IDE. Where a shortcut is given it is the
default of the Basalt keyboard scheme; on macOS "Ctrl" is Cmd, except for
the completion keys (Ctrl+Space, Ctrl+J, Ctrl+Shift+Space), which use Control (see
[Keyboard shortcuts](#keyboard-shortcuts)).

### Editor and IntelliSense

- **Completion** opens as you type (switch it off under Settings > Editor) or
  on Ctrl+Space or Ctrl+J. Ctrl+Shift+Space shows signature help. **Enter and
  Tab** take the highlighted entry when it matches what you typed (a Tab after
  `Str` writes `String`; after `Strq` it does not). With nothing typed they take
  an entry only if the list preselected it or you moved the highlight to it with
  the arrow keys, so Enter still starts a new line. When a keyword and a type
  share a name, the keyword comes first.
- **Quick info** on hover and **diagnostics** as squiggles, from Roslyn.
- **Navigation**: Go to Definition (F12), Find All References (Shift+F12), Go to
  File (Ctrl+Shift+O), Go to Symbol in Solution (Ctrl+T), Go to Symbol in File,
  Go to Line (Ctrl+G), Navigate Backward and Forward. The Outline and
  References panels list symbols and results.
- **Find and Replace** in the open file (Ctrl+F, Ctrl+H, F3 and Shift+F3 for the
  next and previous match) and **Find in Files** (Ctrl+Shift+F) with a Search
  panel.
- **Code folding**, **bracket and quote closing**, a **Git change margin** and a
  **breakpoint margin**.
- **Snippets**: type a shortcut and press Tab twice. The shortcuts are `for`,
  `foreach`, `while`, `do`, `if`, `ifelse`, `select`, `try`, `tryf`, `sub`,
  `function`, `property`, `class`, `module`, `interface`, `enum`, `structure`,
  `using`, `with`, `ctor`, `region`, and `?` for `Console.WriteLine`.
- **Format Document** (Edit menu), **Format Selection**, and formatting on save
  (on by default, Settings > Editor). Code is also tidied while you type.
- **Object Browser** (View menu) and an **Object and Procedure** pair of lists
  in the code window.
- The status bar shows the line ending and encoding of the file; click the
  encoding to reopen the file as another one.

Typing conventions you get without asking: `end if` becomes `End If`,
`if x=1 then` becomes `If x = 1 Then`, `console.writeline` becomes
`Console.WriteLine`, `Then` is added to a finished `If`, the matching
`End Sub` / `End If` is written on Enter, XML documentation comments are
started for you, and `Implements` / `Overrides` lines get their members written
out. Strings and comments are left alone. Turn the conventions off with
Settings > Editor > "Apply conventions while typing".

### Refactoring

Right-click in the editor, press Ctrl+. (Quick Actions), or use the command
palette (Ctrl+Shift+P):

| Refactoring | Default shortcut |
|---|---|
| Rename | F2 |
| Quick Actions | Ctrl+. |
| Extract Method | Ctrl+Alt+M |
| Extract Variable | Ctrl+Alt+V |
| Extract Constant | |
| Inline Variable | Ctrl+Alt+N |
| Convert If / Select Case | |
| Generate Constructor, Generate Property | |
| Move Type to File | |
| Change Signature | |
| Add Imports for the Name Under the Caret | |
| Sort and Remove Imports | Ctrl+Alt+O |

Some refactorings show a preview dialog before they apply. They work on
Visual Basic projects.

### Build, Problems and Run

- **Build > Build Solution** (F6; Ctrl+Shift+B in the Visual Studio scheme).
  Rebuild Solution and Clean Solution are commands in the palette.
- The toolbar has pickers for the **build configuration** (Debug or Release)
  and the **startup project**. Settings > Toolbar chooses which buttons show.
- The **Problems** panel lists errors, warnings and messages, filterable;
  double-click opens the file at the line. **Output** shows the build log.
- **Run > Start Without Debugging** (Ctrl+F5) builds, then runs
  `dotnet run --no-build`, so the project's `launchSettings.json` (environment,
  arguments, URLs) is honoured. A **console program** (a project on the plain
  `Microsoft.NET.Sdk` with `OutputType` `Exe`, without Windows Forms, WPF or
  Avalonia) runs in a **Terminal tab** named after the project, so
  `Console.ReadLine` receives what you type there; when the program ends the tab
  stays open and shows `[process exited with code N]`. A program with a window,
  and web projects, keep the redirected run, whose output goes to the **Output**
  panel. **Run > Stop** ends either kind.
- **Launch settings.** For a Visual Basic project the file is
  `My Project/launchSettings.json`, the project's `AppDesignerFolder`, because
  that is where `dotnet run` reads it. A file an older Basalt wrote to
  `Properties/launchSettings.json` is moved there when the solution opens or the
  project runs, unless a file already exists in the new place. Project >
  Properties... edits the settings.
- **File > Make executable...** publishes the startup project as a release,
  self-contained, single-file program for the platforms you tick.
- **Project > Add New Item...**, **Project > References...** (projects,
  assemblies, packages) and **Project > Properties...** edit the project.
- **Solution Explorer** creates, renames, deletes, cuts, copies, pastes, and
  excludes or includes files.
- **Solutions** open from `.sln` and `.slnx` files, including projects that sit
  inside solution folders nested to any depth. The projects a solution lists are
  the ones shown, not every project found in the folders around it.

### The debugger

Debugging uses [netcoredbg](https://github.com/Samsung/netcoredbg), found as
described in [Prerequisites](#prerequisites). **Debug > Start Debugging** (F5)
builds first, then launches the startup project under the debugger.

- **Console programs run in a Terminal tab.** F5 on a console program (the same
  kind Ctrl+F5 sends to a tab) starts it in a Terminal tab, so `Console.ReadLine`
  gets what you type. The program waits at its start, through a .NET startup
  hook that Basalt compiles for your installed runtime and keeps in
  `~/.basalt/debugger-wait/`, until the debugger attaches (it gives up after 30
  seconds). Breakpoints therefore work from the first line. The tab comes to the
  front when the session starts and again on Continue; a step leaves the panels
  as they are. Programs with a window, and web projects, are launched by the
  debugger as before.
- **Breakpoints**: F9 toggles one in the margin. A breakpoint can have a
  condition written in Visual Basic (`i = 4`,
  `name <> "" AndAlso total > 10`) and a number of passes to skip first
  ("Add Condition..."). Run to Cursor is Ctrl+F10.
- **Stepping**: Step Over F10, Step Into F11, Step Out Shift+F11, Continue F5,
  Stop Shift+F5. **Break All** is in the Debug menu and the palette; it has no
  default key. If Break All stops inside library code with no source (in
  `Thread.Sleep`, say), the first frame that has source is selected, with its
  line and variables.
- **Panels**: Call Stack, Variables, Breakpoints, Watch and Immediate (View
  menu). Hovering a variable while paused shows its value.
- **Visual Basic expressions.** netcoredbg evaluates C#. Watches, hovers and the
  Immediate window parse your Visual Basic expression with Roslyn and send its
  C# reading, so you write `Items.Count`, `x <> 3`, `total Mod 4` or `Me.Name`
  as you would in the editor. Where VB syntax can mean two things (`items(1)` is
  an index or a call), Basalt tries the likelier reading first and the next if
  the debugger refuses it.
- **Visual Basic display.** Variables, Watch and Call Stack show Visual Basic
  type names and values: `Integer`, `List(Of String)`, `Integer()`, `Nothing`,
  `True`, `Me`. Frames read `Program.ComputeAsync()` rather than the compiler's
  state-machine name, and parameters read `n As Integer`.
- **Async, Iterator and lambda locals.** The locals of an `Async` or `Iterator`
  method, and of a lambda that captured variables, are shown under their source
  names, with `Me`, and can be watched.
- **Exception stops.** At an unhandled exception the banner says "stopped on an
  exception" and Output names the exception type and message. The locals of each
  of your frames in the call stack can be read.
- **Immediate window** (Ctrl+Alt+I). `?expression` or `Print expression`
  evaluates in the paused frame. Statements are refused while debugging. With no
  debug session, the line is compiled and run in a separate process against
  your built project. Up and Down recall earlier lines. `Debug.Print` and
  `Debug.WriteLine` output from the program appears there too.
- Breakpoints in `.vbhtml` and `.vbrazor` files stop while the view or
  component renders; this is covered by the tests.
- Settings > Debugging: path to netcoredbg, stop at entry, step into code
  without debug symbols.

What the debugger cannot do is listed under
[Known limitations of the IDE](#known-limitations-of-the-ide).

### The Windows Forms designer

Open a form's `.vb` / `.Designer.vb` pair from Solution Explorer. The tab has a
**Designer** and a **Code** view. The designer includes:

- a toolbox (double-click or drag to add), including controls from referenced
  libraries and the project's own;
- a Properties window grouped by category, an Events panel (double-click an
  event to create its handler), and smart tags;
- layout tools: alignment, distribution, snap to grid and to other controls,
  rulers, tab order, z-order, copy and paste, undo and redo;
- the **keyboard**: the arrow keys move the selected control (one grid step when
  snapping is on, one pixel with Ctrl), Shift+arrow resizes it by one pixel from
  its bottom-right corner, Tab and Shift+Tab walk the controls in tab order,
  Esc selects the container, Delete removes the selection and Ctrl+A (Cmd+A on
  macOS) selects the controls of the form. Lock Controls stops the keys
  from moving anything;
- the **Edit menu** (Cut, Copy, Paste, Select All) and the **Format menu**
  (Align, Make Same Size, Horizontal and Vertical Spacing, Center in Form,
  Order, Lock Controls) act on the designer in front;
- in-place editing of menus, `ToolStrip`, `StatusStrip` and drop-down items;
- the component tray, user controls, localizable forms and a `.resx` resource
  editor with image pickers.

The controls are drawn by Basalt.Forms (Avalonia and SkiaSharp), so a form
looks and runs the same on macOS, Windows and Linux. Visual Studio WinForms
projects (`net6.0-windows` and later with `UseWindowsForms`) are retargeted at
build time off Windows without touching your project files. The limits of the
package are in [Known limitations of the IDE](#known-limitations-of-the-ide).

### The Avalonia designer

`.axaml` files open with a **Designer** and a **XAML** view. The preview is
rendered by the real Avalonia engine. You get a toolbox with a filter, drag and
drop, selection handles, a Properties panel, a **Structure** tree (also the way
to select a container its children cover), an Events panel, a row and column
editor for `Grid`, undo and redo, snap guides while dragging, rulers, zoom and
the Align commands in the **Designer** menu. **Project > Add Window...** creates
a window with Visual Basic or C# code-behind. VB.NET code-behind is generated and
kept in sync.

### Web, Razor and Blazor

New Solution creates Web API, Web Application, Razor Pages, MVC and Blazor
projects in Visual Basic. In the editor, `.vbhtml`, `.vbrazor` and `.vbpage`
files get colouring, completion (including the model's members), diagnostics,
hover, go to definition, find references and formatting from the same parser the
compiler uses. **View > Show Generated Code** (Ctrl+Alt+G) shows the Visual
Basic generated from a template, and the editor menu has **Extract to Partial
View...**. Details of the languages are in [Part II](#part-ii-razor-for-visual-basic-blazor-and-the-packages).

### Database client

The **Database** menu and **Database Explorer** (Ctrl+Alt+S) connect to:

- **SQLite** (a file),
- **SQL Server** (with Windows authentication, encrypt and trust-certificate
  options),
- **PostgreSQL** (with SSL mode),
- **MySQL and MariaDB**.

Add Connection... has a Test Connection button and an advanced connection
string field. The explorer lists tables, views, columns, indexes and foreign
keys. From a table you can **Open Data** (paged grid with a WHERE filter, add
and delete rows, set NULL, save or discard; views and tables without a primary
key are read-only), **Select Top 1000**, export CSV or copy with headers.
**New Query** (Ctrl+Alt+Q) opens a SQL editor: F5 or Ctrl+Enter runs, several
result sets and server messages are shown, a running statement can be
cancelled, and Ctrl+S saves the `.sql` file. Passwords are stored apart from
the settings file (see [AI assistant](#ai-assistant)).

### Git

Git uses the `git` executable, so it must be installed. The **Git** menu opens
the **Changes** panel (stage, discard, commit), **Branches**, **History** (with
the commit graph) and a **Diff** view, and runs Fetch, Pull and Push.
**Start a Repository Here** runs `git init -b main`, writes a `.gitignore` and
makes a first commit. The editor margin marks changed lines.

### Terminal

**View > New Terminal** (Ctrl+`) opens a terminal in the solution's folder. It
runs your shell (`$SHELL`, interactive) on macOS and Linux (a pseudo-terminal) and `%COMSPEC%`
(`cmd.exe`) on Windows through ConPTY; Settings > Terminal chooses another
shell, the font size and the scrollback. Prompts, history and line editing
work. The terminal shows text only: colours and cursor-addressing programs such
as `vim` are not rendered. The same panel hosts the tabs in which console
programs run (see [Build, Problems and Run](#build-problems-and-run) and
[The debugger](#the-debugger)); closing such a tab ends its program.

### Tests

The **Tests** panel discovers tests with `dotnet test --list-tests` and reads
results from the TRX report. The code handles the names that xUnit, NUnit and
MSTest print (including MSTest's own project SDK and the Microsoft Testing
Platform runner), and the Test Project template uses xUnit. Run All
(Ctrl+Alt+T), Run Selected, Run Failed and Debug Selected, with a filter by
name, class, namespace and outcome. "Open Source" in a test's menu jumps to it.

### AI assistant

The **Assistant** panel (Ctrl+Shift+A) talks to **Claude**, **Gemini** or
**ChatGPT**. Choose the assistant, model and API key in Settings > Assistant.

- Actions: Explain, Fix the error, Document, Generate tests, Ask. By default
  the open file and the current errors are sent with the question; both are
  switches in Settings.
- The assistant can read the solution with tools and **propose edits, which you
  review as diffs** and accept or reject one by one. Nothing is written without
  your approval.
- **Inline suggestions** (grey text; Tab accepts) are off by default. When on,
  about 4 KB before and 1 KB after the caret are sent to the provider; files
  that may hold secrets are not.
- **Where keys are kept.** On macOS the key goes to the Keychain. On Windows it
  goes to a file under `~/.basalt/keys` protected with DPAPI for your account.
  On Linux it is a file there readable by your user only, not encrypted. Keys
  are never in the settings file. Database passwords use the same store.

### Settings, themes and keyboard schemes

**Tools > Settings...** (Ctrl+,) has pages for Editor, Appearance, Keyboard,
Toolbar, Terminal, Debugging, Assistant and each language. Settings apply as you
change them and are saved in `~/.basalt/settings.json` (set `BASALT_SETTINGS` to
use another file).

- **Themes**: Light (the default), Dark, Follow the system, Visual Studio Blue,
  High Contrast Light, High Contrast Dark, Solarized Light, Solarized Dark.
  Density is Comfortable or Compact.
- **Keyboard schemes**: Basalt, Visual Studio, Visual Studio Code. Any command
  can be rebound; Settings warns when a key is already taken.
- **Command palette** (Ctrl+Shift+P) finds and runs any command by name.
- Panels dock, split and float; **View > Reset Layout** restores the default.

---

## Prerequisites

| Need | Detail |
|---|---|
| **.NET SDK 10** | `global.json` asks for 10.0.100 and rolls forward within version 10. Needed to build and run the IDE and by every build, run and test it starts. Check with `dotnet --version`. |
| **git** | Required for the Git features and to clone the repository. |
| **netcoredbg** | Required for debugging only. Without it the IDE tells you and runs the program without debugging. See below. |
| macOS | macOS 13 or later (the oldest macOS .NET 10 runs on). |
| Windows | Windows with the .NET 10 SDK; PowerShell for `build/setup-dev.ps1`. |
| Linux | Any distribution the .NET 10 SDK and Avalonia support. |
| Database servers | SQL Server, PostgreSQL and MySQL/MariaDB are servers you run yourself (Docker is one way); Basalt connects to them and does not start them. SQLite needs only a file. |

Only for the packages and extensions, not for the IDE: Node.js 20 or later (VS
Code extension), JDK 21 (Rider plugin), the Visual Studio extension development
workload (Visual Studio extension), clang (the QuickBASIC native tests).

### How Basalt finds netcoredbg

Basalt looks in this order and uses the first that exists:

1. the path in Settings > Debugging;
2. the `BASALT_NETCOREDBG` environment variable (path to the executable);
3. a `debugger/<rid>/` folder (or `debugger/`) next to the IDE;
4. `~/.basalt/debugger/<rid>/` (or `~/.basalt/debugger/`), where `<rid>` is
   `osx-arm64`, `osx-x64`, `win-x64`, `win-arm64`, `linux-x64` or `linux-arm64`;
5. your `PATH`.

The IDE does not download it. The setup script does:

```bash
build/setup-dev.sh --check      # macOS, Linux: list what is present or missing
build/setup-dev.sh              # also installs netcoredbg 3.2.0-1092 into ~/.basalt/debugger/<rid>
```

```powershell
build\setup-dev.ps1 -Check      # Windows
build\setup-dev.ps1             # also installs netcoredbg (and LLVM through winget)
```

`--check` / `-Check` installs nothing. Published netcoredbg builds exist for
linux-x64, linux-arm64, osx-arm64 and win-x64 (on Windows arm64 the x64 build
runs under emulation). For osx-x64 build netcoredbg from source and set
`BASALT_NETCOREDBG`.

---

## Install or build from source

Basalt is built from source. There is no installer and no published release;
the only packaging script is for macOS.

```bash
git clone https://github.com/montagnamarco/basalt.git
cd basalt
dotnet build Basalt.slnx
dotnet run --project src/Basalt.Shell
```

Open a solution directly by passing its path:

```bash
dotnet run --project src/Basalt.Shell -- path/to/MyApp.sln
```

**macOS app bundle.** `build/bundle-macos.sh [output-folder] [runtime]` publishes
the IDE self-contained (default runtime `osx-arm64`, output `artifacts/`) and
assembles `Basalt.app` with its icon, signed ad hoc:

```bash
build/bundle-macos.sh                      # Apple silicon
build/bundle-macos.sh artifacts osx-x64    # Intel
```

**Run from a checkout.** Windows Forms and web projects made by New Solution
refer to the repository's sources (`src/Basalt.Forms`, `src/Basalt.Razor.Vb`),
which the IDE finds by walking up from where it runs. If you move the IDE away
from the checkout, set `BASALT_FORMS_PATH` and `BASALT_RAZORVB_PATH` to the
`src` folder, or point `BASALT_WINFORMS_PACKAGES` to a folder holding the
`Basalt.WinForms` and `Basalt.Drawing` `.nupkg` files (made by
`build/pack.sh --winforms-only`).

Tests: `dotnet test tests/Basalt.Tests`.

---

## Your first project

1. **File > New Solution...** (Ctrl+Shift+N). Under *Visual Basic .NET* choose
   *Console Application*, give a name and a location, and press Create. A
   `.sln` and a project with `Program.vb` are written and opened.
2. Double-click `Program.vb` in **Solution Explorer**. Type `console.writeline("Hi")`
   and watch it become `Console.WriteLine("Hi")`. Press Ctrl+Space inside a call
   for members and Ctrl+Shift+Space for the signature.
3. **Build > Build Solution** (F6). Errors appear in **Problems**; double-click
   one to jump to it.
4. Add a line such as `Dim name = Console.ReadLine()` and press **Ctrl+F5**.
   The program runs in a **Terminal** tab named after the project: type a line
   and it arrives in `ReadLine`. When the program ends the tab shows
   `[process exited with code N]`.
5. Press F9 on a line to set a **breakpoint**, then press **F5**. The console
   program starts in a Terminal tab and stops at the breakpoint, even on its
   first line. Look at **Variables** and **Watch** (type a Visual Basic
   expression such as `name.Length`), type `?expression` in **Immediate**
   (Ctrl+Alt+I), step with F10 and F11, press F5 to continue (the Terminal tab
   comes back to the front) and stop with Shift+F5. If the IDE reports that
   netcoredbg was not found, see
   [Prerequisites](#how-basalt-finds-netcoredbg). A program with a window
   writes to **Output** instead of a Terminal tab.
6. Add a *Test Project* the same way (xUnit), then press Ctrl+Alt+T to run the
   tests from the **Tests** panel.
7. **Git > Start a Repository Here** puts the folder under version control.

For a form, choose *Windows Forms Application* in step 1 and open the form from
Solution Explorer to get the designer. For the web, choose *MVC*, *Razor Pages*
or *Blazor*.

---

## Keyboard shortcuts

Defaults of the **Basalt** scheme. "Ctrl" is **Cmd on macOS**, except for the
completion keys (member list and signature help), which stay on Control. Where the Visual
Studio (VS) or VS Code scheme differs, the table says so. Every shortcut can be
changed in Settings > Keyboard.

| Action | Shortcut |
|---|---|
| New Solution / Open / Save / Save All / Close Document | Ctrl+Shift+N / Ctrl+O / Ctrl+S / Ctrl+Shift+S / Ctrl+W |
| Undo / Redo | Ctrl+Z / Ctrl+Shift+Z (VS: Ctrl+Y) |
| Cut / Copy / Paste | Ctrl+X / Ctrl+C / Ctrl+V |
| Find / Replace in the file | Ctrl+F / Ctrl+H (then F3, Shift+F3) |
| Find in Files | Ctrl+Shift+F |
| Toggle Comment | Ctrl+/ |
| Command Palette | Ctrl+Shift+P |
| Settings | Ctrl+, |
| Member list / Signature help | Ctrl+Space or Ctrl+J / Ctrl+Shift+Space |
| Expand a snippet | shortcut word, then Tab Tab |
| Go to Definition / Find References | F12 / Shift+F12 |
| Go to File | Ctrl+Shift+O (VS Code: Ctrl+P) |
| Go to Symbol in Solution / Go to Line | Ctrl+T / Ctrl+G |
| Navigate Backward / Forward | Ctrl+- / Ctrl+Shift+- |
| Rename / Quick Actions | F2 / Ctrl+. |
| Extract Method / Extract Variable / Inline Variable | Ctrl+Alt+M / Ctrl+Alt+V / Ctrl+Alt+N |
| Sort and Remove Imports | Ctrl+Alt+O |
| Show Generated Code | Ctrl+Alt+G |
| Build Solution | F6 (VS: Ctrl+Shift+B) |
| Start Debugging / Start Without Debugging | F5 / Ctrl+F5 |
| Stop Debugging | Shift+F5 |
| Step Over / Step Into / Step Out | F10 / F11 / Shift+F11 |
| Toggle Breakpoint / Run to Cursor | F9 / Ctrl+F10 |
| Run All Tests | Ctrl+Alt+T |
| New Terminal | Ctrl+` |
| Immediate window | Ctrl+Alt+I |
| Assistant | Ctrl+Shift+A |
| Database Explorer / New Query | Ctrl+Alt+S / Ctrl+Alt+Q |
| View Code / View Designer (forms and windows) | F7 / Shift+F7 |
| Run the query (in a query tab) | F5 or Ctrl+Enter |
| Designer: Actual Size / Fit to Window / Select Container | Ctrl+0 / Ctrl+9 / Esc |

Commands with no default key (use the menu or the palette, or bind one): Break
All, Continue as a separate command, Rebuild and Clean, Run Selected / Failed
Tests, Extract Constant, Change Signature, Make executable, Object Browser,
Format Selection.

Two-step shortcuts such as Ctrl+K, Ctrl+D (Format Document), Ctrl+K, Ctrl+I
(Quick Info) and Ctrl+M, Ctrl+M (fold) are listed in the keyboard settings, but
the key handler matches single key presses only, so they do not fire from the
keyboard. Use Edit > Format Document or the palette. In the VS Code scheme
Format Document is Ctrl+Shift+I and works.

---

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| "The debugger (netcoredbg) was not found" | Install it with `build/setup-dev.sh` / `build/setup-dev.ps1`, or set `BASALT_NETCOREDBG`, or put it on `PATH`. The program runs without debugging meanwhile. |
| `build/setup-dev.sh --check` shows `MISSING` | Read the "Needed for" column. Only the .NET SDK and git are needed for everything; the rest are optional. |
| No MSBuild or SDK found when opening a solution | Install the .NET 10 SDK and make sure `dotnet --version` works in a terminal. |
| Windows Forms or web project cannot restore Basalt packages | The project refers to the checkout's `src`. Set `BASALT_FORMS_PATH`, `BASALT_RAZORVB_PATH` or `BASALT_WINFORMS_PACKAGES` (see [Install](#install-or-build-from-source)). |
| "The terminal could not start" | The configured shell must exist; clear Settings > Terminal > Shell to use the default. |
| A console program started with F5 sits at its start and does not run | It waits for the debugger to attach, for at most 30 seconds, then goes on without it. If the debugger could not attach, the failure is reported. Check that netcoredbg is found (see above) and press Shift+F5 before trying again. |
| `Console.ReadLine` gets no input | Only a console program run in a Terminal tab (Ctrl+F5 or F5) can be typed to. A program with a window, or a web project, writes to Output and has no input. |
| Launch settings are ignored | For a Visual Basic project the file is `My Project/launchSettings.json`, not `Properties/`. Basalt moves an older `Properties/launchSettings.json` there when it does not clash with an existing file. |
| A watch says it cannot evaluate an expression | netcoredbg evaluates C#, so only the Visual Basic that Basalt can translate works. See the debugger entry in [Known limitations of the IDE](#known-limitations-of-the-ide); try a simpler expression, or a member of a local. |
| Immediate says statements cannot run | Not while debugging. Use `?expression`, or stop the session to run a statement in a separate process. |
| A key does nothing | Check Settings > Keyboard for conflicts and for the scheme in force; chords do not work (see above). |
| Settings misbehave | Close the IDE and delete or fix `~/.basalt/settings.json`; an unreadable file is replaced by the defaults. |
| API key not remembered on Linux | Keys are kept under `~/.basalt/keys`; check that folder is writable. |

---

## Known limitations of the IDE

- **Visual Basic only.** C# has no language service in Basalt: `.cs` files get
  colour and nothing else. `.csproj` and `.sln` / `.slnx` files do open.
- **Two-step (chord) shortcuts** do not fire; see
  [Keyboard shortcuts](#keyboard-shortcuts).
- **Terminal** shows text only: no colours, no full-screen programs. The Windows
  terminal is built on ConPTY.
- **Immediate window** does not run statements while debugging.
- **Debugger.** These were measured on netcoredbg 3.2.0.
  - netcoredbg evaluates C#, so Basalt translates Visual Basic expressions
    before it sends them. Many forms work (member access, calls, arithmetic,
    comparisons, `Mod`, `Not`, `AndAlso`, indexing, `Me`), but `If(...)`
    ternaries, `Is Nothing` comparisons, casts, `TypeOf`,
    LINQ and the members of arrays cannot be evaluated in watches. When the
    debugger refuses an expression it says so.
  - After an unhandled exception the program's exit code is reported as 0.
  - Breakpoint conditions go through a simpler translator than watches do. It
    rewrites the comparisons people usually type (`i = 4`, `<>`, `AndAlso`) and
    passes anything else through untouched.
  - Edit and Continue is not available, and the value of a variable cannot be
    changed in a compiled program.
  - The Terminal run and debug of console programs was verified on the
    macOS and Linux (pseudo-terminal) paths. Windows uses ConPTY for the same
    thing, but it has not been run in this round.
  - netcoredbg is not bundled with the IDE.
- **Windows Forms** is the public WinForms API on Avalonia and SkiaSharp. Not
  supported: native window handles, `WndProc` / `DefWndProc`, `CreateParams`,
  P/Invoke needing an `HWND`, ActiveX / COM controls (`AxHost`) and the Internet
  Explorer based `WebBrowser`. Whether a Visual Studio project needs retargeting
  is judged from the project XML alone; `UseWindowsForms` that comes only from
  `Directory.Build.props` is not seen. The IDE designer works on the Visual Basic
  `.vb` / `.Designer.vb` pair.
- **Refactorings** work on Visual Basic projects.
- **No installer or release.** Build from source; macOS has a bundle script.
- **Packages are not on nuget.org.** See Part II.
- **Visual Basic 6 and QuickBASIC.** The code for both is in the repository, but
  the IDE does not offer them (a build-time switch that is off by default, with
  no setting for it). Treat them as experimental.
- **Database client** lists the four providers above; other databases are
  "not supported yet".
- **Extensions** for Rider, Visual Studio and VS Code have not been run end to
  end inside their editors yet; see `extensions/README.md`.

---

## FAQ

**Do I need Windows?** No. Basalt runs on macOS, Windows and Linux, and so do
the Windows Forms projects it designs.

**Can it open my Visual Studio solution?** It opens `.sln`, `.slnx`, `.vbproj`
and `.csproj`. Visual Basic projects are the supported case. A Visual Studio
Windows Forms project is retargeted at build time off Windows, without editing
your files.

**Is it the same compiler as Visual Studio's?** The build is `dotnet build`
with the .NET SDK, and IntelliSense is Roslyn, which is also what Visual Studio
uses for Visual Basic.

**Can my console program read from the keyboard?** Yes. Run it with Ctrl+F5 or
debug it with F5 and it runs in a Terminal tab, where what you type reaches
`Console.ReadLine`. Programs with a window, and web projects, write to Output.

**Do I have to install a debugger?** For debugging, yes: netcoredbg, which the
setup script installs. Everything else works without it.

**Can I use my Visual Studio keys?** Choose the Visual Studio scheme in
Settings > Keyboard. Most defaults already match.

**Where are my settings?** `~/.basalt/settings.json` (`%USERPROFILE%\.basalt\`
on Windows). Debugger copies live in `~/.basalt/debugger/`, API keys on
Windows and Linux in `~/.basalt/keys`.

**Does the assistant send my code anywhere?** Only to the provider you chose
(Claude, Gemini or ChatGPT), only when you ask, and with the open file and
errors only if those switches are on. Inline suggestions are off by default.

**Is there VB6 support?** Not in the IDE you get by default; see the
limitations.

**Are the packages on nuget.org?** Not yet. Part II explains how to build and
use them locally.

---

# Part II: Razor for Visual Basic, Blazor and the packages

Everything below works without the IDE, in Rider, Visual Studio and VS Code.

---

## The `.vbhtml` story

Razor's compiler on modern .NET is `Microsoft.AspNetCore.Razor.Language`. Its
pipeline runs in phases — parse, syntax tree, intermediate document, lowering,
code generation — and most of those phases are extension points: eight
interfaces, forty-five public intermediate node types, all documented and all
replaceable.

The parser is not one of them. C# is hardwired into it. Feed it `@If ok Then`
and it returns a `CSharpExpression` holding `If` followed by an `HtmlContent`
holding `ok Then` — it read the identifier and stopped, because in C# that is
where an expression ends. Custom directives cannot help: their code blocks are
brace-delimited by definition, and VB has no braces. `RazorSyntaxTree` does not
even expose its root node, so the tree cannot be rewritten after the fact.

So Basalt has its own parser — the one thing that could not be reused — and
reuses the rest of the idea: the same phase structure, the same source-mapping
discipline, the same generated-class shape that ASP.NET Core already knows how
to load.

The practical consequence: the runtime does not need to be told anything special.
A view Basalt compiled looks to ASP.NET Core exactly like a view the C# Razor
compiler produced.

---

## Quick start

```bash
dotnet new install Basalt.Templates
dotnet new mvc -lang VB -o MySite
cd MySite && dotnet run
```

That is the whole thing. `Views/Home/Index.vbhtml` is a real Razor view, it
compiles at build time, and it is served by stock ASP.NET Core MVC.

> **Not on nuget.org yet.** All four packages build and are verified end to end
> — `build/pack.sh` installs the templates, creates a project through
> `dotnet new`, and checks the views really became classes (see
> [Building the packages and the extensions](#building-the-packages-and-the-extensions)). They have not been
> published. Until they are, build them locally and restore from `artifacts/`:
>
> ```bash
> build/pack.sh
> dotnet new install artifacts/Basalt.Templates.1.0.0.nupkg
> ```

In an existing VB.NET web project, two steps:

```xml
<PackageReference Include="Basalt.Razor.Vb.AspNetCore" Version="1.0.0" />
```

```vb
builder.Services.AddControllersWithViews()
builder.Services.AddVbViews()   ' the single call that makes .vbhtml work
```

`AddVbViews` registers the view engine hooks that let MVC find the views, and
the package pulls in `Basalt.Razor.Vb` — the source generator that turns every
`.vbhtml` in the project into a compiled class. Referencing `Basalt.Razor.Vb`
directly as well is fine and changes nothing.

---

## The packages

Four packages, each answering a different question your project asks — at
restore time, at build time, or at start-up. None of them depends on the IDE.

| Package | What it is for | When you need it |
|---|---|---|
| **`Basalt.Razor.Vb.AspNetCore`** | Serves `.vbhtml` views from MVC and Razor Pages | Any web project with Razor views. **Start here** — it pulls in the generator for you |
| **`Basalt.Razor.Vb`** | The compiler: source generator, parser, runtime | Comes in automatically with the one above. Reference it directly only for views outside a web app |
| **`Basalt.Razor.Vb.Hosting`** | `.vbpage` pages, compiled while the site runs | Only for Classic ASP-style pages |
| **`Basalt.Templates`** | `dotnet new` templates for VB web projects | Installed once per machine, not referenced by a project |

### `Basalt.Razor.Vb` — turns views into classes

The engine. It carries three things that are useless apart:

- the **source generator**, which runs inside the compiler;
- the **runtime** each generated view inherits from;
- the **MSBuild props and targets** that tell the build which files are views.

At build time the props collect every `.vbhtml` in the project and hand them to
the compiler as `AdditionalFiles`. The generator parses each one and emits a
Visual Basic class inheriting `RazorPage(Of TModel)`, carrying a
`[RazorCompiledItem]` attribute with its path, so ASP.NET Core finds it the way
it finds a compiled C# view.

`Views/Home/Index.vbhtml` becomes the type `YourSite.Views.Home.Index` inside
your own assembly. Nothing is compiled at runtime, so there is no first-request
delay and no Razor SDK dependency in production.

### `Basalt.Razor.Vb.AspNetCore` — makes MVC find them

The generator produces the classes; ASP.NET Core does not know they exist,
because it looks for `.cshtml`. One line fixes that:

```vb
builder.Services.AddVbViews()
```

It registers the three hooks described in
[Serving views under ASP.NET Core](#serving-views-under-aspnet-core), and it
**depends on `Basalt.Razor.Vb`** — so referencing this one alone is enough.
Referencing both is fine and changes nothing.

This is the package to install for an MVC or Razor Pages site. A Web API with
no views needs neither.

### `Basalt.Razor.Vb.Hosting` — `.vbpage` pages

Only for the Classic ASP-style pages. Separate for a concrete reason: it
carries the Visual Basic compiler, which is tens of megabytes, and a site that
compiles its pages at build time should not ship a compiler to production to
get them.

```vb
app.MapVbPagesFromDisk(...)   ' development: save, refresh, see the change
app.MapVbPages()              ' production: compiled in
```

### `Basalt.Templates` — `dotnet new` templates

Installed on the machine rather than referenced by a project:

```bash
dotnet new install Basalt.Templates
```

After that, `dotnet new mvc -lang VB` and its siblings work from any folder.
See [Project templates](#project-templates). Uninstall with
`dotnet new uninstall Basalt.Templates`.

### Which do I install?

| What you are building | Reference |
|---|---|
| MVC or Razor Pages site with `.vbhtml` views | `Basalt.Razor.Vb.AspNetCore` |
| Web API, no views | nothing |
| A site of `.vbpage` pages | `Basalt.Razor.Vb.Hosting` |
| Razor templates outside a web app (mail, reports) | `Basalt.Razor.Vb` |

---

## How a view becomes a class

Nothing happens at runtime. The work is done by a Roslyn incremental source
generator during compilation, in four steps.

**1. The `.vbhtml` files are collected.** The build adds them as
`AdditionalFiles`; the generator reads them from there. The project's
`RootNamespace` is passed through `CompilerVisibleProperty`, because the
generated class has to land in the namespace the rest of the project expects.

**2. The parser produces a document.** A hand-written recursive-descent parser
that understands Visual Basic block structure — `If … Then` / `End If`,
`For Each` / `Next`, `Using`, `Select Case`, and the rest — because a VB block
ends with a keyword, not a brace.

**3. The writer emits Visual Basic.** One class per view, inheriting
`RazorPage(Of TModel)` (or `RazorPages.Page` for a `.cshtml`-style page),
carrying a `[RazorCompiledItem]` attribute so ASP.NET Core discovers it without
being told, as it does a C# view or page.

**4. Source mappings are recorded.** Every fragment of Visual Basic that came
from the template carries a mapping back to its original line and column, and
the generated file is stitched with `#ExternalSource` directives so the VB
compiler blames the right file. This is what makes an error in a view point at
the view rather than at generated code nobody wrote.

Those mappings are also what the language server runs on — see
[IDE support](#ide-support-for-vbhtml). They are the load-bearing part of the whole design,
and the one most easily got subtly wrong: an off-by-twelve in a mapping does not
crash anything, it just makes IntelliSense describe the token next door.

---

## The language

Razor syntax, with Visual Basic in the code positions.

### Expressions

```vbhtml
<p>Hello, @Model.Name</p>
<p>@(price * quantity)</p>
<p>@Await Helper.RenderAsync()</p>
```

Implicit expressions follow member access and call syntax and stop where the
expression stops. Explicit `@( … )` takes anything. `@Await` is understood in
expression position. Output is HTML-encoded; `@Html.Raw( … )` opts out.

### Blocks

```vbhtml
@If user.IsAdmin Then
    <p>Welcome back.</p>
@ElseIf user.IsKnown Then
    <p>Hello again.</p>
@Else
    <p>Please sign in.</p>
@End If

@For Each item In Model.Items
    <li>@item.Name</li>
@Next

@Using Html.BeginForm()
    <input type="submit" value="Go" />
@End Using

@Select Case Model.Kind
    @Case "a"
        <p>First</p>
    @Case Else
        <p>Other</p>
@End Select
```

`While`/`End While`, `Do`/`Loop`, `Try`/`Catch`/`Finally` and `With` work the
same way. Markup and code nest freely in both directions.

### Directives

| Directive | Meaning |
|---|---|
| `@ModelType T` | the view's model type |
| `@Imports Ns` | a namespace import for this view |
| `@Inherits Base` | an explicit base class |
| `Layout = "…"` inside `@Code` | the layout to wrap this view in (`@Layout` as a directive is not supported yet) |
| `@Section Name … @End Section` | a named section for the layout |
| `@Code … @End Code` | a block of statements |
| `@Functions … @End Functions` | methods and fields on the generated class |
| `@Inject T As Name` | dependency injection into the view |
| `@Page` | marks a Razor Page |
| `@Namespace Ns` | overrides the generated namespace |

`RenderBody` and `RenderSection` work in layouts as they do in C#.

### Tag helpers

With `@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers` in `_ViewImports.vbhtml`
(the templates add it), tag helpers run exactly as in a `.cshtml`: the generator
finds every `ITagHelper` in the assemblies you name — the framework's and your
own, written in VB — and emits the same runtime calls the C# compiler does.
Forms get their antiforgery token, `asp-for` its `data-val-*` attributes and
`[Display]` label, `select asp-items`, `asp-route-*`, `<partial>`,
`<environment>` and the rest all work. `@removeTagHelper`, `@tagHelperPrefix` and
`<!element>` opt-out work as in C#.

Without `@addTagHelper` the older compile-time rewriting of `asp-controller`,
`asp-action`, `asp-page` and `asp-for` still applies, so existing views keep
working. `<vc:name>` view component tags are not generated yet.

---

## Serving views under ASP.NET Core

`AddVbViews()` registers two things, and each answers a specific question the
framework asks:

- an **`IViewLocationExpander`**, so MVC looks for `.vbhtml` beside `.cshtml`
  when resolving a view name;
- a **`CompiledRazorAssemblyPart`** for the compiled views, so discovery finds
  them the way it finds C# ones.

Every view and page carries a `[RazorCompiledItem]` attribute naming its path,
exactly as a compiled C# view does, so MVC's own factory turns a resolved path
into the generated class. Nothing replaces it, which is why `.cshtml` views keep
working beside `.vbhtml` ones.

That is the entire integration surface. Controllers, filters, model binding,
validation, dependency injection, layouts, sections, partials and view
components are stock ASP.NET Core and were never touched.

Web API needs nothing at all — there are no views — but the same project can
serve both.

---

## Blazor components

`.razor` is C# only, the same way `.cshtml` is. A `.vbrazor` file is the
Visual Basic counterpart: same Razor syntax, compiled into a real
`ComponentBase` rather than into a class that writes markup.

```vbrazor
@Page "/counter"
@rendermode InteractiveServer

<h1>@Title</h1>
<p role="status">Current count: @currentCount</p>
<button @onclick="AddressOf IncrementCount">Click me</button>

@Code
    <Global.Microsoft.AspNetCore.Components.Parameter>
    Public Property Title As String = "Counter"

    Private currentCount As Integer

    Private Sub IncrementCount()
        currentCount += 1
    End Sub
End Code
```

```bash
dotnet new blazor -lang VB -o MyApp                         # Server
dotnet new blazor -lang VB --interactivity Auto -o MyApp    # None, Server, WebAssembly, Auto
```

The template is a Blazor Web App, as the C# one is: `App.vbrazor` with
`<HeadOutlet />` and `blazor.web.js`, `Routes.vbrazor`, a `MainLayout` and
`NavMenu`, and a `Counter` page. `--interactivity` chooses where it runs:
`Server` (the default) keeps one project; `WebAssembly` and `Auto` add a
Visual Basic `MyApp.Client` project, compiled for the browser, which holds the
`Counter`; `None` is static rendering without it. **All four were generated,
built and run**: in a headless browser the counter counted under Server,
WebAssembly and Auto.

The `.Client` project's `Program.vb` has a `Sub Main` that starts the host
without awaiting it: Visual Basic has no `Async Main`, and in the browser the
runtime stays alive after `Main` returns.

A Visual Basic project has to set `<RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>`
(the template does): the SDK switches it on by itself only for projects with
`.razor` files, and without it `_framework/blazor.web.js` is a 404 and no
component is ever interactive.

### What the compiler does differently

A component does not write HTML out as text. It builds a **render tree** —
`OpenElement`, `AddContent`, `CloseElement` — which Blazor diffs against the
previous one to decide what to change on screen. A component that wrote markup
as text would compile and render nothing, which is why this is a separate
writer rather than a flag on the view one.

`@Page` supplies the route. `@Functions` becomes the component's own members,
so `<Parameter>` properties and event handlers live there.

### Events and directive attributes

```vbrazor
<button @onclick="Sub() count += 1">+1</button>
<button @onclick="AddressOf SaveAsync" @onclick:preventDefault>Save</button>
<input @onkeydown="Sub(e As KeyboardEventArgs) last = e.Key" />
<div class="card @kind" @key="item.Id" @attributes="extra"></div>
<input @ref="box" />
<Chart @rendermode="InteractiveServer" />
```

An event handler is Visual Basic: a lambda, `AddressOf` a method taking the
event's arguments or none, or a method returning a `Task`. It is wrapped in an
`EventCallback` typed by the event's arguments (`MouseEventArgs` for
`@onclick`, `ChangeEventArgs` for `@oninput`...), as the C# compiler types it,
and mapped back to the template, so completion and breakpoints work inside it.
`:preventDefault` and `:stopPropagation`, `@key`, `@ref`, `@attributes`,
`@formname` and `@rendermode` on a component are written as the frames Blazor
expects. A value mixing text and expressions, `class="card @kind"`, is one
attribute. `onclick="@AddressOf Go"` without the marker still works.

A top-level `@Code` block that declares members — `Private count As Integer`,
`Sub Increment()`, a `<Parameter>` property — becomes members too, as `@code`
does in a `.razor`; one that runs statements (`Dim`, a loop around markup)
stays in the render tree. The class is `Partial`, so `Counter.vbrazor.vb` can
hold its code-behind. `_Imports.vbrazor` lends `@Imports`, `@Inject`,
`@Layout`, `@Inherits`, `@Attribute` and `@Implements` to every component in
its folder and below, the nearest file winning; `@Namespace`, `@Attribute` and
`@Implements` work in a component as they do in C#.

### Components inside components

A capitalised tag is another component, the way it is in Razor — the first
letter is the whole distinction, since element names are lower case and a
component is a class.

```vbrazor
<Saluto Nome="Marco" />

<Box>
    <p>Whatever goes here becomes the box's ChildContent.</p>
</Box>
```

What sits between the tags is passed as a `RenderFragment`, so the component
decides where to put it.

### Binding, cascading and generics

```vbrazor
<input @bind="@nome" />
<TextBox @bind-Value="@nome" />

<CascadingValue Value="@tema">
    <Child />
</CascadingValue>

<List(Of String) Items="@names" />
```

`@bind` becomes the pair of attributes it really is: the value out through
`BindConverter`, and the change back through `CreateBinder`, which parses what
arrives into the target's type. A checkbox binds `checked`; `@bind:event`,
`@bind:format`, `@bind:culture`, `@bind:after` and `@bind:get`/`@bind:set` work
as in C#. On a component `@bind-Value` also sets `ValueExpression`, which
`InputText` and the other form inputs need.

### Parameters, fragments and generics

The generator reads every component's parameters from the compilation before
it writes any of them, as the C# compiler does, so a tag is written knowing
what it names — whether the component is a `.vbrazor`, a Visual Basic class or
comes from a library:

```vbrazor
<Card>
    <Header><b>Orders</b></Header>
    <Body>@orders.Count open</Body>
</Card>

<Grid(Of Order) Items="@orders" PageSize="20" OnSelect="Sub(order) picked = order">
    <Row Context="order"><td>@order.Number</td></Row>
</Grid>
```

A tag named after a `RenderFragment` parameter is that parameter's content; a
`RenderFragment(Of T)` hands its value to the content as `context`, or under
the name `Context="…"` gives it. A literal is Visual Basic for any parameter
that is not a `String` or `Object` (`PageSize="20"` is the number), and a
handler for an `EventCallback` parameter is wrapped in one, typed by its
argument. `@typeparam TItem` (with Visual Basic constraints,
`@typeparam TItem As {IComparable}`) makes a component generic.

A generic component's type arguments are inferred from the values its tag
gives, as C# infers them — `<Grid Items="@orders">` is a `Grid(Of Order)`,
`<InputSelect @bind-Value="level">` an `InputSelect(Of Integer)`,
`<CascadingValue Value="@theme">` cascades a `Theme` — by asking the Visual
Basic compiler to infer them for a generic function with the same parameters.
A value naming a local of the render tree, such as a loop variable, cannot be
inferred this way yet; write the type argument in the tag there,
`<Grid(Of Order) …>`.

A value for a typed parameter is converted to the parameter's type, as C#
checks it, so `<ValidationMessage For="@(Function() model.Name)" />` gets the
expression tree it needs and a value of the wrong type is a compile error on
the template. Forms work as in C#: `EditForm`, `DataAnnotationsValidator`,
the `Input*` components with `@bind-Value`, `ValidationMessage` — the MVC
sample's `/signup` page is validated and submitted in a browser by the
end-to-end tests.

Every component imports what a `.razor` file does: `Microsoft.AspNetCore.Components`,
`System.Collections.Generic`, `System.Linq` and `System.Threading.Tasks`.

### CSS isolation

`Counter.vbrazor.css` beside `Counter.vbrazor` styles that component alone, as
`Counter.razor.css` does in C#: the package hands the stylesheet to the SDK's
own scoped-CSS pipeline, which rewrites its selectors with the component's
scope (`p[b-vrp9ab9f78]`) and bundles every component's styles into
`YourApp.styles.css`, and the generator writes the same scope on each of the
component's elements. `::deep` reaches into child components, as in C#. The
Blazor template's layout and menu are styled this way; in the MVC sample a
browser test reads the colour `Clicker.vbrazor.css` gives Clicker's
paragraphs, and not another page's.

### Collocated JavaScript

`Counter.vbrazor.js` beside `Counter.vbrazor` is served at
`./Components/Counter.vbrazor.js`, as `Counter.razor.js` is, for the component
to import as a module:

```vbrazor
@Inject IJSRuntime JS
...
scripts = Await JS.InvokeAsync(Of IJSObjectReference)("import", "./Components/Counter.vbrazor.js")
shouted = Await scripts.InvokeAsync(Of String)("shout", name)
```

The MVC sample's Clicker does exactly this, and a browser test calls it.

### Component libraries

A Razor class library can be written in Visual Basic: a project on
`Microsoft.NET.Sdk.Razor` referencing `Basalt.Razor.Vb`, its `.vbrazor`
components used from an app like any other library's — found, typed and
inferred from the compiled library. Its `.vbrazor.css` travel with it: the
app's `YourApp.styles.css` imports `_content/YourLibrary/…bundle.scp.css`, as
for a C# library. Checked by rendering a library's components from an app in
the tests, and by running an app from the template with such a library.

### Where components live

A component is namespaced by its folders from the project, as a `.razor` file
is: `Components/Pages/Home.vbrazor` compiles into `YourApp.Components.Pages.Home`,
`Components/Layout/MainLayout.vbrazor` into `YourApp.Components.Layout.MainLayout`,
and a component beside the project file into the root namespace. A component
named from another folder is imported the C# way, with `@Imports` in
`_Imports.vbrazor`.

Until September 2026 every component went into `Components`, plus the folders
below a `Pages` folder. A project written against that can keep it with
`<VbRazorComponentNamespaces>Legacy</VbRazorComponentNamespaces>`.

---

## `.vbpage` pages: Classic ASP, modernised

> **Under active development.** The parser, the compiler, the hosting and the
> editor support all work and are covered by tests — but this is the newest
> part of the repository and the one most likely to change. Expect rough
> edges, and say so when you find them.

Alongside Razor there is a second, deliberately simpler model, and it is a
philosophy more than a feature: the one Classic ASP and early PHP had. A page
is a file, a URL is a path, there are no controllers or models to declare, and
you save the file and refresh the browser.

That directness is what made those tools easy to start with, and what the
modern frameworks traded away for structure. `.vbpage` is an attempt to have it
back on top of ASP.NET Core — the old feel, with a real compiler underneath and
none of the old drawbacks.

```vbpage
<%@ Import Namespace="System.Linq" %>
<html>
  <body>
    <h1>Hello</h1>
    <% For i = 1 To 3 %>
      <p>Line <%= i %></p>
    <% Next %>
  </body>
</html>
```

`<% %>` for statements, `<%= %>` for encoded output, `<%== %>` for raw output,
`<%! %>` for member declarations, `<%@ Import %>` for namespaces.

In development, `MapVbPagesFromDisk` compiles pages on demand and watches the
folder: **save the file, refresh the browser, see the change** — no build, no
restart. In production the same pages are compiled into the assembly.

Note the *or* in the sample's comment, and mean it: if pages are both compiled
in and mapped from disk, the compiled one answers first and your edit is never
read — which looks exactly like hot reload being broken.

---

## IDE support for `.vbhtml`

This is the part that makes `.vbhtml` usable rather than merely compilable, and
it is the same implementation everywhere — including in editors that have never
heard of Visual Basic Razor.

### One server, four editors

`vbrazor-langserver` is a Language Server Protocol server built on the **same
parser the compiler uses**. That is a deliberate constraint: a TextMate grammar
that highlights and a compiler that compiles are two opinions about one
language, free to disagree. Here there is one opinion.

Behind the parser sits Roslyn with the project's real compilation loaded through
MSBuild. When you hover a member in a view, the server maps the caret through
the source mappings into generated Visual Basic, asks Roslyn, and maps the
answer back. Your types, your references, your project.

| Feature | |
|---|---|
| Semantic highlighting | 8 token types — keywords, strings, numbers, comments, operators, properties, variables, directives |
| Completion | members, locals, types, directives; Roslyn-backed inside code, markup-aware in HTML |
| Hover | full signatures and documentation |
| Go to definition | into the project and into referenced assemblies |
| Find references | across views and Visual Basic files alike |
| Diagnostics | parse errors immediately, semantic errors as the compilation catches up |
| Signature help, document symbols, folding, highlight, linked editing | |
| Formatting | on request, on save, and while typing — see below |

### Typing that behaves like Visual Basic

The half that made VB feel like VB, in every editor rather than only in
Basalt: type `end if` and it becomes `End If`; type `if x=1 then` and it
becomes `If x = 1 Then`. Blocks inside `@Code` are indented to their depth,
and `<% %>` blocks in a `.vbpage` page are tidied on their own line.

The markup is left exactly as written. Where a tag breaks and how attributes
wrap are opinions people hold strongly, and a formatter that rearranged a
template's HTML is one they switch off — taking the code half with it.

Keyword spelling comes from Roslyn's own tables and spacing from Roslyn's
formatter, rather than from a list kept here: the rules for which operators
take spaces and which contextual keywords are keywords belong to the compiler.
It is the same formatter the IDE runs, so a file does not change shape
depending on which editor last touched it, and it leaves a half-written block
alone — a template is unparseable most of the time it is being typed into.

| Editor | How |
|---|---|
| Rider | Reformat Code (⌥⌘L), and while typing |
| Visual Studio | Format Document (Ctrl+K, Ctrl+D), and while typing |
| VS Code | on save and while typing, enabled by the extension for these files only |
| Basalt | Edit > Format Document, on save, and while typing |

Both `.vbhtml` and `.vbpage` are handled.

### Rider

`extensions/rider-vbrazor` — an IntelliJ Platform plugin registering the file
types, the grammar and the LSP client. Listed as **Basalt**. Install the built
zip via **Settings → Plugins → ⚙ → Install Plugin from Disk**.

### Visual Studio

`extensions/vs-vbrazor` — a VSIX doing the same for Visual Studio 2022, listed as **Basalt**.

### VS Code

`extensions/vscode-vbrazor` — a standard extension, listed as **Basalt**; the server ships inside it.

### Basalt

Built in. No extension needed — the IDE runs the same parser directly.

---

## Project templates

```bash
dotnet new install Basalt.Templates
```

Installed once per machine, not referenced by a project. Then, from anywhere:

| Command | What you get |
|---|---|
| `dotnet new mvc -lang VB` | MVC with `.vbhtml` views |
| `dotnet new webapp -lang VB` | Razor Pages |
| `dotnet new webapi -lang VB` | Web API |
| `dotnet new web -lang VB` | empty ASP.NET Core |
| `dotnet new blazor -lang VB` | a Blazor app with `.vbrazor` components |
| `dotnet new vbpages` | a site of `.vbpage` pages |

These are the same short names the C# templates use, so `-lang VB` selects the
Visual Basic one exactly the way `-lang F#` selects F#.

**A caveat about Rider's New Solution dialog.** Rider reads web templates from
its own bundled .NET SDK rather than from the user template cache, so VB will
not appear in the web section there no matter what the template declares. This
is a limitation on Rider's side, measured rather than assumed. `dotnet new` from
a terminal works, and the resulting project opens and builds in Rider normally.

---

## QuickBASIC, for the fun of it

This one is here because it was enjoyable to write, not because anyone asked
for it. Basalt is an IDE for Visual Basic *and its dialects*, and QuickBASIC is
where the family started.

It is a complete implementation all the same: a lexer, a parser, a symbol
table, an interpreter for running a program straight from the editor, and a
native compiler that translates to C and hands it to clang — so a `.bas` file
becomes a real executable. Cross-compiling works too, and the test suite
verifies it by building for the other architecture and reading back what
`file` says about the result.

`PRINT`, `INPUT`, `GOTO`, `GOSUB`, `DIM`, `WHILE`/`WEND`, `SELECT CASE`, `SUB`
and `FUNCTION` are understood, with editor support alongside the rest.

The IDE you get by default does not offer QuickBASIC (or Visual Basic 6): both
sit behind a feature switch, `IdeFeatures.LegacyBasic` in `src/Basalt.Core`,
that is off. The code behind it is unchanged, builds, and is tested; treat it as
experimental.

See [`src/Basalt.QuickBasic/CROSS-COMPILING.md`](src/Basalt.QuickBasic/CROSS-COMPILING.md)
for which targets build out of the box and which need a sysroot first.

---

## Building the packages and the extensions

Extensions need **Node 20+** (VS Code), **JDK 21** (Rider; the Gradle wrapper is
included, so no separate Gradle install) or the "Visual Studio extension
development" workload (Visual Studio).

The Rider plugin, server included:

```bash
dotnet publish src/Basalt.Razor.Vb.LanguageServer -c Release -r osx-arm64 \
  --self-contained -o extensions/rider-vbrazor/server
cd extensions/rider-vbrazor && ./gradlew buildPlugin
```

The zip lands in `build/distributions/`. Substitute your own runtime identifier
for `osx-arm64`. VS Code: `npm ci && npm run compile` in
`extensions/vscode-vbrazor`.

`build/acceptance.ps1` runs the core, HTTP and browser test projects
sequentially and writes a Markdown report, logs, TRX results and browser
screenshots under `artifacts/acceptance/`. Run it with PowerShell 5.1 on Windows
or `pwsh` elsewhere; `-Suite Web,Browser` selects projects, `-List` shows the
selection without running tests, and `-Filter` passes an explicit test filter to
the selected projects and records that narrower scope in the report. Use
`-NoBuild` only after building the intended test binaries, for example when the
running IDE locks its executable and only its DLL has been rebuilt. It installs
nothing: browser tests use installed Edge on Windows or Playwright Chromium, and
debugger tests need netcoredbg. Exit 1 means failure; exit 2 means incomplete
coverage because tests were skipped. A passing selected run does not establish
every IDE journey or equivalence with Visual Studio.

### Building the packages

```bash
build/pack.sh              # macOS, Linux
build/pack.ps1             # Windows
```

Packs the NuGet packages into `artifacts/` (the four Razor for VB packages,
`Basalt.Drawing` and `Basalt.WinForms`), then proves they work. It reads the
template package's own layout, installs it, creates a project through
`dotnet new mvc -lang VB`, restores it **by version from a feed** with no
reference to this repository's sources, builds it, and checks the `.vbhtml`
views became classes in the assembly. The two Windows Forms packages are checked
the same way with a throwaway `net10.0` VB project that references
`Basalt.WinForms`.

Those checks are the reason the script exists. A package can pack cleanly,
restore cleanly and still produce nothing: the generator flows through a
dependency but the MSBuild props that hand it the views do not, so the project
builds green with no views in it. A template package can put its templates one
folder deeper than `dotnet new` looks, installing successfully and offering
nothing. Both happened here, and asserting only that the build succeeded missed
both.

The template check reads the package rather than asking `dotnet new` what it
offers: templates left over from an earlier run answer that question, so the
check once passed with the layout deliberately broken. It was rewritten and then
verified by breaking the layout again.

`build/pack.sh --skip-test` packs without verifying; `--winforms-only` packs and
checks just the two Windows Forms packages without installing the templates.
Nothing is published and the packages are not on nuget.org. For a maintainer who
decides to publish them, the command is the usual one, with your own key in
place of the placeholder:

```bash
dotnet nuget push "artifacts/*.nupkg" -s https://api.nuget.org/v3/index.json -k YOUR_KEY
```

---

## Repository layout

The IDE:

| Path | |
|---|---|
| `src/Basalt.Shell` | the IDE itself: editor, panels, menus, terminals |
| `src/Basalt.Workspace` | projects, MSBuild, Roslyn, Git, debugger, assistant |
| `src/Basalt.Designer` | the Avalonia visual designer and solution templates |
| `src/Basalt.Data` | the database client's providers |
| `src/Basalt.Core`, `src/Basalt.Extensibility` | shared model and extension points |

Razor for Visual Basic:

| Path | |
|---|---|
| `src/Basalt.Razor.Vb` | parser, view and component writers, source mappings |
| `src/Basalt.Razor.Vb.AspNetCore` | the ASP.NET Core integration, `AddVbViews` |
| `src/Basalt.Razor.Vb.Generator` | the build-time generator entry point |
| `src/Basalt.Razor.Vb.LanguageServer` | the LSP server all four editors run |
| `src/Basalt.Razor.Vb.Hosting` | `.vbpage` pages and on-the-fly compilation |
| `extensions/` | the Rider, Visual Studio and VS Code plugins, all named Basalt |
| `templates/` | `dotnet new` templates |
| `samples/` | a working MVC site and a `.vbpage` site |

Windows Forms for .NET on every desktop system:

| Path | |
|---|---|
| `src/Basalt.Forms`, `src/Basalt.Forms.Primitives`, `src/Basalt.Forms.VisualBasic` | the `System.Windows.Forms` API on Avalonia |
| `src/Basalt.Drawing` | `System.Drawing` on SkiaSharp |
| `src/Basalt.WinForms` | the package that carries them; see its `PACKAGE.md` |
| `src/Basalt.Accessibility`, `src/Basalt.Windows.Extensions` | the smaller assemblies of the same package |

And the rest:

| Path | |
|---|---|
| `src/Basalt.QuickBasic`, `src/Basalt.Vb6`, `src/Basalt.Vb6.Generator` | the QuickBASIC and Visual Basic 6 dialects: experimental and not offered by the IDE by default (see [`CROSS-COMPILING.md`](src/Basalt.QuickBasic/CROSS-COMPILING.md)) |
| `tests/` | the test projects: `Basalt.Tests`, `Basalt.Web.Tests`, `Basalt.E2E.Tests` and the Drawing and Forms suites |
| `build/` | setup, pack, bundle and acceptance scripts |

---

## Design decisions

**One parser, everywhere.** The compiler, the source generator, the language
server and the IDE all run the same parser. An editor and a build cannot
disagree about what a view means.

**Compile-time, not runtime.** Views are classes in your assembly. No runtime
compilation, no first-request delay, no Razor SDK dependency at run time. The
`.vbpage` model is the deliberate exception, and only in development.

**Nothing forked.** MVC, Razor Pages, dependency injection, model binding and
validation are stock ASP.NET Core. The integration is three registrations. There
is nothing to keep in step with each .NET release.

**Tests that would fail.** A test that passes both with and without the fix
proves nothing; that mistake has been made in this repository and caught by
re-introducing the defect on purpose. Rendering is verified by rasterising and
looking at the pixels, because numbers agree with each other while the screen
shows something else.

---

## Known limitations

- **Rider's New Solution dialog** does not offer VB for web projects
  ([above](#project-templates)). Use `dotnet new`.
- **Tag helpers**: `<vc:name>` view component tags are not generated yet, and
  attribute values are matched by name only (a `[type=text]` selector in
  `[HtmlTargetElement]` is treated as "has a type attribute").
- **The Visual Studio, VS Code and Rider extensions** have not been run end to
  end inside their editors yet (see `extensions/README.md`).
- **`.vbpage` pages** are under active development: the newest part of the
  repository, and the one whose shape is most likely to change.
- **Blazor components** cover routing, parameters, event handlers, child
  components, `ChildContent` and named fragments, `@bind`, cascading values
  and generics. A generic component's type arguments are inferred, except from
  a value naming a render-tree local such as a loop variable, where the tag
  has to write them. The editor sees another component's changed parameters
  once the project's compilation next changes.
- The IDE's own limits are in
  [Known limitations of the IDE](#known-limitations-of-the-ide).

---

## Contributing

Issues and pull requests are welcome at
<https://github.com/montagnamarco/basalt>.

A concrete failing case is worth more than a description of one: the view, the
line, and what you expected. Most of the subtle defects fixed here were found
that way.

Code and comments are written in English; user-facing strings are translatable
through `src/Basalt.Core/Localization`. Agents and contributors working in the
repository should read [AGENTS.md](AGENTS.md) first.

---

## Licence

MIT. See [LICENSE](LICENSE).
