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
public class UserController : ControllerBase
{
    private readonly AimsDbContext _db;
    public UserController(AimsDbContext db) => _db = db;

    [HttpGet("get-all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAllUsers()
    {
        var users = await _db.Users
            .AsNoTracking()
            .Select(u => new {
                u.UserID,
                u.FullName,
                u.Email,
                u.EmployeeNumber,
                Role = u.Role.RoleName,
                u.SupervisorID
            })
            .ToListAsync();
        return Ok(users);
    }
   

}
