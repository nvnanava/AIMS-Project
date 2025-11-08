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
            requestConfig.QueryParameters.Select = new[] { "id", "displayName", "mail", "userPrincipalName", "officeLocation" };
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

    // Fetch a single user by Graph object id
    public async Task<User?> GetUserByIdAsync(string graphObjectId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(graphObjectId)) //guardrail in case of invalid input
            throw new ArgumentException("Graph object id is required.", nameof(graphObjectId));

        try
        {
            return await _graphClient.Users[graphObjectId].GetAsync(cfg =>
            {
                cfg.QueryParameters.Select = new[] { "id", "displayName", "mail", "userPrincipalName" }; //this can be extended as needed
            }, ct);
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError ex) when (ex.Error?.Code == "Request_ResourceNotFound")
        {
            return null; // not found in AAD
        }
    }
}
