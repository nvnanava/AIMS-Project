using AIMS.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Controllers.Mvc;

public class AssetDetailsController : Controller
{
    private readonly ILogger<AssetDetailsController> _logger;
    private readonly AimsDbContext _db;

    public AssetDetailsController(ILogger<AssetDetailsController> logger, AimsDbContext db)
    {
        _logger = logger;
        _db = db;
    }

    // Keep parity with old route (/AssetDetails/Index) and provide a clean route (/AssetDetails)
    [HttpGet("/AssetDetails")]
    [HttpGet("/AssetDetails/Index")]
    [AllowAnonymous]
    public async Task<IActionResult> Index([FromQuery] string? category, [FromQuery] string? tag)
    {
        // Supervisors do not use details—send to Search
        if (User.IsInRole("Supervisor") || User.HasClaim("role", "Supervisor"))
            return RedirectToAction(nameof(SearchController.Index), "Search", new { searchQuery = (string?)null });

        var requestedCategory = string.IsNullOrWhiteSpace(category) ? "Laptop" : category.Trim();
        ViewData["Category"] = requestedCategory;
        ViewData["Title"] = $"{requestedCategory} Asset Details";

        // No tag: render empty shell; client can page/fetch
        if (string.IsNullOrWhiteSpace(tag))
            return View("~/Views/AssetDetails/Index.cshtml");

        var t = tag.Trim();

        // Try hardware by SerialNumber, then software by LicenseKey
        string? detectedType = await _db.HardwareAssets.AsNoTracking()
            .Where(h => h.SerialNumber == t)
            .Select(h => h.AssetType)
            .FirstOrDefaultAsync();

        if (detectedType is null)
        {
            detectedType = await _db.SoftwareAssets.AsNoTracking()
                .Where(s => s.SoftwareLicenseKey == t)
                .Select(_ => "Software")
                .FirstOrDefaultAsync();
        }

        if (detectedType is null)
        {
            ViewData["MissingTag"] = t;
            return View("~/Views/AssetDetails/Index.cshtml");
        }

        // If category mismatch, redirect to the right one (deep-link consistency)
        if (!string.Equals(detectedType.Trim(), requestedCategory.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToAction(nameof(Index), new { category = detectedType, tag = t });
        }

        ViewData["Category"] = detectedType;
        ViewData["Title"] = $"{detectedType} Asset Details";
        return View("~/Views/AssetDetails/Index.cshtml");
    }
}
