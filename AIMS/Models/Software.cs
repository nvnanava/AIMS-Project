using System;
using System.ComponentModel.DataAnnotations;

namespace AIMS.Models;

public class Software
{
    public int SoftwareID { get; set; }

    [Required, MaxLength(128)]
    public string SoftwareName { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string SoftwareType { get; set; } = string.Empty;

    [MaxLength(64)]
    public string SoftwareVersion { get; set; } = string.Empty;

    // License key is the human tag for software (unique)
    [Required, MaxLength(128)]
    public string SoftwareLicenseKey { get; set; } = string.Empty; // unique

    public DateOnly? SoftwareLicenseExpiration { get; set; }

    public long SoftwareUsageData { get; set; }

    public decimal SoftwareCost { get; set; }

    public int LicenseTotalSeats { get; set; }
    public int LicenseSeatsUsed { get; set; }

    public string Comment { get; set; } = string.Empty;

    public bool IsArchived { get; set; } = false;
}
