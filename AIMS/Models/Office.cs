using System.ComponentModel.DataAnnotations;

namespace AIMS.Models;

public class Office
{
    public int OfficeID { get; set; }

    [Required, MaxLength(128)]
    public string OfficeName { get; set; } = string.Empty;

    [MaxLength(256)]
    public string Location { get; set; } = string.Empty;

    public ICollection<User> Users { get; set; } = new List<User>();
}
