using System;

using System.Collections.Generic;

namespace AIMS.Models;

public partial class Software
{
    public int SoftwareId { get; set; }

    public string SoftwareName { get; set; } = null!;

    public string SoftwareType { get; set; } = null!;

    public string SoftwareVersion { get; set; } = null!;

    public string SoftwareDeploymentLocation { get; set; } = null!;

    public string SoftwareLicenseKey { get; set; } = null!;

    public DateOnly? SoftwareLicenseExpiration { get; set; }

    public long SoftwareUsageData { get; set; }

    public decimal SoftwareCost { get; set; }
}
