# Basalt

**Razor for Visual Basic on modern .NET — plus a cross-platform IDE to write it in.**

Microsoft shipped `.vbhtml` with ASP.NET MVC 5 on the .NET Framework and never
carried it forward. On .NET Core and everything after it, Razor is C# only: no
`.vbhtml` compilation, no tooling, no templates. A VB.NET web application has
had no view engine of its own for a decade.

Basalt closes that gap. It is a Razor implementation for Visual Basic that
compiles `.vbhtml` views ahead of time, serves them under ASP.NET Core MVC,
Razor Pages and Web API, and gives them syntax highlighting, IntelliSense and
diagnostics **in Rider, Visual Studio, VS Code and in Basalt's own editor**.

Everything here is MIT licensed. There is no runtime dependency on Basalt the
IDE: the packages work in whatever editor you already use.

---

## Table of contents

- [The `.vbhtml` story](#the-vbhtml-story)
- [Quick start](#quick-start)
- [How a view becomes a class](#how-a-view-becomes-a-class)
- [The language](#the-language)
- [Serving views under ASP.NET Core](#serving-views-under-aspnet-core)
- [`.vbp` pages: Classic ASP, modernised](#vbp-pages-classic-asp-modernised)
- [IDE support](#ide-support)
- [Project templates](#project-templates)
- [The IDE](#the-ide)
- [Building from source](#building-from-source)
- [Repository layout](#repository-layout)
- [Design decisions](#design-decisions)
- [Known limitations](#known-limitations)
- [Contributing](#contributing)

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

In an existing VB.NET web project, two steps:

```xml
<PackageReference Include="Basalt.Razor.Vb" Version="1.0.0" />
<PackageReference Include="Basalt.Razor.Vb.AspNetCore" Version="1.0.0" />
```

```vb
builder.Services.AddControllersWithViews()
builder.Services.AddVbViews()   ' the single call that makes .vbhtml work
```

`Basalt.Razor.Vb` carries the source generator, so every `.vbhtml` under the
project becomes a compiled class on the next build. `AddVbViews` registers the
view engine hooks that let MVC find them.

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
carrying a `[RazorCompiledItem]` attribute so ASP.NET Core's discovery finds it
without being told.

**4. Source mappings are recorded.** Every fragment of Visual Basic that came
from the template carries a mapping back to its original line and column, and
the generated file is stitched with `#ExternalSource` directives so the VB
compiler blames the right file. This is what makes an error in a view point at
the view rather than at generated code nobody wrote.

Those mappings are also what the language server runs on — see
[IDE support](#ide-support). They are the load-bearing part of the whole design,
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
| `@Layout "…"` | the layout to wrap this view in |
| `@Section Name … @End Section` | a named section for the layout |
| `@Code … @End Code` | a block of statements |
| `@Functions … @End Functions` | methods and fields on the generated class |
| `@Inject T As Name` | dependency injection into the view |
| `@Page` | marks a Razor Page |
| `@Namespace Ns` | overrides the generated namespace |

`RenderBody` and `RenderSection` work in layouts as they do in C#.

### Tag helpers

`asp-controller`, `asp-action`, `asp-page` and `asp-for` are rewritten at
compile time — `asp-for` emits the matching `name`, `id` and current value on
`input`, `label` and `textarea`.

---

## Serving views under ASP.NET Core

`AddVbViews()` registers three things, and each answers a specific question the
framework asks:

- an **`IViewLocationExpander`**, so MVC looks for `.vbhtml` beside `.cshtml`
  when resolving a view name;
- an **`IRazorPageFactoryProvider`**, so a resolved path is turned into an
  instance of the generated class;
- a **`CompiledRazorAssemblyPart`** for the compiled views, so discovery finds
  them the way it finds C# ones.

That is the entire integration surface. Controllers, filters, model binding,
validation, dependency injection, layouts, sections, partials and view
components are stock ASP.NET Core and were never touched.

Web API needs nothing at all — there are no views — but the same project can
serve both.

---

## `.vbp` pages: Classic ASP, modernised

Alongside Razor there is a second, deliberately simpler model: a page is a file,
a URL is a path, and there are no controllers or models to declare.

```vbp
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

## IDE support

This is the part that makes `.vbhtml` usable rather than merely compilable, and
it is the same implementation everywhere.

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

Both `.vbhtml` and `.vbp` are handled.

### Rider

`extensions/rider-vbrazor` — an IntelliJ Platform plugin registering the file
types, the grammar and the LSP client. Install the built zip via
**Settings → Plugins → ⚙ → Install Plugin from Disk**.

### Visual Studio

`extensions/vs-vbrazor` — a VSIX doing the same for Visual Studio 2022.

### VS Code

`extensions/vscode-vbrazor` — a standard extension; the server ships inside it.

### Basalt

Built in. No extension needed.

---

## Project templates

```bash
dotnet new install Basalt.Templates
```

| Command | What you get |
|---|---|
| `dotnet new mvc -lang VB` | MVC with `.vbhtml` views |
| `dotnet new webapp -lang VB` | Razor Pages |
| `dotnet new webapi -lang VB` | Web API |
| `dotnet new web -lang VB` | empty ASP.NET Core |
| `dotnet new vbpages` | a site of `.vbp` pages |

These are the same short names the C# templates use, so `-lang VB` selects the
Visual Basic one exactly the way `-lang F#` selects F#.

**A caveat about Rider's New Solution dialog.** Rider reads web templates from
its own bundled .NET SDK rather than from the user template cache, so VB will
not appear in the web section there no matter what the template declares. This
is a limitation on Rider's side, measured rather than assumed. `dotnet new` from
a terminal works, and the resulting project opens and builds in Rider normally.

---

## The IDE

Basalt is also a full IDE for Visual Basic — the editor these packages were
developed in. It runs on macOS, Windows and Linux from one source tree.

- **MSBuild compilation** with clickable errors carrying file, line and column
- **Roslyn IntelliSense** in-process: completion, diagnostics, quick info,
  go-to-definition
- **A visual Avalonia designer** — toolbox, drag-and-drop, selection handles,
  property grid, undo/redo — previewing through the real Avalonia engine rather
  than an imitation of it, with VB.NET code-behind generated and kept in sync
- **Typing that behaves like VB on Windows**: `end if` becomes `End If`,
  `if x=1 then` becomes `If x = 1 Then`, blocks close themselves on Enter,
  identifiers follow the project's real symbols
- **Integrated terminals** on a real pseudo-terminal — shell prompts, colours,
  `vim` and `top` all work
- **Git**: branch, status, staging, commit, history
- **Dockable panels** that split, float and restore
- **A QuickBASIC dialect** for older sources

---

## Building from source

Requires the **.NET 10 SDK**. For the IDE extensions: **JDK 17+** for Rider (the
Gradle wrapper is included — use `./gradlew`, no separate Gradle install) and
**Node 18+** for VS Code.

```bash
git clone https://github.com/montagnamarco/basalt.git
cd basalt
dotnet build Basalt.slnx
dotnet test tests/Basalt.Tests
dotnet run --project src/Basalt.Shell     # the IDE
```

The Rider plugin, server included:

```bash
dotnet publish src/Basalt.Razor.Vb.LanguageServer -c Release -r osx-arm64 \
  --self-contained -o extensions/rider-vbrazor/server
cd extensions/rider-vbrazor && ./gradlew buildPlugin
```

The zip lands in `build/distributions/`. Substitute your own runtime identifier
for `osx-arm64` as needed.

---

## Repository layout

| Path | |
|---|---|
| `src/Basalt.Razor.Vb` | parser, code writer, source mappings, source generator |
| `src/Basalt.Razor.Vb.AspNetCore` | the ASP.NET Core integration — `AddVbViews` |
| `src/Basalt.Razor.Vb.Hosting` | `.vbp` pages and on-the-fly compilation |
| `src/Basalt.Razor.Vb.LanguageServer` | the LSP server all four editors run |
| `src/Basalt.Razor.Vb.Generator` | the build-time generator entry point |
| `src/Basalt.Shell` | the IDE |
| `src/Basalt.Workspace` | projects, MSBuild, Roslyn, Git |
| `src/Basalt.Designer` | the Avalonia visual designer |
| `src/Basalt.Core`, `src/Basalt.Extensibility` | shared model and extension points |
| `src/Basalt.QuickBasic` | the QuickBASIC dialect |
| `extensions/` | Rider, Visual Studio and VS Code plugins |
| `templates/` | `dotnet new` templates |
| `samples/` | a working MVC site and a `.vbp` site |
| `tests/` | 2,226 tests |

---

## Design decisions

**One parser, everywhere.** The compiler, the source generator, the language
server and the IDE all run the same parser. An editor and a build cannot
disagree about what a view means.

**Compile-time, not runtime.** Views are classes in your assembly. No runtime
compilation, no first-request delay, no Razor SDK dependency at run time. The
`.vbp` model is the deliberate exception, and only in development.

**Nothing forked.** MVC, Razor Pages, dependency injection, model binding and
validation are stock ASP.NET Core. The integration is three registrations. There
is nothing to keep in step with each .NET release.

**Tests that would fail.** A test that passes both with and without the fix
proves nothing — that mistake has been made in this repository and caught by
re-introducing the defect on purpose. Rendering is verified by rasterising and
looking at the pixels, because numbers agree with each other while the screen
shows something else.

---

## Known limitations

- **Rider's New Solution dialog** does not offer VB for web projects
  ([above](#project-templates)). Use `dotnet new`.
- **Tag helpers** cover `asp-controller`, `asp-action`, `asp-page` and
  `asp-for`. `asp-validation-for`, `@inject` on tag helpers, and
  `addTagHelper`/`removeTagHelper`/`tagHelperPrefix` are not implemented yet.
- **The Visual Studio and VS Code extensions** are built from the same server as
  the Rider plugin but have had less use. Reports are welcome.
- **The designer** is missing distance guides while dragging, a grid row/column
  overlay, and a colour picker in the property grid.

---

## Contributing

Issues and pull requests are welcome at
<https://github.com/montagnamarco/basalt>.

A concrete failing case is worth more than a description of one — the view, the
line, and what you expected. Most of the subtle defects fixed here were found
that way.

Code and comments are written in English; user-facing strings are translatable.

---

## Licence

MIT — see [LICENSE](LICENSE).
