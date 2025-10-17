using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIMS.Controllers.Mvc;

[Route("assets")]
[AllowAnonymous] // page handles auth itself if needed
public class AssetsRouteController : Controller
{
    // /assets/{type} → /AssetDetails?category={normalized}&source=card
    [HttpGet("{type:allowedAssetType}")]
    public IActionResult ByType(string type)
    {
        AIMS.Routing.AllowedAssetTypeConstraint.TryNormalize(type, out var category);
        return RedirectToAction("Index", "AssetDetails", new { category, source = "card" });
    }

    // /assets/{type}/{tag} → /AssetDetails?category={normalized}&tag={tag}
    [HttpGet("{type:allowedAssetType}/{tag}")]
    public IActionResult ByTypeAndTag(string type, string tag)
    {
        AIMS.Routing.AllowedAssetTypeConstraint.TryNormalize(type, out var category);
        return RedirectToAction("Index", "AssetDetails", new { category, tag });
    }
}
