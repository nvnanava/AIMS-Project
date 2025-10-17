using AIMS.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIMS.Controllers.Mvc;

[Authorize(Policy = "mbcAdmin")] // Only users with the "Admin" role can access any action methods in this controller

public class AdminController : Controller
{
    private readonly IGraphUserService _graphUserService; // interface for better testability

    public AdminController(IGraphUserService graphUserService) // Constructor with dependency injection
    {
        _graphUserService = graphUserService; // Assign injected service to private field
    }
    [HttpGet("aad-users")] // Endpoint to get Azure AD users with optional search parameter
    public async Task<IActionResult> GetAzureAdUsers([FromQuery] string? search = null)
    {
        var users = await _graphUserService.GetUsersAsync(search); // Call service to get users
        return Ok(users); // Return users as JSON
    }
    [HttpGet("aad-users-roles/{userId}")] // Endpoint to get roles for a specific user by userId
    public async Task<IActionResult> GetUserRoles(string userId)
    {
        var roles = await _graphUserService.GetUserRolesAsync(userId);
        return Ok(roles); // Return roles as JSON
    }
    public IActionResult Index()
    {
        return View();
    }
}
