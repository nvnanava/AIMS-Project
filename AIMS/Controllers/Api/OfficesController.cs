using AIMS.Data;
using AIMS.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Controllers.Api;

[ApiController]
[Route("api/offices")]
public class OfficesController : ControllerBase
{
    private readonly AimsDbContext _context;

    public OfficesController(AimsDbContext context)
    {
        _context = context;
    }

    // GET /api/offices
    [HttpGet]
    public async Task<IActionResult> GetOffices()
    {
        var offices = await _context.Offices
            .Select(o => new { o.OfficeID, o.OfficeName })
            .ToListAsync();

        return Ok(offices);
    }

    // GET /api/offices/search?query=xxx
    [HttpGet("search")]
    public async Task<IActionResult> SearchOffices([FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Ok(Array.Empty<object>());

        var results = await _context.Offices
            .Where(o => o.OfficeName.Contains(query))
            .Select(o => new { o.OfficeID, o.OfficeName })
            .ToListAsync();

        return Ok(results);
    }
}
