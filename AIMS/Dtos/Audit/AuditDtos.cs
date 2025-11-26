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

    public string Action { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // Expose decoded snapshot and attachment flags
    public string? SnapshotJson { get; set; }
    public bool HasAttachment { get; set; }
    public string? AttachmentContentType { get; set; }

    // Target
    public AssetKind AssetKind { get; set; }
    public int? HardwareID { get; set; }
    public int? SoftwareID { get; set; }
    public int? AssignmentID { get; set; }

    // Convenience
    public string? HardwareName { get; set; }
    public string? SoftwareName { get; set; }

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

    // Client inputs we convert to bytes
    public string? SnapshotJson { get; set; }
    public string? AttachmentBase64 { get; set; }
    public string? AttachmentContentType { get; set; }

    // Target
    [Required] public AssetKind AssetKind { get; set; }
    public int? HardwareID { get; set; }
    public int? SoftwareID { get; set; }

    public List<CreateAuditLogChangeDto>? Changes { get; set; }

    // Used for dedup/upsert (matches ExternalId on AuditLog)
    public Guid? ExternalId { get; set; }
    public int? AssignmentID { get; set; }
}
