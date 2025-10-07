using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AIMS.Data;
using AIMS.Models;
using AIMS.Queries;
using AIMS.Services;
using AIMS.Utilities;
using AIMS.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIMS.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly AimsDbContext _db;
    private readonly HardwareQuery? _hardwareQuery;
    private readonly SoftwareQuery? _softwareQuery;
    private readonly UserQuery? _userQuery;
    private readonly AssignmentsQuery? _assignQuery;
    private readonly AssetTypeCatalogService _catalog;
    private readonly SummaryCardService _summarySvc;

    public HomeController(
        ILogger<HomeController> logger,
        AimsDbContext db,
        IServiceProvider sp,
        AssetTypeCatalogService catalog,
        SummaryCardService summarySvc
    )
    {
        _logger = logger;
        _db = db;
        _catalog = catalog;
        _summarySvc = summarySvc;

        _hardwareQuery = sp.GetService<HardwareQuery>();
        _softwareQuery = sp.GetService<SoftwareQuery>();
        _userQuery = sp.GetService<UserQuery>();
        _assignQuery = sp.GetService<AssignmentsQuery>();
    }

    public async Task<IActionResult> Index()
    {
        // Supervisors should land on Search, not the cards page
        if (User.IsSupervisor())
            return RedirectToAction(nameof(Search), new { searchQuery = (string?)null });

        // Provide dynamic asset types
        List<AIMS.ViewModels.Home.AssetCardVm>? types = null;
        try
        {
            types = (await _catalog.GetAllTypesAsync(HttpContext.RequestAborted)).ToList();
            ViewData["AssetTypes"] = types;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load asset type catalog for Home/Index; falling back to view-only.");
            ViewData["AssetTypes"] = null;
        }

        // Server-side snapshot for *first paint* (prevents number/dot flash)
        try
        {
            var wanted = types?.Select(t => t.AssetType).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            var snapshot = await _summarySvc.GetSummaryAsync(wanted, HttpContext.RequestAborted);
            ViewData["CardSnapshot"] = snapshot; // read by the partial
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Summary snapshot not available for first paint.");
            ViewData["CardSnapshot"] = null;
        }

        if (_hardwareQuery is not null && _softwareQuery is not null)
        {
            var model = new HomeIndexViewModel
            {
                Hardware = await _hardwareQuery.GetAllHardwareAsync(),
                Software = await _softwareQuery.GetAllSoftwareAsync()
            };
            return View(model);
        }

        return View();
    }

    public IActionResult Reports() => View();
    public IActionResult AuditLog() => View();
    public IActionResult Privacy() => View();

    [HttpGet]
    public IActionResult Search(string? searchQuery)
    {
        var q = string.IsNullOrWhiteSpace(searchQuery) ? null : searchQuery.Trim();
        ViewBag.SearchQuery = q;

        if (q is null)
        {
            ViewBag.Results = new List<Dictionary<string, string>>();
            return View();
        }

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

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
        => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });

    // ----------------------------------------------------------------------------
    // Details page with a server-side guard:
    // ----------------------------------------------------------------------------
    [AllowAnonymous] // dev only; public access, but Supervisors get redirected
    public async Task<IActionResult> AssetDetailsComponent(string? category, string? tag)
    {
        if (User.IsSupervisor())
            return RedirectToAction(nameof(Search), new { searchQuery = (string?)null });

        var requestedCategory = string.IsNullOrWhiteSpace(category) ? "Laptop" : category.Trim();

        ViewData["Category"] = requestedCategory;
        ViewData["Title"] = $"{requestedCategory} Asset Details";

        if (string.IsNullOrWhiteSpace(tag))
            return View();

        var t = tag.Trim();

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
            ViewData["MissingTag"] = t;
            return View();
        }

        if (!string.Equals(detectedType.Trim(), requestedCategory.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToAction(nameof(AssetDetailsComponent), new
            {
                category = detectedType,
                tag = t
            });
        }

        ViewData["Category"] = detectedType;
        ViewData["Title"] = $"{detectedType} Asset Details";
        return View();
    }
}
