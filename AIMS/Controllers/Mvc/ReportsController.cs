using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace AIMS.Controllers.Mvc
{
    //[Authorize]
    [Route("reports")]
    public class ReportsController : Controller
    {
        private readonly ICurrentUser _currentUser;

        public ReportsController(ICurrentUser currentUser) => _currentUser = currentUser;

        // GET /reports
        [HttpGet]
        public async Task<IActionResult> Index(CancellationToken ct)
        {
            // what’s actually in the auth ticket?
            var oid = User.FindFirst("oid")?.Value
                   ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var upn = User.FindFirst("preferred_username")?.Value
                    ?? User.Identity?.Name;

            ViewBag.DebugOid = oid;
            ViewBag.DebugUpn = upn;

            var userId = await _currentUser.GetUserIdAsync(ct);

            if (userId is null)
            {


                ViewBag.CurrentUserId = null;
                return View();
            }

            ViewBag.CurrentUserId = userId;
            return View();
        }
    }
}
