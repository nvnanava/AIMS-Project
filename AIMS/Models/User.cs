using System.Collections.Generic;

namespace AIMS.Models;

public class User
{
    // PK
    public int UserID { get; set; }
    public Guid ExternalId { get; set; }

    // Columns
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? GraphObjectID { get; set; }     // MS Graph object id
    public string EmployeeNumber { get; set; } = string.Empty; // e.g. "28809"
    public bool IsActive { get; set; } = true;

    // FKs
    public int RoleID { get; set; }
    public int? SupervisorID { get; set; }

    // Navigation
    public Role Role { get; set; } = null!;
    public User? Supervisor { get; set; }
    public ICollection<User> DirectReports { get; set; } = new List<User>();

    public ICollection<Feedback> FeedbackSubmissions { get; set; } = new List<Feedback>();
    public ICollection<AuditLog> AuditActions { get; set; } = new List<AuditLog>();
    public ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();
}
