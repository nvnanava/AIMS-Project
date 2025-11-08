using AIMS.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Controllers.Api;

[ApiController]
[Route("api/office")]
public class OfficeController : ControllerBase
{
    private readonly AimsDbContext _db;

    public OfficeController(AimsDbContext db)
    {
        _db = db;
    }

    // GET /api/office/list
    // Used by the app to populate office dropdowns, etc.
    [HttpGet("list")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOffices()
    {
        var offices = await _db.Offices
            .AsNoTracking()
            .Select(o => new
            {
                o.OfficeID,
                o.OfficeName
            })
            .ToListAsync();

        return Ok(offices);
    }
}
