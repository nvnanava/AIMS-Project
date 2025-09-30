using Microsoft.AspNetCore.Mvc;

namespace AIMS.Controllers
{
    // Minimal UI route so /assets/{type} renders a view and honors the allow-list constraint.
    [Route("assets")]
    public class AssetsRouteController : Controller
    {
        [HttpGet("{type:allowedAssetType}")]
        public IActionResult Index(string type)
        {
            ViewData["AssetType"] = type;
            return View("~/Views/Home/Index.cshtml"); // swap to your Assets view if you have one
        }
    }
}
