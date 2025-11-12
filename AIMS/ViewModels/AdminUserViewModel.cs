using System.ComponentModel.DataAnnotations;

namespace AIMS.ViewModels
{
    public class AdminUserViewModel
    {
        public int UserID { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        [Required]
        public string GraphObjectID { get; set; } = string.Empty;
        public int RoleID { get; set; }
        public string RoleName { get; set; } = string.Empty;
        public int? SupervisorID { get; set; }
    }
}
