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
    private OfficeQuery _officeQuery;

    public OfficeController(AimsDbContext db, OfficeQuery officeQuery)
    {
        _db = db;
        _officeQuery = officeQuery;
    }

    [HttpGet("list")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOffices()
    {
        var offices = await _db.Offices
            .Select(o => new { o.OfficeID, o.OfficeName })
            .ToListAsync();

        return Ok(offices);
    }
    [HttpGet("search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchOffices([FromQuery] string query = "", CancellationToken ct = default)
    {
        return Ok(await _officeQuery.SearchOfficesAsync(query, ct));
    }
}
