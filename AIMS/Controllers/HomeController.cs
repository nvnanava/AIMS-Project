using System.Diagnostics;
using AIMS.Data;
using AIMS.Models;
using AIMS.Queries;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly AimsDbContext _db;

    private readonly HardwareQuery? _hardwareQuery;
    private readonly SoftwareQuery? _softwareQuery;
    private readonly UserQuery? _userQuery;
    private readonly AssignmentsQuery? _assignQuery;

    public HomeController(
        ILogger<HomeController> logger,
        AimsDbContext db,
        IServiceProvider sp
    )
    {
        _logger = logger;
        _db = db;

        _hardwareQuery = sp.GetService<HardwareQuery>();
        _softwareQuery = sp.GetService<SoftwareQuery>();
        _userQuery = sp.GetService<UserQuery>();
        _assignQuery = sp.GetService<AssignmentsQuery>();
    }

    // If services exist, pass a model. Otherwise just render the view (client can fetch /api/assets).
    public async Task<IActionResult> Index()
    {
        if (_hardwareQuery is not null && _softwareQuery is not null)
        {
            var model = new HomeIndexViewModel
            {
                Hardware = await _hardwareQuery.GetAllHardwareAsync(),
                Software = await _softwareQuery.GetAllSoftwareAsync()
            };
            return View(model);
        }

        return View(); // no model; the page can call /api/assets client-side
    }

    public IActionResult Reports() => View();

    // Search page: blank query renders empty; otherwise view can still fetch via /api/assets.
    [HttpGet]
    public async Task<IActionResult> Search(string? searchQuery)
    {
        var q = string.IsNullOrWhiteSpace(searchQuery) ? null : searchQuery.Trim();
        ViewBag.SearchQuery = q;

        if (q is null)
        {
            ViewBag.Results = new List<Dictionary<string, string>>();
            return View();
        }

        // (Optional) If we wire a real query layer later, map into ViewBag.Results here.
        try
        {
            ViewBag.Results = new List<Dictionary<string, string>>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed for query '{Query}'", q);
            ViewBag.Results = new List<Dictionary<string, string>>();
        }

        return View();
    }

    public IActionResult Privacy() => View();
    // public IActionResult Feedback() => View(); # Scaffolded

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
        => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });

    // ----------------------------------------------------------------------------
    // Details page with a server-side guard:
    // If tag is provided and its true type doesn't match the category in the URL,
    // redirect to the same page with the corrected category.
    // ----------------------------------------------------------------------------
    public async Task<IActionResult> AssetDetailsComponent(string? category, string? tag)
    {
        // Normalize requested category (used when no tag)
        var requestedCategory = string.IsNullOrWhiteSpace(category) ? "Laptop" : category.Trim();
        var requestedKey = requestedCategory.ToLowerInvariant();

        ViewData["Category"] = requestedCategory;
        ViewData["Title"] = $"{requestedCategory} Asset Details";

        // No tag? Just render the category view (list-by-type behavior)
        if (string.IsNullOrWhiteSpace(tag))
            return View();

        var t = tag.Trim();

        // Try hardware by serial, then software by license key
        var hw = await _db.HardwareAssets
            .AsNoTracking()
            .Where(h => h.SerialNumber == t)
            .Select(h => new { Type = h.AssetType })
            .FirstOrDefaultAsync();

        string? detectedType = hw?.Type;

        if (detectedType is null)
        {
            var sw = await _db.SoftwareAssets
                .AsNoTracking()
                .Where(s => s.SoftwareLicenseKey == t)
                .Select(s => new { Type = "Software" })
                .FirstOrDefaultAsync();

            detectedType = sw?.Type;
        }

        if (detectedType is null)
        {
            // Tag not found: render the page so the client JS can show the empty state nicely
            ViewData["MissingTag"] = t;
            return View();
        }

        // Category mismatch? Redirect to the correct category for this tag
        if (!string.Equals(detectedType.Trim(), requestedCategory.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToAction(nameof(AssetDetailsComponent), new
            {
                category = detectedType,
                tag = t
            });
        }

        // Types match — render
        ViewData["Category"] = detectedType; // ensure canonical casing
        ViewData["Title"] = $"{detectedType} Asset Details";
        return View();
    }
}

public class HomeIndexViewModel
{
    public List<GetHardwareDto> Hardware { get; set; } = new();
    public List<GetSoftwareDto> Software { get; set; } = new();
}
