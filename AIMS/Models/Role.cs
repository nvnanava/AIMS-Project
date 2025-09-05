using System.Collections.Generic;

namespace AIMS.Models
{
    public class Role
    {
        public int RoleID { get; set; } // PK

        public string RoleName { get; set; } = string.Empty; // Required

        public string Description { get; set; } = string.Empty; // Required

        // Navigation property: One Role can be assigned to many Users
        public ICollection<User> Users { get; set; } = new List<User>();
    }
}