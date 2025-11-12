using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using AIMS.Data;
using AIMS.Models;

public interface ICurrentUser
{
    string? GraphObjectId { get; }
    Task<int?> GetUserIdAsync(CancellationToken ct = default);

}


public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _http;
    private readonly AimsDbContext _db;

    public CurrentUser(IHttpContextAccessor http, AimsDbContext db)
    {
        _http = http;
        _db = db;
    }

    public string? GraphObjectId =>
        _http.HttpContext?.User?.FindFirst("oid")?.Value
        ?? _http.HttpContext?.User?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        // NOTE: no fallback to ClaimTypes.NameIdentifier (that’s the JWT 'sub')

    public async Task<int?> GetUserIdAsync(CancellationToken ct = default)
    {
        var goid = GraphObjectId;
        if (string.IsNullOrWhiteSpace(goid)) return null;

        return await _db.Users
            .Where(u => u.GraphObjectID == goid && !u.IsArchived)
            .Select(u => (int?)u.UserID)
            .SingleOrDefaultAsync(ct);
    }
}
