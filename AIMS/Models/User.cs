using System.ComponentModel.DataAnnotations;

namespace AIMS.Models;

public class User
{
    public int UserID { get; set; }
    public Guid ExternalId { get; set; } = Guid.NewGuid();

    [Required, MaxLength(128)]
    public string FullName { get; set; } = string.Empty;

    [Required, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    public string? GraphObjectID { get; set; }

    [Required, MaxLength(32)]
    public string EmployeeNumber { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public int RoleID { get; set; }
    public Role Role { get; set; } = null!;

    public int? SupervisorID { get; set; }
    public User? Supervisor { get; set; }

    public int? OfficeID { get; set; }
    public Office? Office { get; set; }

    public bool IsArchived { get; set; } = false;

    public ICollection<User> DirectReports { get; set; } = new List<User>();

    public ICollection<AuditLog> AuditActions { get; set; } = new List<AuditLog>();
    public ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();
}
