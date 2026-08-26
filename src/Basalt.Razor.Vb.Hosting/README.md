# Basalt.Razor.Vb.Hosting

`.vbpage` pages for ASP.NET Core, compiled while the site is running.

> **Under active development.** This is the newest part of
> [Basalt](https://github.com/montagnamarco/basalt) and the one whose shape is
> most likely to change.

## What a `.vbpage` page is

Classic ASP's philosophy, on top of ASP.NET Core: a page is a file, a URL is a
path, and there are no controllers or models to declare.

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

## Use

```vb
Dim app = WebApplication.CreateBuilder(args).Build()

If app.Environment.IsDevelopment() Then
    app.MapVbPagesFromDisk(IO.Path.Combine(app.Environment.ContentRootPath, "Pages"))
Else
    app.MapVbPages()
End If

app.Run()
```

From disk, a page is compiled on demand and the folder is watched: **save the
file, refresh the browser, see the change** — no build, no restart. In
production the same pages are compiled into the assembly by the source
generator in `Basalt.Razor.Vb`.

Note the `Else`, and mean it. If pages are both compiled in *and* mapped from
disk, the compiled one answers first and your edit is never read — which looks
exactly like hot reload being broken.

## Why it is a separate package

It carries the Visual Basic compiler, which is tens of megabytes. A site that
compiles its pages at build time should not have to ship a compiler to
production to get them.

## Licence

MIT. Part of [Basalt](https://github.com/montagnamarco/basalt).
