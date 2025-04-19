using Microsoft.AspNetCore.Mvc;

namespace AIMS.Controllers
{
    public class AuditLogController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
