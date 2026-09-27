namespace Basalt.Workspace.Web;

/// <summary>
/// What a view's asp-controller, asp-action and asp-page can name in its
/// project.
/// </summary>
/// <param name="Actions">
/// Each controller by its route name ("Home" for HomeController), with its
/// actions.
/// </param>
/// <param name="Pages">Each Razor Page by the path asp-page takes: "/Index", "/Admin/Users".</param>
public sealed record RouteCatalog(
    IReadOnlyDictionary<string, IReadOnlyList<string>> Actions,
    IReadOnlyList<string> Pages)
{
    public static RouteCatalog Empty { get; } =
        new(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase), []);
}
