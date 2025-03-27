using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using AIMS.Models;
using System.Linq;

namespace AIMS.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly AIMSContext _context;

    public HomeController(ILogger<HomeController> logger, AIMSContext context)
    {
        _logger = logger;
        _context = context;
    }

    public IActionResult Index(string search, string sortBy)
    {
        var assets = _context.Assets.AsQueryable();

        if (!string.IsNullOrEmpty(search)) {
            assets = assets.Where(a => a.Name.Contains(search) || a.Type.Contains(search) || a.Status.Contains(search) || (a.AssignedTo ?? "").Contains(search));
        }
        assets = sortBy?.ToLower() switch {
            "name" => assets.OrderBy(a => a.Name),
            "type" => assets.OrderBy(a => a.Type),
            "status" => assets.OrderBy(a => a.Status),
            "assigned" => assets.OrderBy(a => a.AssignedTo),
            _=> assets
        };
        return View(assets.ToList());
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
