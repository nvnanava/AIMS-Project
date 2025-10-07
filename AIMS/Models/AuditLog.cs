namespace AIMS.Models;

public class AuditLog
{
    public int AuditLogID { get; set; }
    public Guid ExternalId { get; set; } = Guid.NewGuid();

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    // Who did it
    public int UserID { get; set; }
    public User User { get; set; } = null!;

    // What happened
    public string Action { get; set; } = string.Empty;        // e.g. "Create", "Edit", "Assign", "Archive"
    public string Description { get; set; } = string.Empty;   // human summary

    // Optional long-form payloads
    public string? BlobUri { get; set; }          // large attachments (screenshots, exported JSON, etc.)
    public string? SnapshotJson { get; set; }     // OPTIONAL: full serialized asset after change (for quick restore)

    // Target (exactly one of these per AssetKind)
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
    public string Field { get; set; } = string.Empty;

    // Store as strings for portability; app can render/parse types as needed
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}
