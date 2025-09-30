using System.Security.Claims;
using AIMS.Models;

namespace AIMS.Helpers
{
    public static class ClaimsPrincipalExtensions // Extension methods for ClaimsPrincipal to check user roles
    {
        public static bool IsAdmin(this ClaimsPrincipal user) // Check if the user is an admin
        {
            return user.HasClaim("preferred_username", "nvnanavati@csus.edu") ||
                     user.HasClaim("preferred_username", "tburguillos@csus.edu") ||
                     user.HasClaim("preferred_username", "akalustatsingh@csus.edu") ||
                     user.HasClaim("preferred_username", "keeratkhandpur@csus.edu") ||
                     user.HasClaim("preferred_username", "suhailnajimudeen@csus.edu") ||
                     user.HasClaim("preferred_username", "hkaur20@csus.edu") ||
                     user.HasClaim("preferred_username", "cameronlanzaro@csus.edu") ||
                     user.HasClaim("prefered_username", "norinphlong@csus.edu");


        }
        public static bool IsHelpDesk(this ClaimsPrincipal user) // Check if the user is a help desk
        {
            return user.HasClaim("preferred_username", "richardGrayson@gotham.edu") ||
                        user.HasClaim("preferred_username", "wallyWest@CentralCity.edu");
        }
        public static bool IsSupervisor(this ClaimsPrincipal user) // Check if the user is a supervisor
        {
            return user.HasClaim("preferred_username", "niyant397@gmail.com");

        }
    }
}


