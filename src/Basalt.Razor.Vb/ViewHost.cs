namespace Basalt.Razor.Vb;

/// <summary>
/// Which runtime a generated view is written for.
/// </summary>
/// <remarks>
/// The two hosts want different shapes from the same template: the standalone
/// runtime has its own base class and a synchronous Execute, while ASP.NET
/// Core requires RazorPage(Of TModel) and an async ExecuteAsync it can await.
/// Generating for the wrong one fails to compile, so the choice is explicit
/// rather than guessed from what happens to be referenced.
/// </remarks>
public enum ViewHost
{
    /// <summary>
    /// Basalt's own runtime, for generating text outside a web application:
    /// mail bodies, reports, code generation.
    /// </summary>
    Standalone,

    /// <summary>
    /// ASP.NET Core MVC: the views are served over HTTP by a controller.
    /// </summary>
    AspNetCore,

    /// <summary>
    /// An ASP.NET Core Razor Page, which routes on its own path.
    /// </summary>
    /// <remarks>
    /// A separate host because a page inherits RazorPages.Page rather than
    /// RazorPage(Of TModel): different base, different registration. Emitting
    /// the MVC shape produced a class the framework silently never routed to,
    /// which is a 404 with nothing in the log to explain it.
    /// </remarks>
    RazorPage,

    /// <summary>
    /// A Blazor component, in any of its render modes.
    /// </summary>
    /// <remarks>
    /// The furthest from the others: a component does not write HTML at all.
    /// It builds a render tree — OpenElement, AddContent, CloseElement — which
    /// Blazor then diffs against the previous one to decide what to change on
    /// screen. Writing markup out as text, the way every other host does,
    /// produces a class that compiles and renders nothing.
    ///
    /// Server, WebAssembly and Auto are not separate hosts: they differ in
    /// where the component runs, not in what is generated for it.
    /// </remarks>
    Component,
}
