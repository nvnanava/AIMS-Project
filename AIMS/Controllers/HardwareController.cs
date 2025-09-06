using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using AIMS.Data;
using AIMS.Models;

namespace AIMS.Controllers;

// Commented out for now, enable when we have entraID
// [Authorize(Roles = "Admin")]
[ApiController]
[Route("api/hardware")]
public class HardwareController : ControllerBase
{
    private readonly AimsDbContext _db;
    public HardwareController(AimsDbContext db) => _db = db;

    [HttpGet("get-all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAllHardware()
    {
         var users = await _db.HardwareAssets
            .AsNoTracking()
            .Select(h => new
            {
                h.HardwareID,
                h.AssetName,
                h.Status,
            })
            .ToListAsync();
        return Ok(users);
    }
   

}
