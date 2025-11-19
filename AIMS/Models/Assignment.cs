using System;

namespace AIMS.Models;

public class Assignment
{
    public int AssignmentID { get; set; }

    // Who
    public int? UserID { get; set; } // nullable
    public User? User { get; set; }


    // What (XOR: one of these must be set; enforced via check constraint)
    public AssetKind AssetKind { get; set; }

    // NOTE: these reference PKs (NOT human tags)
    public int? HardwareID { get; set; } // when AssetKind = Hardware
    public Hardware? Hardware { get; set; }

    public int? SoftwareID { get; set; } // when AssetKind = Software
    public Software? Software { get; set; }

    // When
    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UnassignedAtUtc { get; set; }

    // Agreement file stored inline
    public byte[]? AgreementFile { get; set; }
    public string? AgreementFileName { get; set; }
    public string? AgreementContentType { get; set; }

    // convenience
    public bool IsActive => UnassignedAtUtc == null;

    // convenience: for grids/icons
    public bool HasAgreementFile => AgreementFile != null && AgreementFile.Length > 0;
}
