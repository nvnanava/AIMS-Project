using System;

namespace AIMS.Models;

public class Hardware
{
    // PK
    public int HardwareID { get; set; }

    // Columns
    public string AssetName { get; set; } = string.Empty;
    public string AssetType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty; // unique
    public DateOnly WarrantyExpiration { get; set; }
    public DateOnly PurchaseDate { get; set; }
}
