using Microsoft.AspNetCore.Mvc;

namespace AIMS.Controllers.Mvc;

public class AuditLogController : Controller
{
    public IActionResult Index()
    {
        return View();
    }
}
