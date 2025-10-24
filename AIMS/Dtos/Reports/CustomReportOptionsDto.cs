using System.ComponentModel;

namespace AIMS.Dtos.Reports;

public class CustomReportOptionsDto
{
    [DefaultValue(true)]
    public bool seeHardware { get; set; } = true;
    [DefaultValue(true)]
    public bool seeSoftware { get; set; } = true;
    [DefaultValue(true)]
    public bool seeUsers { get; set; } = true;
    [DefaultValue(true)]
    public bool seeOffice { get; set; } = true;
    [DefaultValue(false)]
    public bool seeExpiration { get; set; } = false;
    [DefaultValue(false)]
    public bool filterByMaintenance { get; set; } = false;
}
