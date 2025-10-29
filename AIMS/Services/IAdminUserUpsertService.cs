using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Services
{
    public interface IAdminUserUpsertService
    {
        Task<AIMS.Models.User> UpsertAdminUserAsync(string graphObjectId, int? roleId, int? supervisorId, CancellationToken ct);
    }

    public sealed class AdminUserUpsertService : IAdminUserUpsertService
    {
        private readonly AIMS.Data.AimsDbContext _db;
        private readonly IGraphUserService _graphUserService;

        public AdminUserUpsertService(AIMS.Data.AimsDbContext db, IGraphUserService graphUserService)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _graphUserService = graphUserService ?? throw new ArgumentNullException(nameof(graphUserService));
        }

        public async Task<AIMS.Models.User> UpsertAdminUserAsync(string graphObjectId, int? roleId, int? supervisorId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(graphObjectId))
                throw new ArgumentException("graphObjectId is required", nameof(graphObjectId));

            // Fetch user from AAD
            var g = await _graphUserService.GetUserByIdAsync(graphObjectId, ct);
            if (g == null)
                throw new InvalidOperationException($"User with Graph ID '{graphObjectId}' not found in AAD.");

            // Try to locate by GraphObjectId in the DB
            var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.GraphObjectID == graphObjectId, ct);

            if (user == null)
            {
                user = new AIMS.Models.User
                {
                    GraphObjectID = graphObjectId,
                    FullName = g.DisplayName ?? "Unknown",
                    Email = g.Mail ?? g.UserPrincipalName ?? "",
                    EmployeeNumber = Guid.NewGuid().ToString("N")[..8],
                    IsActive = true,
                    IsArchived = false
                };

                if (roleId.HasValue) user.RoleID = roleId.Value;
                if (supervisorId.HasValue) user.SupervisorID = supervisorId.Value;

                _db.Users.Add(user);
            }
            else
            {
                // Update existing user details
                user.FullName = g.DisplayName ?? user.FullName;
                user.Email = g.Mail ?? g.UserPrincipalName ?? user.Email;

                if (roleId.HasValue) user.RoleID = roleId.Value;
                if (supervisorId.HasValue) user.SupervisorID = supervisorId.Value;

                user.IsActive = true;
                user.IsArchived = false;
            }
            try
            {
                await _db.SaveChangesAsync(ct);

            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.GraphObjectID == graphObjectId, ct);

            }


            return user;
        }
        private static bool IsUniqueViolation(DbUpdateException ex)
        {
            if (ex.InnerException is SqlException sqlEx)
                return sqlEx.Number == 2601 || sqlEx.Number == 2627; // Unique constraint violation numbers
            return false;

        }
    }
}

