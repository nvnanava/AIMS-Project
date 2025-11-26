using AIMS.Data;
using AIMS.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Controllers.Api;

[ApiController]
[Route("api/debug")]
public class DebugController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    private readonly AimsDbContext _context;

    public DebugController(AimsDbContext context, IWebHostEnvironment env)
    {
        _context = context;
        _env = env;
    }

    // Get a List of Offices in the local DB
    [HttpGet("offices")]
    public async Task<IActionResult> GetOffices()
    {
        var offices = await _context.Offices
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
        var exists = await _context.Offices.AnyAsync(o => o.OfficeName == "Test Office");
        if (exists)
            return Ok("Offices already exist — no action taken.");

        Office office = new Office { OfficeName = "Test Office", Location = "Test Office" };

        _context.Offices.Add(office);
        await _context.SaveChangesAsync();

        return Ok("Seeded a test office successfully.");
    }
}
