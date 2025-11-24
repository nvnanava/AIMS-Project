using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIMS.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Services
{
    public interface ICurrentUser
    {
        string? GraphObjectId { get; }

        Task<int?> GetUserIdAsync(CancellationToken ct = default);
    }

    public sealed class CurrentUser : ICurrentUser
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly AimsDbContext _db;

        public CurrentUser(IHttpContextAccessor httpContextAccessor, AimsDbContext db)
        {
            _httpContextAccessor = httpContextAccessor;
            _db = db;
        }

        public string? GraphObjectId =>
            _httpContextAccessor.HttpContext?
                .User?
                .FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")
                ?.Value;

        public async Task<int?> GetUserIdAsync(CancellationToken ct = default)
        {
            var oid = GraphObjectId;
            if (string.IsNullOrWhiteSpace(oid))
                return null;

            return await _db.Users
                .Where(u => u.GraphObjectID == oid)
                .Select(u => (int?)u.UserID)
                .FirstOrDefaultAsync(ct);
        }
    }
}
