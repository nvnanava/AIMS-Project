namespace AIMS.ViewModels;

public class CustomReportDto
{
    public bool seeHardware { get; set; }
    public bool seeSoftware { get; set; }
    public bool seeUsers { get; set; }
    public bool seeOffice { get; set; }
    public bool seeExpiration { get; set; }
    public bool filterByMaintenance { get; set; }
}