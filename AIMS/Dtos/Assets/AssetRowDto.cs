using AIMS.Dtos.Assignments;

namespace AIMS.Dtos.Assets;

public sealed class AssetRowDto
{
    public int? HardwareID { get; init; }
    public int? SoftwareID { get; init; }
    public string AssetName { get; init; } = "";
    public string Type { get; init; } = "";
    public string Tag { get; init; } = "";
    public string AssignedTo { get; init; } = "";
    public string Status { get; init; } = "";
    public int? AssignedUserId { get; init; }
    public string? AssignedEmployeeNumber { get; init; }
    public string? AssignedEmployeeName { get; init; }
    public DateTime? AssignedAtUtc { get; set; }

    public string? Comment { get; set; }
    public bool IsArchived { get; set; }
    public int? LicenseSeatsUsed { get; set; }
    public int? LicenseTotalSeats { get; set; }

    // For multi-seat software rows: all active seat assignments on this page.
    public List<SeatAssignmentChipDto> SeatAssignments { get; set; } = new();
}

// DTO for software seat chips
public sealed class SeatAssignmentChipDto
{
    public int UserId { get; init; }
    public string? DisplayName { get; init; }
    public string? EmployeeNumber { get; init; }
}

