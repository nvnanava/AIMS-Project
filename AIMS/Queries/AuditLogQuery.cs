using AIMS.Data;
using AIMS.Models;
using Microsoft.EntityFrameworkCore;

public class AuditLogQuery
{

    private readonly AimsDbContext _db;

    public AuditLogQuery(AimsDbContext db)
    {
        _db = db;
    }

    public async Task<List<GetAuditRecordDto>> GetAllAuditRecordsAsync()
    {
        // Example query, adjust as needed
        return await _db.AuditLogs
            .Select(a => new GetAuditRecordDto
            {
                AuditLogID = a.AuditLogID,
                ExternalId = a.ExternalId,
                TimestampUtc = a.TimestampUtc,
                UserID = a.UserID,
                Action = a.Action,
                Description = a.Description,
                PreviousValue = a.PreviousValue,
                NewValue = a.NewValue,
                AssetKind = a.AssetKind,
                AssetTag = a.AssetTag,
                HardwareAsset = a.HardwareAsset,
                SoftwareID = a.SoftwareID,
                SoftwareAsset = a.SoftwareAsset
            })
            .ToListAsync();
    }

    public async Task<int> createAuditRecordAsync(CreateAuditRecordDto data)
    {
        // basic error checking
        if (data == null)
        {
            throw new InvalidDataException("Missing record data!");
        }

        // check if the user is valid
        bool userExists = await _db.Assignments.AnyAsync(u => u.UserID == data.UserID);
        if (!userExists)
        {
            throw new Exception($"User with ID {data.UserID} does not exist!");
        }

        // check if hardwareTag is valid, if hardware
        if (data.AssetKind == AssetKind.Hardware)
        {
            if (data.AssetTag == null)
                throw new Exception("For AssetKind=Hardware you must supply AssetTag (HardwareID).");
            var assetTagExists = await _db.HardwareAssets.AnyAsync(hw => hw.HardwareID == data.AssetTag);
            if (!assetTagExists)
            {
                throw new Exception("Please specify a valid AssetTag (HardwareID).");
            }
        }
        // check if softwareID is valid, if software

        else if (data.AssetKind == AssetKind.Software)
        {
            if (data.SoftwareID == null)
                throw new Exception("For AssetKind=Software you must supply SoftwareID.");

            var softwareIDExists = await _db.SoftwareAssets.AnyAsync(sw => sw.SoftwareID == data.SoftwareID);
            if (!softwareIDExists)
                throw new Exception("Please specify a valid SoftwareID.");
        }
        else
        {
            throw new Exception("Unknown AssetKind.");
        }


        // check if description is not empty
        if (string.IsNullOrEmpty(data.Description))
        {
            throw new Exception("Cannot have empty action description!");
        }

        AuditLog newRecord = new AuditLog
        {
            TimestampUtc = DateTime.UtcNow,
            UserID = data.UserID,
            Action = "Assign",
            Description = data.Description,
            AssetKind = data.AssetKind,
            AssetTag = data.AssetKind == AssetKind.Hardware ? data.AssetTag : null,
            SoftwareID = data.AssetKind == AssetKind.Software ? data.SoftwareID : null,
        };

        // finally, create assignment
        _db.AuditLogs.Add(newRecord);
        await _db.SaveChangesAsync();

        return newRecord.AuditLogID;
    }

    public async Task<GetAuditRecordDto?> GetAuditRecordAsync(int auditRecID)
    {
        return await _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.AuditLogID == auditRecID)
            .Select(a => new GetAuditRecordDto
            {
                AuditLogID = a.AuditLogID,
                ExternalId = a.ExternalId,
                TimestampUtc = a.TimestampUtc,
                UserID = a.UserID,
                Action = a.Action,
                Description = a.Description,
                PreviousValue = a.PreviousValue,
                NewValue = a.NewValue,
                AssetKind = a.AssetKind,
                AssetTag = a.AssetTag,
                HardwareAsset = a.HardwareAsset,
                SoftwareID = a.SoftwareID,
                SoftwareAsset = a.SoftwareAsset
            })
            .FirstOrDefaultAsync();
    }
}



public class GetAuditRecordDto
{

    // PK / identifiers
    public int AuditLogID { get; set; }
    public Guid ExternalId { get; set; } // for deterministic references/upserts

    // When / Who / What
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public int UserID { get; set; }

    // Action metadata
    public string Action { get; set; } = string.Empty; // e.g., Create/Edit/Assign/Archive
    public string Description { get; set; } = string.Empty;
    public string? PreviousValue { get; set; }
    public string? NewValue { get; set; }

    public AssetKind AssetKind { get; set; } // 1 = Hardware, 2 = Software
    public int? AssetTag { get; set; } // FK -> Hardware.HardwareID when AssetKind = Hardware
    public Hardware? HardwareAsset { get; set; }
    public int? SoftwareID { get; set; } // FK -> Software.SoftwareID when AssetKind = Software
    public Software? SoftwareAsset { get; set; }

}

public class CreateAuditRecordDto
{
    public int UserID { get; set; }

    // Action metadata
    public string Description { get; set; } = string.Empty;

    public AssetKind AssetKind { get; set; } // 1 = Hardware, 2 = Software
    public int? AssetTag { get; set; } // FK -> Hardware.HardwareID when AssetKind = Hardware
    public int? SoftwareID { get; set; } // FK -> Software.SoftwareID when AssetKind = Software
}