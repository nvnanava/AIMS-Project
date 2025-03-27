using System;
using System.ComponentModel.DataAnnotations;

namespace AssetTrackingSystem.Models
{
    public class Asset
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string Type { get; set; } = string.Empty;

        [Required]
        public string Status { get; set; } = string.Empty;

        [Required]
        public string Team { get; set; } = string.Empty;

        public string SerialNumber { get; set; } = string.Empty;

        [Required]
        public DateTime PurchaseDate { get; set; }

        public string Vendor { get; set; } = string.Empty;

        public string PoNumber { get; set; } = string.Empty;

        public DateTime? WarrantyExpiration { get; set; }

        public string Location { get; set; } = string.Empty;

        public bool IsAssigned { get; set; }

        public string EmployeeName { get; set; } = string.Empty;

        public string EmployeeId { get; set; } = string.Empty;

        public string Department { get; set; } = string.Empty;
    }
}

