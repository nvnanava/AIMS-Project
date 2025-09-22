using Microsoft.AspNetCore.Mvc;
using AIMS.Helpers;

namespace AIMS.Controllers
{
    [Route("assets")]
    public class AssetsController : Controller
    {
        [HttpGet("")]
        [HttpGet("{type?}")]
        public IActionResult Index(string? type)
        {
            // Guard: validate type param
            if (!ValidAssetTypes.IsValid(type))
                return NotFound(); // triggers middleware -> /error/not-found

            ViewData["Type"] = string.IsNullOrWhiteSpace(type) ? "all" : type!.ToLowerInvariant();
            return View();
        }
    }
}
