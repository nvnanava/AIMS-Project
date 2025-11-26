using AIMS.Data;
using AIMS.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Controllers.Api;

[ApiController]
[Route("api/office")]
public class DebugController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    private readonly AimsDbContext _db;

    public DebugController(AimsDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    // Get a List of Offices in the local DB
    [HttpGet("offices")]
    public async Task<IActionResult> GetOffices()
    {
        var offices = await _db.Offices
            .Select(o => new { o.OfficeID, o.OfficeName })
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
        var exists = await _db.Offices.AnyAsync(o => o.OfficeName == "Test Office");
        if (exists)
            return Ok("Offices already exist — no action taken.");

        Office office = new Office { OfficeName = "Test Office", Location = "Test Office" };

        _db.Offices.Add(office);
        await _db.SaveChangesAsync();

        return Ok("Seeded a test office successfully.");
    }
}
