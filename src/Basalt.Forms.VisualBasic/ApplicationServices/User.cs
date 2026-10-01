using System.Security.Principal;

namespace Microsoft.VisualBasic.ApplicationServices;

/// <summary>
/// My.User: the identity the application runs as. There is no portable
/// equivalent of WindowsPrincipal's group lookup outside Windows, so the
/// default principal is a GenericPrincipal for the current OS user with no
/// roles: Name reports who is running the application, IsInRole always
/// answers false unless a caller replaces CurrentPrincipal with one that
/// actually knows the application's roles.
/// </summary>
public class User
{
    private IPrincipal? _principal;

    public User()
    {
    }

    public IPrincipal CurrentPrincipal
    {
        get => InternalPrincipal;
        set => InternalPrincipal = value;
    }

    protected virtual IPrincipal InternalPrincipal
    {
        get => _principal ??= new GenericPrincipal(new GenericIdentity(Environment.UserName), Array.Empty<string>());
        set => _principal = value;
    }

    public bool IsAuthenticated => CurrentPrincipal.Identity?.IsAuthenticated ?? false;

    public string Name => CurrentPrincipal.Identity?.Name ?? string.Empty;

    public bool IsInRole(string role) => CurrentPrincipal.IsInRole(role);
}
