using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AIMS.Models;

public class Role
{
    public int RoleID { get; set; }

    [Required, MaxLength(64)]
    public string RoleName { get; set; } = string.Empty;

    [Required, MaxLength(256)]
    public string Description { get; set; } = string.Empty;

    public ICollection<User> Users { get; set; } = new List<User>();
}
