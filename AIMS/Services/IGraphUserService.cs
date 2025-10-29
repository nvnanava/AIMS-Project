using Microsoft.Graph.Models;

namespace AIMS.Services;

public interface IGraphUserService // Interface for GraphUserService
{
    Task<List<User>> GetUsersAsync(string? search = null); // Method to get users with optional search
    Task<List<DirectoryObject>> GetUserRolesAsync(string userId); // Method to get user roles by userId
    Task<User?> GetUserByIdAsync(string graphObjectId, CancellationToken ct = default);
}

