using AIMS.Data;
using AIMS.Models;
using Microsoft.EntityFrameworkCore;

public class HardwareQuery
{
    private readonly AimsDbContext _db;
    public HardwareQuery(AimsDbContext db) => _db = db;

    public async Task<List<GetHardwareDto>> GetAllHardwareAsync(CancellationToken ct = default)
    {
        return await _db.HardwareAssets
            .AsNoTracking()
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
                PurchaseDate = h.PurchaseDate,

                // Is there an OPEN assignment?
                IsAssigned = _db.Assignments.Any(a =>
                    a.AssetKind == AssetKind.Hardware &&
                    a.AssetTag == h.HardwareID &&
                    a.UnassignedAtUtc == null)
            })
            .ToListAsync(ct);
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

    public bool IsAssigned { get; set; }

    // derive effective status if Status is blank
    public string EffectiveStatus =>
        string.IsNullOrWhiteSpace(Status)
            ? (IsAssigned ? "Assigned" : "Available")
            : Status;
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
