using System;

namespace AIMS.Models;

public class AuditLog
{
    // PK / identifiers
    public int AuditLogID { get; set; }
    public Guid ExternalId { get; set; } // for deterministic references/upserts

    // When / Who / What
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public int UserID { get; set; }
    public User User { get; set; } = null!;

    // Action metadata
    public string Action { get; set; } = string.Empty; // e.g., Create/Edit/Assign/Archive
    public string Description { get; set; } = string.Empty;
    public string? PreviousValue { get; set; }
    public string? NewValue { get; set; }

    // Target asset (XOR with check constraint; must match AssetKind)
    public AssetKind AssetKind { get; set; } // 1 = Hardware, 2 = Software
    public int? AssetTag { get; set; } // FK -> Hardware.HardwareID when AssetKind = Hardware
    public Hardware? HardwareAsset { get; set; }
    public int? SoftwareID { get; set; } // FK -> Software.SoftwareID when AssetKind = Software
    public Software? SoftwareAsset { get; set; }
}
