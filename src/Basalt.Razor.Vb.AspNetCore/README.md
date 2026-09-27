| Tag helpers, with `@addTagHelper` (`asp-for`, validation, `select`, `environment`, `partial`, forms with antiforgery, …) | yes — the framework's own, run as in C# |
| Tag helpers of your own, in VB or C# | yes |
| `<vc:name>` view component tags | not yet |# Basalt.Razor.Vb.AspNetCore

ASP.NET Core MVC and Razor Pages sites written in Visual Basic, with `.vbhtml`
views.

## Why this exists

Visual Basic works on the web today for everything that does not go through a
view. Minimal APIs run. Web API controllers run. MVC controllers run — right
up to the moment one returns `View()`, where the Razor engine reports:

```
Searched locations: /Views/Home/Index.cshtml, /Views/Shared/Index.cshtml
```

Only `.cshtml`. The Visual Basic view sits unread beside it. This package
closes that gap.

## Getting started from a template

```
dotnet new install Basalt.Templates
dotnet new vbmvc -o MySite      # MVC
dotnet new vbwebapp -o MySite   # Razor Pages
dotnet new vbwebapi -o MyApi    # Web API
dotnet new vbweb -o MySite      # empty
cd MySite && dotnet run
```

## Getting started by hand

```xml
<ItemGroup>
  <PackageReference Include="Basalt.Razor.Vb" Version="1.0.0" />
  <PackageReference Include="Basalt.Razor.Vb.AspNetCore" Version="1.0.0" />
</ItemGroup>
```

```vb
Imports Basalt.Razor.Vb.AspNetCore

Public Module Program
    Public Sub Main(args As String())
        Dim builder = WebApplication.CreateBuilder(args)

        builder.Services.AddControllersWithViews()
        builder.Services.AddVbViews()          ' the whole setup

        Dim app = builder.Build()
        app.MapDefaultControllerRoute()
        app.Run()
    End Sub
End Module
```

`Views/Home/Index.vbhtml`:

```vbhtml
@ModelType MyApp.Models.Customer

<h1>Hello @Model.Name</h1>

@If Model.Orders > 2 Then
    <p>VIP customer</p>
End If
```

That is all. The views are compiled by the source generator into ordinary
classes in your assembly — there is no runtime compilation step, and an error
in a template is reported against the template, at the right line.

## Mixing with C#

Both are served. `.vbhtml` is preferred where both exist, so a site can be
ported one view at a time.

## What works

Every row was checked by running a site and making a request, not by reading
the code.

| | |
|---|---|
| MVC controllers and views | yes |
| Razor Pages (`@Page`), handlers (`OnGet`, `OnPost`) | yes |
| Model binding, `<BindProperty>`, antiforgery | yes |
| Typed models, `@ModelType` | yes |
| Layouts, `_ViewStart`, `_ViewImports`, nested | yes |
| `@Section`, partials, `@Await` | yes |
| `@Html`, `@Url`, `@ViewData`, `@ViewBag`, `@TempData` | yes |
| `@Inject` | yes |
| `@<p>` markup transitions | yes |
| Control flow: `If`, `For Each`, `Select Case`, `Using`, … | yes |
| `asp-controller`, `asp-action`, `asp-page` on `<a>` and `<form>` | yes |
| `asp-for` on `<input>`, `<textarea>` and `<label>` | partly — name, id and value by textual rewrite; no validation attributes, no `[Display]` |
| Other tag helpers (validation, `select`, `environment`, …) | not yet — the attribute is dropped |
| Tag helpers of your own | not yet |
| Blazor components | `.vbrazor`, compiled by the `Basalt.Razor.Vb` generator; static rendering only for now |

### About tag helpers

With `@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers` in `_ViewImports.vbhtml`,
the generator finds the tag helper classes and writes the same calls the C#
Razor compiler writes, so each one runs exactly as in a `.cshtml`. A view
without the directive keeps the older compile-time rewriting of `asp-action`,
`asp-controller`, `asp-page` and `asp-for`.

### Localised views

`@Inject IViewLocalizer L` and `@L("Key")` work as in C#, with one Visual Basic
difference: VB names an embedded `.resx` after the root namespace and the file
name only, whatever folder it is in. Keep `Views.Home.Index.resx` anywhere in the
project and call `AddLocalization()` **without** `ResourcesPath` — with
`ResourcesPath = "Resources"` the localizer looks for
`RootNamespace.Resources.Views.Home.Index`, which a VB project never produces.

## Requirements

.NET 10 or later.
