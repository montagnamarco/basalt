# Basalt

**A cross-platform IDE for Visual Basic and its dialects.**

Basalt is an integrated development environment for Visual Basic, running on
macOS, Windows and Linux from one source tree. MSBuild compilation with
clickable errors, Roslyn IntelliSense in-process, a visual Avalonia designer
previewing through the real engine, Git, real terminals, and typing that
behaves the way VB does on Windows — `end if` becoming `End If` as you type.

Along the way it grew a few things that stand on their own:

- **Razor for Visual Basic on modern .NET.** `.vbhtml` was left behind at the
  .NET Framework; Basalt brings it back, as packages that work in Rider,
  Visual Studio and VS Code as well as here. This is the largest piece of
  work in the repository and most of what follows is about it.
- **Blazor components in Visual Basic.** A `.vbrazor` file compiles into a
  real `ComponentBase`, with parameters, event handlers and routing.
  Server, WebAssembly and Auto interactivity are all verified in a browser.
- **`.vbpage` pages** — Classic ASP's philosophy, brought up to date: a page is a
  file, a URL is a path, save and refresh. **Under active development.**
- **QuickBASIC**, which is here for the fun of it. See
  [below](#quickbasic-for-the-fun-of-it).

Everything is MIT licensed. The Razor packages have no dependency on the IDE:
use them in whatever editor you already have.

---

## Table of contents

- [The IDE](#the-ide)
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
- [Building from source](#building-from-source)
- [Repository layout](#repository-layout)
- [Design decisions](#design-decisions)
- [Known limitations](#known-limitations)
- [Contributing](#contributing)

---

## The IDE

The editor these packages were developed in, and the reason the repository
exists.

- **MSBuild compilation** with clickable errors carrying file, line and column
- **Roslyn IntelliSense** in-process — completion, diagnostics, quick info,
  go-to-definition — with no external server
- **A visual Avalonia designer**: toolbox, drag-and-drop, selection handles,
  property grid, undo/redo. The preview is rendered by the real Avalonia
  engine rather than an imitation of it, and VB.NET code-behind is generated
  and kept in sync
- **Typing that behaves like VB on Windows**: `end if` becomes `End If`,
  `if x=1 then` becomes `If x = 1 Then`, blocks close themselves on Enter,
  and identifiers follow the project's real symbols
  (`console.writeline` → `Console.WriteLine`). Strings and comments are left
  alone
- **Integrated terminals** on a pseudo-terminal on macOS and Linux, where shell
  prompts work; colours, cursor addressing and full-screen programs such as
  `vim` are not rendered yet, and on Windows the terminal does not start yet
  (ConPTY is planned)
- **Git**: branch, status, staging, commit, history
- **Dockable panels** that split, float into their own window, and restore
- **New Solution** generating VB.NET solutions that are verified by tests that
  actually compile them

```bash
dotnet run --project src/Basalt.Shell
```

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
> [Building the packages](#building-the-packages)). They have not been
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
| Basalt | ⌥⌘D, on save, and while typing |

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

See [`src/Basalt.QuickBasic/CROSS-COMPILING.md`](src/Basalt.QuickBasic/CROSS-COMPILING.md)
for which targets build out of the box and which need a sysroot first.

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

### Building the packages

```bash
build/pack.sh
```

Packs all four NuGet packages into `artifacts/`, then proves they work. It
reads the template package's own layout, installs it, creates a project through
`dotnet new mvc -lang VB`, restores it **by version from a feed** with no
reference to this repository's sources, builds it, and checks the `.vbhtml`
views actually became classes in the assembly.

Those last checks are the reason the script exists. A package can pack cleanly,
restore cleanly and still produce nothing — the generator flows through a
dependency but the MSBuild props that hand it the views do not, so the project
builds green with no views in it and says nothing about why. A template package
can pack cleanly and put its templates one folder deeper than `dotnet new`
looks, installing successfully and offering nothing. Both of those happened
here, and asserting only that the build succeeded missed both.

The template check reads the package rather than asking `dotnet new` what it
offers: templates left over from an earlier run answer that question, so the
check passed with the layout deliberately broken. It was rewritten and then
verified by breaking the layout again.

`build/pack.sh --skip-test` packs without verifying. Publishing:

```bash
dotnet nuget push "artifacts/*.nupkg" -s https://api.nuget.org/v3/index.json -k YOUR_KEY
```

---

## Repository layout

The IDE:

| Path | |
|---|---|
| `src/Basalt.Shell` | the IDE itself — editor, panels, menus, terminals |
| `src/Basalt.Workspace` | projects, MSBuild, Roslyn, Git |
| `src/Basalt.Designer` | the Avalonia visual designer |
| `src/Basalt.Core`, `src/Basalt.Extensibility` | shared model and extension points |

Razor for Visual Basic:

| Path | |
|---|---|
| `src/Basalt.Razor.Vb` | parser, view and component writers, source mappings |
| `src/Basalt.Razor.Vb.AspNetCore` | the ASP.NET Core integration — `AddVbViews` |
| `src/Basalt.Razor.Vb.Generator` | the build-time generator entry point |
| `src/Basalt.Razor.Vb.LanguageServer` | the LSP server all four editors run |
| `src/Basalt.Razor.Vb.Hosting` | `.vbpage` pages and on-the-fly compilation |
| `extensions/` | the Rider, Visual Studio and VS Code plugins, all named Basalt |
| `templates/` | `dotnet new` templates |
| `samples/` | a working MVC site and a `.vbpage` site |

And the rest:

| Path | |
|---|---|
| `src/Basalt.QuickBasic` | the QuickBASIC dialect, interpreter and native compiler |
| `tests/` | 2,226 tests |

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
proves nothing — that mistake has been made in this repository and caught by
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
- **The Visual Studio and VS Code extensions** are built from the same server as
  the Rider plugin but have had less use. Reports are welcome.
- **`.vbpage` pages** are under active development — the newest part of the
  repository, and the one whose shape is most likely to still change.
- **Blazor components** cover routing, parameters, event handlers, child
  components, `ChildContent` and named fragments, `@bind`, cascading values
  and generics. A generic component's type arguments are inferred, except from
  a value naming a render-tree local such as a loop variable, where the tag
  has to write them. The editor sees another component's changed parameters
  once the project's compilation next changes.
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
