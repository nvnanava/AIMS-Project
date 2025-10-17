using AIMS.Data;
using AIMS.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Controllers.Api;

[ApiController]
[Route("api/debug")]
public class DebugController : ControllerBase
{
    private readonly AimsDbContext _context;

    public DebugController(AimsDbContext context)
    {
        _context = context;
    }

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
        if (await _context.Offices.AnyAsync())
            return Ok("Offices already exist — no action taken.");

        var offices = new[]
        {
                new Office { OfficeName = "Houston", Location = "Houston, TX" },
                new Office { OfficeName = "San Ramon", Location = "San Ramon, CA" },
                new Office { OfficeName = "Remote", Location = "Remote" }
            };

        _context.Offices.AddRange(offices);
        await _context.SaveChangesAsync();

        return Ok("Seeded 3 offices successfully.");
    }
}
