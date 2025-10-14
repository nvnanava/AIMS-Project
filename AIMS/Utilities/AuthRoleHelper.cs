using System.Security.Claims;

namespace AIMS.Utilities;

public static class AuthRoleHelper
{
    // Centralized allow-lists (keep them here or move to appsettings later)
    private static readonly string[] AdminUsernames =
    {
        "nvnanavati@csus.edu",
        "akalustatsingh@csus.edu",
        "tburguillos@csus.edu",
        "keeratkhandpur@csus.edu",
        "suhailnajimudeen@csus.edu",
        "hkaur20@csus.edu",
        "cameronlanzaro@csus.edu",
        "norinphlong@csus.edu"
    };

    private static readonly string[] HelpdeskUsernames =
    {
        "barryAllen@centralcity.edu"
    };

    private static readonly string[] SupervisorUsernames =
    {
        "richardGrayson@gotham.edu",
        "niyant397@gmail.com",
        "tnburg@pacbell.net"
    };

    private static string? GetUpn(ClaimsPrincipal user) =>
        user?.FindFirst("preferred_username")?.Value
        ?? user?.Identity?.Name;

    public static bool IsAdmin(ClaimsPrincipal user)
        => IsInList(user, AdminUsernames);

    public static bool IsHelpdesk(ClaimsPrincipal user)
        => IsInList(user, HelpdeskUsernames);

    public static bool IsSupervisor(ClaimsPrincipal user)
        => IsInList(user, SupervisorUsernames);

    public static bool IsAdminOrHelpdesk(ClaimsPrincipal user)
        => IsAdmin(user) || IsHelpdesk(user);

    private static bool IsInList(ClaimsPrincipal user, string[] list)
    {
        var upn = GetUpn(user);
        return !string.IsNullOrWhiteSpace(upn)
               && list.Contains(upn, StringComparer.OrdinalIgnoreCase);
    }
}
