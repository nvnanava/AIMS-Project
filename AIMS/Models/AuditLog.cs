using System.ComponentModel.DataAnnotations;

namespace AIMS.Models;

public enum AuditLogAction { Create = 1, Edit = 2, Assign = 3, Unassign = 4, Archive = 5 };
public class AuditLog
{
    public int AuditLogID { get; set; }

    [Required]
    public Guid ExternalId { get; set; } = Guid.NewGuid();

    [Required]
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    // Who did it
    [Required]
    public int UserID { get; set; }
    public User User { get; set; } = null!;

    // What happened
    public AuditLogAction Action { get; set; } // e.g. "Create", "Edit", "Assign", "Archive"
    public string Description { get; set; } = string.Empty;   // human summary

    // Inline storage (no external blob store)
    public byte[]? AttachmentBytes { get; set; }      // small attachments (logs, text files, etc.)
    public string? AttachmentContentType { get; set; }  // MIME type of AttachmentBytes; no max length.

    public byte[]? SnapshotBytes { get; set; }        // OPTIONAL: full serialized asset after change (for quick restore)
    public string? SnapshotContentType { get; set; }    // MIME type of SnapshotBytes; no max length.

    // Target (exactly one of these per AssetKind)
    [Required]
    public AssetKind AssetKind { get; set; }

    public int? HardwareID { get; set; }
    public Hardware? HardwareAsset { get; set; }

    public int? SoftwareID { get; set; }
    public Software? SoftwareAsset { get; set; }

    // Fine-grained changes (zero or more)
    public ICollection<AuditLogChange> Changes { get; set; } = new List<AuditLogChange>();
}

public class AuditLogChange
{
    public int AuditLogChangeID { get; set; }

    public int AuditLogID { get; set; }
    public AuditLog AuditLog { get; set; } = null!;

    // Which property changed on the asset (e.g., "AssetTag", "Status")
    [Required]
    public string Field { get; set; } = string.Empty;

    // Store as strings for portability; app can render/parse types as needed
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}
