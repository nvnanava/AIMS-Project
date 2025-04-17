using System;

using System.Collections.Generic;

namespace AIMS.Models;

public partial class Hardware
{
    public int AssetTag { get; set; }

    public string AssetName { get; set; } = null!;

    public string AssetType { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string Manufacturer { get; set; } = null!;

    public string Model { get; set; } = null!;

    public string SerialNumber { get; set; } = null!;

    public DateOnly WarrantyExpiration { get; set; }

    public DateOnly PurchaseDate { get; set; }
}
