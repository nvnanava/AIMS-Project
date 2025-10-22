using AIMS.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Controllers.Api;

// [Authorize(Roles = "Admin")] 
[ApiController]
[Route("api/office")]
public class OfficeController : ControllerBase
{
    private readonly AimsDbContext _db;

    public OfficeController(AimsDbContext db)
    {
        _db = db;
    }

    // Temporary debug endpoint to view valid office IDs and names
    [HttpGet("list")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOffices()
    {
        var offices = await _db.Offices
            .Select(o => new { o.OfficeID, o.OfficeName })
            .ToListAsync();

        return Ok(offices);
    }
}
