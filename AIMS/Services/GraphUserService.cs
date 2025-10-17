using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace AIMS.Services;

public class GraphUserService : IGraphUserService
{
    private readonly GraphServiceClient _graphClient;

    public GraphUserService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient;
    }

    // Fetch a list of Azure AD users
    public async Task<List<User>> GetUsersAsync(string? search = null)
    {
        var usersResponse = await _graphClient.Users.GetAsync(requestConfig =>
        {
            requestConfig.QueryParameters.Top = 10;
            requestConfig.QueryParameters.Select = new[] { "id", "displayName", "mail", "userPrincipalName" };
            if (!string.IsNullOrEmpty(search))
            {
                requestConfig.QueryParameters.Filter = $"startswith(displayName,'{search}')";
            }
        });
        return usersResponse?.Value?.ToList() ?? new List<User>();
    }
    // Fetch the roles/groups the user belongs to
    public async Task<List<DirectoryObject>> GetUserRolesAsync(string userId)
    {
        var result = await _graphClient.Users[userId].MemberOf.GetAsync();
        return result?.Value?.ToList() ?? new List<DirectoryObject>();
    }
}
