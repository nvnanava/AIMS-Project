using System.Diagnostics;
using AIMS.Models;
using AIMS.Services;
using AIMS.Utilities;
using AIMS.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIMS.Controllers.Mvc;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly AssetTypeCatalogService _catalog;
    private readonly SummaryCardService _summarySvc;

    public HomeController(
        ILogger<HomeController> logger,
        AssetTypeCatalogService catalog,
        SummaryCardService summarySvc)
    {
        _logger = logger;
        _catalog = catalog;
        _summarySvc = summarySvc;
    }

    // Dashboard cards (home)
    public async Task<IActionResult> Index()
    {
        // Supervisors land on Search, not cards
        if (User.IsSupervisor())
            return RedirectToAction(nameof(SearchController.Index), "Search", new { searchQuery = (string?)null });

        // Dynamic asset type list for cards (used by _HomePageCardComponent)
        try
        {
            var types = (await _catalog.GetAllTypesAsync(HttpContext.RequestAborted)).ToList();
            ViewData["AssetTypes"] = types;

            // Server snapshot for first paint (prevents number/dot flash)
            var wantedTypes = types.Select(t => t.AssetType)
                                   .Where(s => !string.IsNullOrWhiteSpace(s))
                                   .ToList();

            var snapshot = await _summarySvc.GetSummaryAsync(wantedTypes, HttpContext.RequestAborted);
            ViewData["CardSnapshot"] = snapshot;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Home/Index: failed to load catalog and/or snapshot.");
            ViewData["AssetTypes"] = null;
            ViewData["CardSnapshot"] = null;
        }

        return View();
    }

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
        => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
