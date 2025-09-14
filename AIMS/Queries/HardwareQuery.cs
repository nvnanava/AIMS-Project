using AIMS.Data;
using AIMS.Models;
using Microsoft.EntityFrameworkCore;

public class HardwareQuery
{
    private readonly AimsDbContext _db;
    public HardwareQuery(AimsDbContext db) => _db = db;

    public async Task<List<GetHardwareDto>> GetAllHardwareAsync()
    {
        // Example query, adjust as needed
        return await _db.HardwareAssets
            .Select(h => new GetHardwareDto
            {
                HardwareID = h.HardwareID,
                AssetName = h.AssetName,
                AssetType = h.AssetType,
                Status = h.Status,
                Manufacturer = h.Manufacturer,
                Model = h.Model,
                SerialNumber = h.SerialNumber,
                WarrantyExpiration = h.WarrantyExpiration,
                PurchaseDate = h.PurchaseDate
            })
            .ToListAsync();
    }
}



public class GetHardwareDto
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

public class CreateHardwareDto
{
    public string AssetName { get; set; } = string.Empty;
    public string AssetType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty; // unique
    public DateOnly WarrantyExpiration { get; set; }
    public DateOnly PurchaseDate { get; set; }
}

public class UpdateHardwareDto
{
    public string AssetName { get; set; } = string.Empty;
    public string AssetType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public DateOnly WarrantyExpiration { get; set; }
    public DateOnly PurchaseDate { get; set; }
}