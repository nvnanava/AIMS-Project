using System.ComponentModel.DataAnnotations;

namespace AIMS.Dtos.Software;

public class GetSoftwareDto
{
    public int SoftwareID { get; set; }
    public string SoftwareName { get; set; } = string.Empty;
    public string SoftwareType { get; set; } = string.Empty;
    public string SoftwareVersion { get; set; } = string.Empty;
    public string SoftwareLicenseKey { get; set; } = string.Empty;
    public DateOnly? SoftwareLicenseExpiration { get; set; }
    public long SoftwareUsageData { get; set; }
    public decimal SoftwareCost { get; set; }
    public int LicenseTotalSeats { get; set; }
    public int LicenseSeatsUsed { get; set; }
    public string Comment { get; set; } = string.Empty;
}

public class CreateSoftwareDto
{
    [Required, MaxLength(128)]
    public string SoftwareName { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string SoftwareType { get; set; } = string.Empty;

    [MaxLength(64)]
    public string SoftwareVersion { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string SoftwareLicenseKey { get; set; } = string.Empty;

    public DateOnly? SoftwareLicenseExpiration { get; set; }

    [Range(0, long.MaxValue)]
    public long SoftwareUsageData { get; set; }

    [Range(0, double.MaxValue)]
    public decimal SoftwareCost { get; set; }

    [Range(0, int.MaxValue)]
    public int LicenseTotalSeats { get; set; }

    [Range(0, int.MaxValue)]
    public int LicenseSeatsUsed { get; set; }

    public string Comment { get; set; } = string.Empty;
}

public class UpdateSoftwareDto
{
    [MaxLength(128)]
    public string? SoftwareName { get; set; }

    [MaxLength(64)]
    public string? SoftwareType { get; set; }

    [MaxLength(64)]
    public string? SoftwareVersion { get; set; }

    [MaxLength(128)]
    public string? SoftwareLicenseKey { get; set; } // unique

    public DateOnly? SoftwareLicenseExpiration { get; set; }

    [Range(0, long.MaxValue)]
    public long? SoftwareUsageData { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? SoftwareCost { get; set; }

    [Range(0, int.MaxValue)]
    public int? LicenseTotalSeats { get; set; }

    [Range(0, int.MaxValue)]
    public int? LicenseSeatsUsed { get; set; }

    public string? Comment { get; set; }
}

public sealed class AssignSeatRequestDto
{
    [Range(1, int.MaxValue)]
    public int SoftwareID { get; set; }

    [Range(1, int.MaxValue)]
    public int UserID { get; set; }
}

public sealed class ReleaseSeatRequestDto
{
    [Range(1, int.MaxValue)]
    public int SoftwareID { get; set; }

    [Range(1, int.MaxValue)]
    public int UserID { get; set; }
}

public sealed class SeatOperationResultDto
{
    public int SoftwareID { get; set; }
    public int LicenseTotalSeats { get; set; }
    public int LicenseSeatsUsed { get; set; }
    public string Message { get; set; } = "";
}
