using System.Security.Claims;

namespace AIMS.Utilities
{
    public static class ClaimsPrincipalExtensions
    {
        public static bool IsAdmin(this ClaimsPrincipal user)
            => AuthRoleHelper.IsAdmin(user);

        public static bool IsHelpDesk(this ClaimsPrincipal user)
            => AuthRoleHelper.IsHelpdesk(user);

        public static bool IsSupervisor(this ClaimsPrincipal user)
            => AuthRoleHelper.IsSupervisor(user);

        public static bool IsAdminOrHelpdesk(this ClaimsPrincipal user)
            => AuthRoleHelper.IsAdminOrHelpdesk(user);
    }
}
