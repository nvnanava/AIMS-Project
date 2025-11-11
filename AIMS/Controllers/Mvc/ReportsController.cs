using AIMS.Queries;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
namespace AIMS.Controllers.Mvc;

public class ReportsController : Controller
{
    // use OfficeQuery to pull a list of Offices to use on the frontend
    private OfficeQuery _officeQuery;

    public ReportsController(OfficeQuery officeQuery)
    {
        _officeQuery = officeQuery;
    }
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        // pull the currently logged in User's GraphObjectID from the Graph API
        ViewData["CurrentUserId"] = User.GetObjectId();
        // save a list of offices so that we do not need to query the API
        ViewData["Offices"] = await _officeQuery.GetAllOfficesAsync();
        return View();
    }
}
