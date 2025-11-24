using AIMS.Queries;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Services
{
    public interface IAdminUserUpsertService
    {
        Task<AIMS.Models.User> UpsertAdminUserAsync(string graphObjectId, int? roleId, int? supervisorId, int? officeId, CancellationToken ct);
    }

    public sealed class AdminUserUpsertService : IAdminUserUpsertService
    {
        private readonly AIMS.Data.AimsDbContext _db;
        private readonly IGraphUserService _graphUserService;
        private readonly OfficeQuery _officeQuery;

        public AdminUserUpsertService(AIMS.Data.AimsDbContext db, IGraphUserService graphUserService, OfficeQuery officeQuery)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _graphUserService = graphUserService ?? throw new ArgumentNullException(nameof(graphUserService));
            _officeQuery = officeQuery ?? throw new ArgumentNullException(nameof(officeQuery));
        }

        public async Task<AIMS.Models.User> UpsertAdminUserAsync(string graphObjectId, int? roleId, int? supervisorId, int? officeId, CancellationToken ct)
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
                    //IsActive = true,
                    IsArchived = false
                };

                if (roleId.HasValue) user.RoleID = roleId.Value;
                if (supervisorId.HasValue) user.SupervisorID = supervisorId.Value;
                if (officeId.HasValue) user.OfficeID = officeId.Value;

                _db.Users.Add(user);
            }
            else
            {
                var aadOffice = g.OfficeLocation ?? "";

                // Update existing user details
                user.FullName = g.DisplayName ?? user.FullName;
                user.Email = g.Mail ?? g.UserPrincipalName ?? user.Email;

                if (roleId.HasValue) user.RoleID = roleId.Value;
                if (supervisorId.HasValue) user.SupervisorID = supervisorId.Value;
                if (officeId.HasValue) user.OfficeID = officeId.Value;
                else if (!string.IsNullOrEmpty(aadOffice) && user.Office?.OfficeName != aadOffice)
                {

                    // check if the database contains an office of the same name
                    var office = await _db.Offices.Where(o => o.OfficeName.ToLower() == aadOffice.ToLower()).FirstOrDefaultAsync(ct);
                    // store the OfficeID
                    var OfficeId = office is not null ? office.OfficeID : -1;

                    // if a new office is being added
                    if (office is null)
                    {
                        // create the new office and retrieve its ID in the local DB
                        OfficeId = await _officeQuery.AddOffice(aadOffice);
                    }
                    user.OfficeID = OfficeId;
                }

                //user.IsActive = true;
                user.IsArchived = false;
            }
            try
            {
                await _db.SaveChangesAsync(ct);

            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Another writer beat us; fetch the row that now exists
                user = await _db.Users
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(u => u.GraphObjectID == graphObjectId, ct);
            }
            // Guarantee non-null on all exit paths to satisfy the non-nullable return type
            if (user == null)
                throw new InvalidOperationException(
                    $"Upsert failed: user with Graph ID '{graphObjectId}' was not found after save.");

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

