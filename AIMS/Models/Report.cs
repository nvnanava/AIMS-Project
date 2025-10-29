using System.ComponentModel.DataAnnotations;

namespace AIMS.Models;

public class Report
{
    public int ReportID { get; set; }
    public Guid ExternalId { get; set; } = Guid.NewGuid();

    [Required, MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string Type { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime DateCreated { get; set; } = DateTime.UtcNow;

    // Who/Where generated
    public int? GeneratedByUserID { get; set; }
    public User? GeneratedByUser { get; set; }

    public int? GeneratedForOfficeID { get; set; }
    public Office? GeneratedForOffice { get; set; }

    // Content
    [Required]
    public byte[] Content { get; set; } = new byte[0];
}
