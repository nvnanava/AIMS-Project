using AIMS.Data;
using AIMS.Dtos.Audit;
using AIMS.Models;
using AIMS.Services;
using Microsoft.EntityFrameworkCore;

public class AuditLogQuery
{
    private readonly AimsDbContext _db;
    private readonly IAuditEventBroadcaster _broadcaster; // inject broadcaster

    public AuditLogQuery(AimsDbContext db, IAuditEventBroadcaster broadcaster) // ctor updated
    {
        _db = db;
        _broadcaster = broadcaster;
    }

    public async Task<List<GetAuditRecordDto>> GetAllAuditRecordsAsync(CancellationToken ct = default)
    {
        return await _db.AuditLogs
            .AsNoTracking()
            .OrderByDescending(a => a.TimestampUtc)
            .Select(a => new GetAuditRecordDto
            {
                AuditLogID = a.AuditLogID,
                ExternalId = a.ExternalId,
                TimestampUtc = a.TimestampUtc,
                UserID = a.UserID,
                UserName = a.User.FullName,
                Action = a.Action,
                Description = a.Description,
                BlobUri = a.BlobUri,
                SnapshotJson = a.SnapshotJson,
                AssetKind = a.AssetKind,
                HardwareID = a.HardwareID,
                SoftwareID = a.SoftwareID,
                HardwareName = a.HardwareAsset != null ? a.HardwareAsset.AssetName : null,
                SoftwareName = a.SoftwareAsset != null ? a.SoftwareAsset.SoftwareName : null,
                Changes = a.Changes
                    .OrderBy(c => c.AuditLogChangeID)
                    .Select(c => new AuditLogChangeDto
                    {
                        AuditLogChangeID = c.AuditLogChangeID,
                        Field = c.Field,
                        OldValue = c.OldValue,
                        NewValue = c.NewValue
                    }).ToList()
            })
            .ToListAsync(ct);
    }

    public async Task<GetAuditRecordDto?> GetAuditRecordAsync(int auditLogId, CancellationToken ct = default)
    {
        return await _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.AuditLogID == auditLogId)
            .Select(a => new GetAuditRecordDto
            {
                AuditLogID = a.AuditLogID,
                ExternalId = a.ExternalId,
                TimestampUtc = a.TimestampUtc,
                UserID = a.UserID,
                UserName = a.User.FullName,
                Action = a.Action,
                Description = a.Description,
                BlobUri = a.BlobUri,
                SnapshotJson = a.SnapshotJson,
                AssetKind = a.AssetKind,
                HardwareID = a.HardwareID,
                SoftwareID = a.SoftwareID,
                HardwareName = a.HardwareAsset != null ? a.HardwareAsset.AssetName : null,
                SoftwareName = a.SoftwareAsset != null ? a.SoftwareAsset.SoftwareName : null,
                Changes = a.Changes
                    .OrderBy(c => c.AuditLogChangeID)
                    .Select(c => new AuditLogChangeDto
                    {
                        AuditLogChangeID = c.AuditLogChangeID,
                        Field = c.Field,
                        OldValue = c.OldValue,
                        NewValue = c.NewValue
                    }).ToList()
            })
            .FirstOrDefaultAsync(ct);
    }

    // Recent audit logs by asset
    public async Task<List<GetAuditRecordDto>> GetRecentAuditRecordsAsync(string assetKind, int assetId, int take = 5, CancellationToken ct = default)
    {
        var kind = Enum.TryParse<AssetKind>(assetKind, true, out var parsedKind) ? parsedKind : AssetKind.Hardware;

        return await _db.AuditLogs
            .AsNoTracking()
            .Where(a =>
                a.AssetKind == kind &&
                ((kind == AssetKind.Hardware && a.HardwareID == assetId) ||
                 (kind == AssetKind.Software && a.SoftwareID == assetId)))
            .OrderByDescending(a => a.TimestampUtc)
            .Take(take)
            .Select(a => new GetAuditRecordDto
            {
                AuditLogID = a.AuditLogID,
                ExternalId = a.ExternalId,
                TimestampUtc = a.TimestampUtc,
                UserID = a.UserID,
                UserName = a.User.FullName,
                Action = a.Action,
                Description = a.Description,
                AssetKind = a.AssetKind,
                HardwareID = a.HardwareID,
                SoftwareID = a.SoftwareID
            })
            .ToListAsync(ct);
    }

    public async Task<int> CreateAuditRecordAsync(CreateAuditRecordDto data, CancellationToken ct = default)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (string.IsNullOrWhiteSpace(data.Action)) throw new ArgumentException("Action is required.");
        if (string.IsNullOrWhiteSpace(data.Description)) throw new ArgumentException("Description is required.");

        // Validate user exists
        var userExists = await _db.Users.AsNoTracking().AnyAsync(u => u.UserID == data.UserID, ct);
        if (!userExists) throw new InvalidOperationException($"User with ID {data.UserID} does not exist.");

        // Validate target based on AssetKind (XOR)
        if (data.AssetKind == AssetKind.Hardware)
        {
            if (data.HardwareID is null)
                throw new InvalidOperationException("For AssetKind=Hardware, HardwareID must be provided.");
            var hwExists = await _db.HardwareAssets.AsNoTracking()
                .AnyAsync(h => h.HardwareID == data.HardwareID, ct);
            if (!hwExists) throw new InvalidOperationException("Please specify a valid HardwareID.");
            if (data.SoftwareID is not null)
                throw new InvalidOperationException("Specify only HardwareID for AssetKind=Hardware.");
        }
        else if (data.AssetKind == AssetKind.Software)
        {
            if (data.SoftwareID is null)
                throw new InvalidOperationException("For AssetKind=Software, SoftwareID must be provided.");
            var swExists = await _db.SoftwareAssets.AsNoTracking()
                .AnyAsync(s => s.SoftwareID == data.SoftwareID, ct);
            if (!swExists) throw new InvalidOperationException("Please specify a valid SoftwareID.");
            if (data.HardwareID is not null)
                throw new InvalidOperationException("Specify only SoftwareID for AssetKind=Software.");
        }
        else
        {
            throw new InvalidOperationException("Unknown AssetKind.");
        }

        var log = new AuditLog
        {
            TimestampUtc = DateTime.UtcNow,
            UserID = data.UserID,
            Action = data.Action,
            Description = data.Description,
            BlobUri = data.BlobUri,
            SnapshotJson = data.SnapshotJson,
            AssetKind = data.AssetKind,
            HardwareID = data.AssetKind == AssetKind.Hardware ? data.HardwareID : null,
            SoftwareID = data.AssetKind == AssetKind.Software ? data.SoftwareID : null
        };

        // Optional per-field diffs
        if (data.Changes is { Count: > 0 })
        {
            foreach (var c in data.Changes)
            {
                log.Changes.Add(new AuditLogChange
                {
                    Field = c.Field,
                    OldValue = c.OldValue,
                    NewValue = c.NewValue
                });
            }
        }

        _db.AuditLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        // Broadcast realtime event after persistence
        var userName = await _db.Users.AsNoTracking()
            .Where(u => u.UserID == log.UserID)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(ct);

        var dto = new AIMS.Contracts.AuditEventDto
        {
            Id = (log.ExternalId != Guid.Empty ? log.ExternalId.ToString() : log.AuditLogID.ToString()),
            OccurredAtUtc = log.TimestampUtc,
            Type = log.Action,
            User = $"{(userName ?? $"User#{log.UserID}")} ({log.UserID})",
            Target = log.AssetKind == AssetKind.Hardware
                ? (log.HardwareID.HasValue ? $"Hardware#{log.HardwareID}" : "Hardware")
                : (log.SoftwareID.HasValue ? $"Software#{log.SoftwareID}" : "Software"),
            Details = log.Description ?? "",
            Hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(
                    $"{log.AuditLogID}|{log.ExternalId}|{log.TimestampUtc:o}|{log.Action}|{log.UserID}|{log.AssetKind}|{log.HardwareID}|{log.SoftwareID}|{log.Description}"
                )))
        };

        await _broadcaster.BroadcastAsync(dto);

        return log.AuditLogID;
    }
}
