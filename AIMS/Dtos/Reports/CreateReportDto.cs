namespace AIMS.Dtos.Reports;

public class CreateReportDto
{

    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime DateCreated { get; set; } = DateTime.UtcNow;

    // Who/Where generated
    public int? GeneratedByUserID { get; set; }

    public int? GeneratedForOfficeID { get; set; }
    public byte[] Content { get; set; } = new byte[0];
}
