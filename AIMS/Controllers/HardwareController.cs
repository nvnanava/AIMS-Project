using System.Linq;
using AIMS.Data;
using AIMS.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Controllers;

// Commented out for now, enable when we have entraID
// [Authorize(Roles = "Admin")]
[ApiController]
[Route("api/hardware")]
public class HardwareController : ControllerBase
{
    private readonly AimsDbContext _db;
    private readonly HardwareQuery _hardwareQuery;
    public HardwareController(AimsDbContext db, HardwareQuery hardwareQuery)
    {
        _db = db;
        _hardwareQuery = hardwareQuery;
    }

    [HttpGet("get-all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAllHardware()
    {
        var users = await _hardwareQuery.GetAllHardwareAsync();
        return Ok(users);
    }


}
