using System.Diagnostics;
using System.Threading.Tasks;
using AIMS.Models;
using AIMS.Queries;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace AIMS.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    // Optional services: will be null if not registered
    private readonly HardwareQuery? _hardwareQuery;
    private readonly SoftwareQuery? _softwareQuery;
    private readonly UserQuery? _userQuery;
    private readonly AssignmentsQuery? _assignQuery;

    public HomeController(ILogger<HomeController> logger, IServiceProvider sp)
    {
        _logger = logger;

        // Try-resolve; if not found, actions fall back to simple views
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

    public IActionResult Search(string? searchQuery)
    {
        ViewBag.SearchQuery = searchQuery ?? "";
        return View();
    }

    public IActionResult Privacy() => View();

    public IActionResult Feedback() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
        => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });

    public IActionResult AssetDetailsComponent(string? category)
    {
        ViewData["Category"] = string.IsNullOrWhiteSpace(category) ? "Laptop" : category;
        return View();
    }
}

// ViewModel is only used when services are available.
// (GetHardwareDto / GetSoftwareDto come from our existing query layer)
public class HomeIndexViewModel
{
    public List<GetHardwareDto> Hardware { get; set; } = new();
    public List<GetSoftwareDto> Software { get; set; } = new();
}
