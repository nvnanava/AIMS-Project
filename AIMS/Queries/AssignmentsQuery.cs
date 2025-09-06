using System.Security.Cryptography.X509Certificates;
using AIMS.Data;
using AIMS.Models;
using Microsoft.EntityFrameworkCore;

public class AssignmentsQuery
{
    private readonly AimsDbContext _db;
    public AssignmentsQuery(AimsDbContext db) => _db = db;

    public async Task<List<GetAssignmentDto>> GetAllAssignmentsAsync()
    {
        // Example query, adjust as needed
        return await _db.Assignments
            .Select(a => new GetAssignmentDto
            {
                AssignmentID = a.AssignmentID,
                AssetKind = a.AssetKind,
                UserID = a.UserID,
                User = a.User.FullName,
                HardwareID = a.AssetTag,
                SoftwareID = a.SoftwareID,
                AssignedAtUtc = a.AssignedAtUtc
            })
            .ToListAsync();
    }

    public async Task<List<GetAssignmentDto>> GetActiveAssignments()
    {
        var rows = await _db.Assignments
            .AsNoTracking()
            .Where(a => a.UnassignedAtUtc == null)
            .Select(a => new GetAssignmentDto
            {
                AssignmentID = a.AssignmentID,
                AssetKind = a.AssetKind,
                UserID = a.UserID,
                User = a.User.FullName,
                HardwareID = a.AssetTag,
                SoftwareID = a.SoftwareID,
                AssignedAtUtc = a.AssignedAtUtc
            })
            .ToListAsync();

        return rows;
    }

    public async Task<GetAssignmentDto> GetAssignmentAsync(int AssignmentID)
    {
     return await _db.Assignments.
        AsNoTracking()
        .Where(a => a.AssignmentID == AssignmentID).
        Select(a => new GetAssignmentDto
        {
            AssignmentID = a.AssignmentID,
                AssetKind = a.AssetKind,
                UserID = a.UserID,
                User = a.User.FullName,
                HardwareID = a.AssetTag,
                SoftwareID = a.SoftwareID,
                AssignedAtUtc = a.AssignedAtUtc
        }).
        FirstOrDefaultAsync();   
    }
}




public class GetAssignmentDto {
    
     public int AssignmentID { get; set; }

    // Who
    public int UserID { get; set; }
    public string User { get; set; } = string.Empty;

    // What (one of these must be set, enforced in code/migration)
    public AssetKind AssetKind { get; set; }
    public int? HardwareID { get; set; }      // when Hardware
    public int? SoftwareID { get; set; }    // when Software

    // When
    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;

}