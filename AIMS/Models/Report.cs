using System;

namespace AIMS.Models;

public class Report
{
    public int ReportID { get; set; }
    public Guid ExternalId { get; set; }                 // deterministic external handle

    public required string Name { get; set; }
    public required string Type { get; set; }            // keep as string per your model
    public string? Description { get; set; }

    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
}