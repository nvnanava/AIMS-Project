namespace AIMS.ViewModels;

public sealed class AssetRowVm
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
}

