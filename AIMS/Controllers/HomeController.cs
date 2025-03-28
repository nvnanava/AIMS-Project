using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using AIMS.Models;

namespace AIMS.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }
public IActionResult Index(string search)
{
    var tableData = new List<Dictionary<string, string>> {};

    if (!string.IsNullOrEmpty(search))
    {
        search = search.ToLower();
        tableData = tableData
            .Where(item => item.Any(entry => 
                entry.Value?.ToLower().Contains(search) ?? false))
            .ToList();
    }

    return View(tableData);
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

internal class Asset
{
    public string Name { get; set; }
    public string Type { get; set; }
    public string TagNumber { get; set; }
    public string AssignedTo { get; set; }
    public string Status { get; set; }
}