using AIMS.Data;
using AIMS.Queries;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Controllers.Api;

// [Authorize(Roles = "Admin")] 
[ApiController]
[Route("api/office")]
public class OfficeController : ControllerBase
{
    private readonly AimsDbContext _db;

    // use the office Query to abstract complex logic
    private OfficeQuery _officeQuery;

    public OfficeController(AimsDbContext db, OfficeQuery officeQuery)
    {
        _db = db;
        _officeQuery = officeQuery;
    }

    // Get a list of Offices
    [HttpGet("list")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOffices()
    {
        var offices = await _db.Offices
            // return basic officeID and Name
            .Select(o => new { o.OfficeID, o.OfficeName })
            .ToListAsync();

        return Ok(offices);
    }

    // allow search queries for offices
    [HttpGet("search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchOffices([FromQuery] string query = "", CancellationToken ct = default)
    {
        // use Query method; this returns results in the shape of OfficeVm
        return Ok(await _officeQuery.SearchOfficesAsync(query, ct));
    }
}
