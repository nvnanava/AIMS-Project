using System;
using System.ComponentModel.DataAnnotations;

namespace AIMS.Models;

public class AddSoftwareDto
{
    [Required]
    
    public int SoftwareId { get; set; } = 0;

    public string SoftwareName { get; set; } = null!;
    public string SoftwareType { get; set; } = null!;
    public string SoftwareVersion { get; set; } = null!;
    public string SoftwareDeploymentLocation { get; set; } = null!;
    public string SoftwareLicenseKey { get; set; } = null!;
    [DataType(DataType.Date)]
    public DateOnly? SoftwareLicenseExpiration { get; set; }
    public long SoftwareUsageData { get; set; }
    public decimal SoftwareCost { get; set; }
}
