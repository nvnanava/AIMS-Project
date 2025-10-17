using Microsoft.AspNetCore.Mvc;

namespace AIMS.Controllers.Mvc;

public class SearchController : Controller
{
    // View is fully client-driven; we keep the param for deep-links and telemetry
    [HttpGet]
    public IActionResult Index([FromQuery] string? searchQuery)
    {
        ViewBag.SearchQuery = string.IsNullOrWhiteSpace(searchQuery) ? null : searchQuery.Trim();
        return View(); // Views/Search/Index.cshtml
    }
}
