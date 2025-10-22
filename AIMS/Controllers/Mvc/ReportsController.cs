using Microsoft.AspNetCore.Mvc;

namespace AIMS.Controllers.Mvc;

public class ReportsController : Controller
{
    [HttpGet]
    public IActionResult Index()
        => View(); // Views/Reports/Index.cshtml
}
