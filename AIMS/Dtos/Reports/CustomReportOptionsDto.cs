namespace AIMS.Dtos.Reports;

public class CustomReportOptionsDto
{
    public bool seeHardware { get; set; } = true;
    public bool seeSoftware { get; set; } = true;
    public bool seeUsers { get; set; } = true;
    public bool seeOffice { get; set; } = true;
    public bool seeExpiration { get; set; } = false;
    public bool filterByMaintenance { get; set; } = false;
}
