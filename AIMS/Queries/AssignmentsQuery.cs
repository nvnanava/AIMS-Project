// File: AIMS/Queries/AssignmentsQuery.cs
using AIMS.Data;
using AIMS.Models;
using AIMS.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Queries;

public class AssignmentsQuery
{
    private readonly AimsDbContext _db;
    public AssignmentsQuery(AimsDbContext db) => _db = db;

    // status: "active" (default), "closed", or "all"
    public async Task<List<GetAssignmentDto>> GetAllAssignmentsAsync(string status = "active", CancellationToken ct = default)
    {
        var norm = (status ?? "active").Trim().ToLowerInvariant();
        var q = _db.Assignments.AsNoTracking();

        q = norm switch
        {
            "active" => q.Where(a => a.UnassignedAtUtc == null),
            "closed" => q.Where(a => a.UnassignedAtUtc != null),
            "all" => q,
            _ => q.Where(a => a.UnassignedAtUtc == null) // fallback
        };

        return await q
            .OrderByDescending(a => a.AssignedAtUtc)
            .Select(a => new GetAssignmentDto
            {
                AssignmentID = a.AssignmentID,
                AssetKind = a.AssetKind,
                UserID = a.UserID,
                User = a.User.FullName,
                HardwareID = a.AssetTag,
                SoftwareID = a.SoftwareID,
                AssignedAtUtc = a.AssignedAtUtc,
                UnassignedAtUtc = a.UnassignedAtUtc
            })
            .ToListAsync(ct);
    }

    public async Task<GetAssignmentDto?> GetAssignmentAsync(int assignmentId, CancellationToken ct = default)
    {
        return await _db.Assignments
            .AsNoTracking()
            .Where(a => a.AssignmentID == assignmentId)
            .Select(a => new GetAssignmentDto
            {
                AssignmentID = a.AssignmentID,
                AssetKind = a.AssetKind,
                UserID = a.UserID,
                User = a.User.FullName,
                HardwareID = a.AssetTag,
                SoftwareID = a.SoftwareID,
                AssignedAtUtc = a.AssignedAtUtc,
                UnassignedAtUtc = a.UnassignedAtUtc
            })
            .FirstOrDefaultAsync(ct);
    }
}
