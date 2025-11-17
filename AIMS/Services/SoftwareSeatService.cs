using AIMS.Data;
using AIMS.Dtos.Audit;
using AIMS.Models;
using AIMS.Queries;
using AIMS.Utilities;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Services;

public class SoftwareSeatService
{
#if DEBUG
    // Test-only override for retry count. Null = use default (3).
    public static int? RetryOverride { get; set; }
#endif

    private readonly AimsDbContext _db;
    private readonly AuditLogQuery _audit;

    public SoftwareSeatService(AimsDbContext db, AuditLogQuery audit)
    {
        _db = db;
        _audit = audit;
    }

    /// <summary>
    /// Assigns a software seat to a user, enforcing capacity and one-open-assignment-per (software,user).
    /// Writes an audit record including optional comment and a link hint to the Audit Log.
    /// </summary>
    public async Task AssignSeatAsync(
        int softwareId,
        int userId,
        string? comment = null,
        CancellationToken ct = default)
    {
        // ---- Resolve user (for nice audit text) ----
        var userInfo = await _db.Users.AsNoTracking()
            .Where(u => u.UserID == userId)
            .Select(u => new { u.UserID, u.FullName, u.EmployeeNumber })
            .SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("User not found.");

        var empText = string.IsNullOrWhiteSpace(userInfo.EmployeeNumber) ? "N/A" : userInfo.EmployeeNumber;
        var whoLabel = $"{userInfo.FullName} (Emp #{empText})";

#if DEBUG
        var maxRetries = RetryOverride ?? 3;
#else
        const int maxRetries = 3;
#endif
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            // Load software + current rowversion (ignore global filters so we can detect archived)
            var sw = await _db.SoftwareAssets
                .IgnoreQueryFilters()
                .Where(s => s.SoftwareID == softwareId)
                .SingleOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException("Software not found.");

            if (sw.IsArchived)
                throw new InvalidOperationException("Cannot assign seat for archived software.");

            // --- Capacity guards (both styles) -----------------------------------
            if (sw.LicenseSeatsUsed >= sw.LicenseTotalSeats)
                throw new SeatCapacityException("No available seats for this software license.");

            var openCount = await _db.Assignments.CountAsync(a =>
                a.AssetKind == AssetKind.Software &&
                a.SoftwareID == softwareId &&
                a.UnassignedAtUtc == null, ct);

            if (openCount >= sw.LicenseTotalSeats)
                throw new SeatCapacityException("No available seats for this software license.");
            // ---------------------------------------------------------------------

            // Ensure at most one active assignment per (SoftwareID,UserID)
            var alreadyOpen = await _db.Assignments.AnyAsync(a =>
                    a.AssetKind == AssetKind.Software &&
                    a.SoftwareID == softwareId &&
                    a.UserID == userId &&
                    a.UnassignedAtUtc == null,
                ct);
            if (alreadyOpen) return; // idempotent

            // Snapshot counts for audit (Prev/New)
            var usedBefore = sw.LicenseSeatsUsed;
            var total = sw.LicenseTotalSeats;
            var usedAfter = Math.Min(usedBefore + 1, total);

            // Open the assignment row
            _db.Assignments.Add(new Assignment
            {
                UserID = userId,
                AssetKind = AssetKind.Software,
                SoftwareID = softwareId,
                AssignedAtUtc = DateTime.UtcNow,
                UnassignedAtUtc = null
            });

            // Increment (clamped; race protected by rowversion + retry)
            sw.LicenseSeatsUsed = usedAfter;

            try
            {
                await _db.SaveChangesAsync(ct);
                CacheStamp.BumpAssets();

                // ---- AUDIT with Prev/New seats + optional comment + link ----
                await _audit.CreateAuditRecordAsync(new CreateAuditRecordDto
                {
                    UserID = userId,
                    Action = "Assign",
                    Description = BuildAssignDescription(sw, whoLabel, comment),
                    AssetKind = AssetKind.Software,
                    SoftwareID = sw.SoftwareID,
                    Changes = new List<CreateAuditLogChangeDto>
                    {
                        new CreateAuditLogChangeDto
                        {
                            Field = "Seats",
                            OldValue = $"{usedBefore}/{total}",
                            NewValue = $"{usedAfter}/{total}"
                        }
                    }
                }, ct);
                // --------------------------------------------------------------------

                return; // success
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxRetries)
            {
                _db.ChangeTracker.Clear(); // refresh and retry
                continue;
            }
        }

        throw new DbUpdateConcurrencyException("Failed to assign seat after retries.");
    }

    /// <summary>
    /// Releases a software seat for a user (if open). Idempotent.
    /// Writes an audit record including optional comment and a link hint to the Audit Log.
    /// </summary>
    public async Task ReleaseSeatAsync(
        int softwareId,
        int userId,
        string? comment = null,
        CancellationToken ct = default)
    {
        // ---- Resolve user (for nice audit text) ----
        var userInfo = await _db.Users.AsNoTracking()
            .Where(u => u.UserID == userId)
            .Select(u => new { u.UserID, u.FullName, u.EmployeeNumber })
            .SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("User not found.");

        var empText = string.IsNullOrWhiteSpace(userInfo.EmployeeNumber) ? "N/A" : userInfo.EmployeeNumber;
        var whoLabel = $"{userInfo.FullName} (Emp #{empText})";

#if DEBUG
        var maxRetries = RetryOverride ?? 3;
#else
        const int maxRetries = 3;
#endif

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var sw = await _db.SoftwareAssets
                .IgnoreQueryFilters()
                .Where(s => s.SoftwareID == softwareId)
                .SingleOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException("Software not found.");

            // Find open assignment
            var open = await _db.Assignments
                .Where(a => a.AssetKind == AssetKind.Software
                         && a.SoftwareID == softwareId
                         && a.UserID == userId
                         && a.UnassignedAtUtc == null)
                .SingleOrDefaultAsync(ct);

            if (open is null) return; // idempotent

            // Snapshot counts for audit (Prev/New)
            var usedBefore = sw.LicenseSeatsUsed;
            var total = sw.LicenseTotalSeats;
            var usedAfter = Math.Max(0, usedBefore - 1);

            open.UnassignedAtUtc = DateTime.UtcNow;

            // Safe decrement (never negative)
            sw.LicenseSeatsUsed = usedAfter;

            try
            {
                await _db.SaveChangesAsync(ct);
                CacheStamp.BumpAssets();

                // ---- AUDIT with Prev/New seats + optional comment + link ----
                await _audit.CreateAuditRecordAsync(new CreateAuditRecordDto
                {
                    UserID = userId,
                    Action = "Unassign",
                    Description = BuildReleaseDescription(sw, whoLabel, comment),
                    AssetKind = AssetKind.Software,
                    SoftwareID = sw.SoftwareID,
                    Changes = new List<CreateAuditLogChangeDto>
                    {
                        new CreateAuditLogChangeDto
                        {
                            Field = "Seats",
                            OldValue = $"{usedBefore}/{total}",
                            NewValue = $"{usedAfter}/{total}"
                        }
                    }
                }, ct);
                // --------------------------------------------------------------------

                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxRetries)
            {
                _db.ChangeTracker.Clear();
                continue;
            }
        }

        throw new DbUpdateConcurrencyException("Failed to release seat after retries.");
    }

    // ------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------

    private static string BuildAssignDescription(Software sw, string whoLabel, string? comment)
    {
        var baseText = $"Assigned seat for {sw.SoftwareName} {sw.SoftwareVersion} to {whoLabel}";
        var commentPart = string.IsNullOrWhiteSpace(comment)
            ? string.Empty
            : $" Comment: {comment.Trim()}";

        return baseText + commentPart;
    }

    private static string BuildReleaseDescription(Software sw, string whoLabel, string? comment)
    {
        var baseText = $"Released seat for {sw.SoftwareName} {sw.SoftwareVersion} from {whoLabel}";
        var commentPart = string.IsNullOrWhiteSpace(comment)
            ? string.Empty
            : $" Comment: {comment.Trim()}";

        return baseText + commentPart;
    }
}

public sealed class SeatCapacityException : Exception
{
    public SeatCapacityException(string message) : base(message) { }
}
