namespace AIMS.ViewModels;

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
}
