using AIMS.Data; // Add this line if AimsDbContext is in the Data namespace
using AIMS.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Controllers.Mvc;


[Authorize(Policy = "mbcAdmin")]
public class AdminController : Controller
{
    private readonly IGraphUserService _graphUserService;
    private readonly AimsDbContext _db;

    public AdminController(IGraphUserService graphUserService, AimsDbContext db)
    {
        _graphUserService = graphUserService;
        _db = db;
    }

    [HttpGet("aad-users")]
    public async Task<IActionResult> GetAzureAdUsers([FromQuery] string? search = null)
        => Ok(await _graphUserService.GetUsersAsync(search));

    [HttpGet("aad-users-roles/{userId}")]
    public async Task<IActionResult> GetUserRoles(string userId)
        => Ok(await _graphUserService.GetUserRolesAsync(userId));

    public async Task<IActionResult> Index()
    {
        // Return empty list - users are loaded via search API only
        return View(new List<AIMS.Models.User>());
    }
}
