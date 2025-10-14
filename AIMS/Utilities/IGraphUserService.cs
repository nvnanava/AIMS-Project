using Microsoft.Graph.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AIMS.Utilities
{
    public interface IGraphUserService // Interface for GraphUserService
    {
        Task<List<User>> GetUsersAsync(string? search = null); // Method to get users with optional search
        Task<List<DirectoryObject>> GetUserRolesAsync(string userId); // Method to get user roles by userId
    }
}
