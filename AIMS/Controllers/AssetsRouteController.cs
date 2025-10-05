using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIMS.Controllers
{
    [Route("assets")]
    [AllowAnonymous]  // allow public access; details page handles auth internally
    public class AssetsRouteController : Controller
    {
        // /assets/{type}  →  /Home/AssetDetailsComponent?category={normalized}&source=card
        [HttpGet("{type:allowedAssetType}")]
        [AllowAnonymous]
        public IActionResult ByType(string type)
        {
            // turn slug/plural into canonical category (e.g., "charging-cable" → "Charging Cable")
            AIMS.Routing.AllowedAssetTypeConstraint.TryNormalize(type, out var category);
            return RedirectToAction("AssetDetailsComponent", "Home",
                new { category, source = "card" });
        }

        // /assets/{type}/{tag}  →  /Home/AssetDetailsComponent?category={normalized}&tag={tag}
        [HttpGet("{type:allowedAssetType}/{tag}")]
        [AllowAnonymous]
        public IActionResult ByTypeAndTag(string type, string tag)
        {
            AIMS.Routing.AllowedAssetTypeConstraint.TryNormalize(type, out var category);
            return RedirectToAction("AssetDetailsComponent", "Home",
                new { category, tag });
        }
    }
}