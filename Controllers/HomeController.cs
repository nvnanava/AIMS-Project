using Microsoft.AspNetCore.Mvc;

namespace AssetTrackingSystem.Controllers
{
    public class HomeController : Controller
    {
        [HttpGet("")]
        public IActionResult Index()
        {
            // Instead of returning a view, we return plain text.
            return Content("Hello from HomeController!");
        }
    }
}


