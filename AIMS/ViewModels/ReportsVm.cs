namespace AIMS.ViewModels;

public class ReportsVm
{
    public int ReportID { get; set; }
    public required string Name { get; set; }
    public required string Type { get; set; }
    public string? Description { get; set; }

    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    // Who/Where generated
    public int? GeneratedByUserID { get; set; }
    public string? GeneratedByUserName { get; set; }

    public int? GeneratedByOfficeID { get; set; }
    public string? GeneratedByOfficeString { get; set; }

    public string BlobUri { get; set; } = string.Empty;
}