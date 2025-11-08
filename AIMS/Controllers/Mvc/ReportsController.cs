using AIMS.Queries;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
namespace AIMS.Controllers.Mvc;

public class ReportsController : Controller
{
    private OfficeQuery _officeQuery;

    public ReportsController(OfficeQuery officeQuery)
    {
        _officeQuery = officeQuery;
    }
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewData["CurrentUserId"] = User.GetObjectId();
        ViewData["Offices"] = await _officeQuery.GetAllOfficesAsync();
        return View();
    }
}
