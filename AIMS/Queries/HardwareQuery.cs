using AIMS.Data;
using AIMS.Models;
using AIMS.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Queries;

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
                AssetTag = h.AssetTag,
                AssetName = h.AssetName,
                AssetType = h.AssetType,
                Status = h.Status,
                Manufacturer = h.Manufacturer,
                Model = h.Model,
                SerialNumber = h.SerialNumber,
                WarrantyExpiration = h.WarrantyExpiration,
                PurchaseDate = h.PurchaseDate,

                // Is there an OPEN assignment? (keyed by HardwareID)
                IsAssigned = _db.Assignments.Any(a =>
                    a.AssetKind == AssetKind.Hardware &&
                    a.HardwareID == h.HardwareID &&
                    a.UnassignedAtUtc == null)
            })
            .ToListAsync(ct);
    }
}
