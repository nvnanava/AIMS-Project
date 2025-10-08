using System;
using AIMS.Models;

namespace AIMS.ViewModels;

public class CreateAssignmentDto
{
    public int UserID { get; set; }
    public AssetKind AssetKind { get; set; } // Hardware or Software
    public int? HardwareID { get; set; }     // when Hardware
    public int? SoftwareID { get; set; }     // when Software
}

public class CloseAssignmentDto
{
    public int AssignmentID { get; set; }
}

public class GetAssignmentDto
{
    public int AssignmentID { get; set; }

    // Who
    public int? UserID { get; set; }
    public string User { get; set; } = string.Empty;

    // What (exactly one set)
    public AssetKind AssetKind { get; set; }
    public int? HardwareID { get; set; }
    public int? SoftwareID { get; set; }

    // When
    public DateTime AssignedAtUtc { get; set; }
    public DateTime? UnassignedAtUtc { get; set; }
}
