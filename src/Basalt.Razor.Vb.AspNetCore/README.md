# Basalt.Razor.Vb.AspNetCore

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
| Other tag helpers (`asp-for`, validation) | not yet — the attribute is dropped |
| Tag helpers of your own | not yet |
| Blazor components | no |

### About tag helpers

ASP.NET Core's tag helpers parse markup into a tree and run a class over each
element. Basalt keeps markup as text, so the ones above are rewritten in place
instead: `asp-action` becomes a call to `Url.Action`. The rest are removed
rather than left alone — an unknown `asp-` attribute in the page looks like it
did something, and the link goes nowhere.

## Requirements

.NET 10 or later.
