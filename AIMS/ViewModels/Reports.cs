namespace AIMS.ViewModels;

public class CustomReportOptionsDto
{
    public bool seeHardware { get; set; } = true;
    public bool seeSoftware { get; set; } = true;
    public bool seeUsers { get; set; } = true;
    public bool seeOffice { get; set; } = true;
    public bool seeExpiration { get; set; } = false;
    public bool filterByMaintenance { get; set; } = false;
}

public class ReportsVm
{
    public int ReportID { get; set; }
    public required string Name { get; set; }
    public required string Type { get; set; }
    public string? Description { get; set; }

    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    // Who/Where generated
    public string? GeneratedByUserName { get; set; }

    public string? GeneratedByOfficeString { get; set; }

    public string BlobUri { get; set; } = string.Empty;
}

public class CreateReportDto
{

    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime DateCreated { get; set; } = DateTime.UtcNow;

    // Who/Where generated
    public int? GeneratedByUserID { get; set; }

    public int? GeneratedByOfficeID { get; set; }

    // Output location
    public string BlobUri { get; set; } = string.Empty;
}