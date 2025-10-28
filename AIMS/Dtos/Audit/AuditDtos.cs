using System.ComponentModel.DataAnnotations;
using AIMS.Models;

namespace AIMS.Dtos.Audit;

public sealed class AuditLogChangeDto
{
    public int AuditLogChangeID { get; set; }
    public string Field { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}

public sealed class GetAuditRecordDto
{
    // Identifiers
    public int AuditLogID { get; set; }
    public Guid ExternalId { get; set; }

    // When / Who / What
    public DateTime TimestampUtc { get; set; }
    public int UserID { get; set; }
    public string UserName { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;        // e.g. "Create", "Edit", "Assign", "Archive"
    public string Description { get; set; } = string.Empty;

    // Optional large payloads (blob-backed, snapshot inline)
    public string? BlobUri { get; set; }
    public string? SnapshotJson { get; set; }

    // Target (exactly one based on AssetKind)
    public AssetKind AssetKind { get; set; }
    public int? HardwareID { get; set; }
    public int? SoftwareID { get; set; }

    // Convenience display
    public string? HardwareName { get; set; }
    public string? SoftwareName { get; set; }

    // Fine-grained changes (0..N)
    public List<AuditLogChangeDto> Changes { get; set; } = new();
}

public sealed class CreateAuditLogChangeDto
{
    [Required] public string Field { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}

public sealed class CreateAuditRecordDto
{
    [Required] public int UserID { get; set; }

    [Required] public string Action { get; set; } = string.Empty;
    [Required] public string Description { get; set; } = string.Empty;

    // Optional long-form payloads
    public string? BlobUri { get; set; }
    public string? SnapshotJson { get; set; }

    // Target (exactly one must be set and match AssetKind)
    [Required] public AssetKind AssetKind { get; set; }
    public int? HardwareID { get; set; }
    public int? SoftwareID { get; set; }

    // Optional per-field diffs to insert as child rows
    public List<CreateAuditLogChangeDto>? Changes { get; set; }

    // Used for dedup/upsert (matches ExternalId on AuditLog)
    public Guid? ExternalId { get; set; }
}
