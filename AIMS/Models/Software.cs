using System;

namespace AIMS.Models;

public class Software
{
    // PK
    public int SoftwareID { get; set; }

    // Columns
    public string SoftwareName { get; set; } = string.Empty;
    public string SoftwareType { get; set; } = string.Empty;
    public string SoftwareVersion { get; set; } = string.Empty;
    public string SoftwareLicenseKey { get; set; } = string.Empty; // unique
    public DateOnly? SoftwareLicenseExpiration { get; set; }
    public long SoftwareUsageData { get; set; }
    public decimal SoftwareCost { get; set; }
}