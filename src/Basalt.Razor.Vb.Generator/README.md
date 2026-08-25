# Basalt.Razor.Vb

Razor views written in Visual Basic, for ASP.NET Core.

ASP.NET Core's own Razor compiler emits C# and only C#, so a `.vbhtml` view has
no way to be compiled — the file type exists in ASP.NET (the framework
before Core) but was never carried over. This package fills that gap: a source
generator reads `.vbhtml` templates and emits Visual Basic, which the VB
compiler then builds like any other source file.

## Using it

Reference the package. Nothing else is needed:

```xml
<PackageReference Include="Basalt.Razor.Vb" Version="1.0.0" />
```

Every `.vbhtml` file in the project is compiled. Write one as you would a
`.cshtml`, with Visual Basic in place of C#:

```vbhtml
@ModelType Customer

<h1>@Model.Name</h1>

@Code
    Dim total = Model.Orders.Count
End Code

<p>Orders: @total</p>

@If total > 0 Then
    <ul>
        @For Each order In Model.Orders
            <li>@order.Reference</li>
        Next
    </ul>
End If
```

Each view becomes a class inheriting `VbHtmlView(Of TModel)`, named after the
file and placed in a namespace following its folder. Render it by calling
`Render()`.

## What is supported

- `@ModelType` to declare the model
- `@expression` for values, HTML-encoded
- `@Code ... End Code` blocks
- `@If`, `@For`, `@For Each`, `@While`, `@Select Case` around markup
- `@@` for a literal at sign
- Comments with `@* ... *@`

## Diagnostics

Problems in a view are reported against the view itself, with the line and
column of the template rather than of the generated code. That distinction
matters: an error reported against generated Visual Basic tells the user
nothing about the file they wrote.

## Turning off the empty-project warning

A project that references this package but contains no views produces a
warning, since that usually means the views are in the wrong place. If the
arrangement is deliberate:

```xml
<PropertyGroup>
  <BasaltRazorVbWarnOnEmpty>false</BasaltRazorVbWarnOnEmpty>
</PropertyGroup>
```
