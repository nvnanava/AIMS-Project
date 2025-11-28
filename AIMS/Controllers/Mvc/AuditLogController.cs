using AIMS.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIMS.Controllers.Mvc;

[Authorize]
public class AuditLogController : Controller
{
    public IActionResult Index()
    {
        if (!User.IsAdmin())
        {
            return View("~/Views/Error/NotAuthorized.cshtml");
        }
        else
        {

            return View();
        }
    }
}
