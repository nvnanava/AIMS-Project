namespace AIMS.ViewModels;

using AIMS.Models;

public class CreateAssignmentDto
{
    public int UserID { get; set; }
    public AssetKind AssetKind { get; set; } // Hardware or Software
    public int? AssetTag { get; set; }       // when Hardware: HardwareID
    public int? SoftwareID { get; set; }     // when Software: SoftwareID
}

public class CloseAssignmentDto
{
    public int AssignmentID { get; set; }
}

public class GetAssignmentDto
{
    public int AssignmentID { get; set; }

    // Who
    public int UserID { get; set; }
    public string User { get; set; } = string.Empty;

    // What (one of these must be set, enforced in controller/EF)
    public AssetKind AssetKind { get; set; }
    public int? HardwareID { get; set; }   // when Hardware
    public int? SoftwareID { get; set; }   // when Software

    // When
    public DateTime AssignedAtUtc { get; set; }
    public DateTime? UnassignedAtUtc { get; set; }
}
