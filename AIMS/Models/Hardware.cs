using System;
using System.ComponentModel.DataAnnotations;

namespace AIMS.Models;

public class Hardware
{
    // PK
    public int HardwareID { get; set; }

    // Human-facing tag (client standard): VARCHAR(16)
    [Required, MaxLength(16)]
    public string AssetTag { get; set; } = string.Empty; // unique

    [MaxLength(128)]
    public string AssetName { get; set; } = string.Empty;

    [Required, MaxLength(32)]
    public string AssetType { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Status { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string Manufacturer { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string Model { get; set; } = string.Empty;

    // Physical/device serial # (unique per hardware)
    [Required, MaxLength(128)]
    public string SerialNumber { get; set; } = string.Empty; // unique

    [Required]
    public DateOnly WarrantyExpiration { get; set; }

    [Required]
    public DateOnly PurchaseDate { get; set; }

    public string Comment { get; set; } = string.Empty;
}
