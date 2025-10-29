namespace AIMS.Dtos.Reports;

public class DownloadReportDto
{
    public int ReportID { get; set; }
    public required string Name { get; set; }
    public required string Type { get; set; }
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    // Who/Where generated
    public string? GeneratedByUserName { get; set; }

    public string? GeneratedByOfficeString { get; set; }
    public byte[] Content { get; set; } = new byte[0];
}
