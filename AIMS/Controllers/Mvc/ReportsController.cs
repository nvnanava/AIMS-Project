using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.Identity.Web;
namespace AIMS.Controllers.Mvc;

public class ReportsController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        ViewData["CurrentUserId"] = User.GetObjectId();
        ViewData["UserName"] = User.GetDisplayName();
        return View();
    }
}
