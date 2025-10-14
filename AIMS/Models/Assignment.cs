using System;
using System.ComponentModel.DataAnnotations;

namespace AIMS.Models;

public class Assignment
{
    public int AssignmentID { get; set; }

    // Who
    public int? UserID { get; set; } // nullable
    public User? User { get; set; }

    // Where (optional)
    public int? OfficeID { get; set; }
    public Office? Office { get; set; }

    // What (XOR: one of these must be set; enforced via check constraint)
    public AssetKind AssetKind { get; set; }

    // NOTE: these reference PKs (NOT human tags)
    public int? HardwareID { get; set; } // when AssetKind = Hardware
    public Hardware? Hardware { get; set; }

    public int? SoftwareID { get; set; } // when AssetKind = Software
    public Software? Software { get; set; }

    // When
    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UnassignedAtUtc { get; set; } // null == active

    // convenience
    public bool IsActive => UnassignedAtUtc == null;
}
