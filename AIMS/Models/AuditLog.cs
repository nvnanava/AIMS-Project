using System;

namespace AIMS.Models;

public class AuditLog
{
    // PK
    public int AuditLogID { get; set; }

    // Columns
    public DateTime Timestamp { get; set; }
    public string Action { get; set; } = string.Empty;     // e.g., Create/Edit/Assign/Archive
    public string Description { get; set; } = string.Empty;
    public string? PreviousValue { get; set; }             // JSON/plain text
    public string? NewValue { get; set; }                  // JSON/plain text

    // FKs (all optional except UserID)
    public int UserID { get; set; }
    public User User { get; set; } = null!;

    public int? AssetTag { get; set; }
    public Hardware? HardwareAsset { get; set; }

    public int? SoftwareID { get; set; }
    public Software? SoftwareAsset { get; set; }
}