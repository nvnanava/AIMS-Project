namespace AIMS.Dtos.Users;

public sealed class PersonDto
{
    public int UserID { get; set; }
    public string EmployeeNumber { get; set; } = "";
    public string Name { get; set; } = "";

    public int? OfficeID { get; set; }

    public bool IsArchived { get; set; } // this defaults to false

    public DateTime? ArchivedAtUtc { get; set; } //null when not archived
}
