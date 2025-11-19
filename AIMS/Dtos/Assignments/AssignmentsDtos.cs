using System;
using AIMS.Models;

namespace AIMS.Dtos.Assignments;

public class CreateAssignmentDto
{
    public int UserID { get; set; }

    public AssetKind AssetKind { get; set; } // Hardware or Software

    public int? HardwareID { get; set; }     // when Hardware
    public int? SoftwareID { get; set; }     // when Software

    // Comment to log on assign (used only for audit text)
    public string? Comment { get; set; }
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

    // Expose EmployeeNumber so JS can show "Name (12345)"
    public string? EmployeeNumber { get; set; }

    // What (exactly one set)
    public AssetKind AssetKind { get; set; }
    public int? HardwareID { get; set; }
    public int? SoftwareID { get; set; }

    // When
    public DateTime AssignedAtUtc { get; set; }
    public DateTime? UnassignedAtUtc { get; set; }

    // Agreement metadata for UI (optional to project in queries)
    public bool HasAgreementFile { get; set; }
    public string? AgreementFileName { get; set; }
}

public sealed class AssignmentCreatedDto
{
    public int AssignmentID { get; set; }
    public int UserID { get; set; }
    public AssetKind AssetKind { get; set; }
    public int? HardwareID { get; set; }
    public int? SoftwareID { get; set; }

}
