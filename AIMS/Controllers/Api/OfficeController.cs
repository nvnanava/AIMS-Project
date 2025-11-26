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


    [HttpPost("seed-offices")]
    public async Task<IActionResult> SeedOffices()
    {
        if (!_env.IsDevelopment() && !_env.IsEnvironment("Playwright") && !_env.IsEnvironment("Test"))
        {
            return Forbid();
        }

        // Check if a Test Office already exists
        var exists = await _context.Offices.AnyAsync(o => o.OfficeName == "Test Office");
        if (exists)
            return Ok("Offices already exist — no action taken.");

        Office office = new Office { OfficeName = "Test Office", Location = "Test Office" };

        _context.Offices.Add(office);
        await _context.SaveChangesAsync();

        return Ok("Seeded a test office successfully.");
    }

}
