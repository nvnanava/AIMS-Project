using System.ComponentModel.DataAnnotations;

namespace AIMS.Dtos.Hardware;

public class GetHardwareDto
{
    public int HardwareID { get; set; }
    public string AssetTag { get; set; } = string.Empty; // unique
    public string AssetName { get; set; } = string.Empty;
    public string AssetType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty; // unique
    public DateOnly WarrantyExpiration { get; set; }
    public DateOnly PurchaseDate { get; set; }
    public string Comment { get; set; } = string.Empty;

    public bool IsAssigned { get; set; }

    // derive effective status if Status is blank
    public string EffectiveStatus =>
        string.IsNullOrWhiteSpace(Status)
            ? (IsAssigned ? "Assigned" : "Available")
            : Status;
}

public class CreateHardwareDto
{
    [Required, MaxLength(16)]
    public string AssetTag { get; set; } = string.Empty;

    [MaxLength(128)]
    public string AssetName { get; set; } = string.Empty;

    [Required, MaxLength(32)]
    public string AssetType { get; set; } = string.Empty;

    [Required, MaxLength(32)]
    public string Status { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string Manufacturer { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string Model { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string SerialNumber { get; set; } = string.Empty;

    [Required]
    public DateOnly WarrantyExpiration { get; set; }

    [Required]
    public DateOnly PurchaseDate { get; set; }

    public string Comment { get; set; } = string.Empty;
}

public class UpdateHardwareDto
{
    [MaxLength(16)]
    public string? AssetTag { get; set; }
    public string? AssetName { get; set; }
    public string? AssetType { get; set; }
    public string? Status { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? Comment { get; set; }
    public string? SerialNumber { get; set; }
    public DateOnly? WarrantyExpiration { get; set; }
    public DateOnly? PurchaseDate { get; set; }
}

public sealed class BulkHardwareRequest
{
    public List<CreateHardwareDto> Dtos { get; set; } = new();
}
