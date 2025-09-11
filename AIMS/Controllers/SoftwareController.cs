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
[Route("api/software")]
public class SoftwareController : ControllerBase
{
    private readonly AimsDbContext _db;
    private readonly SoftwareQuery _softwareQuery;
    public SoftwareController(AimsDbContext db, SoftwareQuery softwareQuery)
    {
        _db = db;
        _softwareQuery = softwareQuery;
    }

    [HttpGet("get-all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAllSoftware()
    {
        var users = await _softwareQuery.GetAllSoftwareAsync();
        return Ok(users);
    }
   

}
