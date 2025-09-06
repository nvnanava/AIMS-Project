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
[Route("api/user")]
public class UserController : ControllerBase
{
    private readonly AimsDbContext _db;
    private readonly UserQuery _userQuery;
    public UserController(AimsDbContext db, UserQuery userQuery)
    {
        _db = db;
        _userQuery = userQuery;
    }

    [HttpGet("get-all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAllUsers()
    {
        var users = await _userQuery.GetAllUsersAsync();
        return Ok(users);
    }
    
    [HttpGet("{searchString?}")]
    public async Task<IActionResult> SearchUsersByName([FromQuery] string? searchString)
    {
        if (searchString == null)
        {
            return Ok(await _userQuery.GetFirstNUsers(20));
        }
        var users = await _userQuery.SearchUserByName(searchString);
        return Ok(users);
    }
   

}
