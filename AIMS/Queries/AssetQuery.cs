// Combined Hardware and Software Query

using AIMS.Data;
using AIMS.Models;
using Microsoft.EntityFrameworkCore;

public class AssetQuery
{
    private readonly AimsDbContext _db;
    public AssetQuery(AimsDbContext db) => _db = db;

    public async Task<List<GetAssetDto>> SearchAssetByName(String query)
    {

        // search the hardware table
        var hardware = _db.HardwareAssets
        .Where(h => h.AssetName.Contains(query))
        .Select(h => new GetAssetDto
        {
            AssetKind = 1,
            AssetName = h.AssetName,
            AssetID = h.HardwareID
        });

        // search the software table
        var software = _db.SoftwareAssets
                .Where(s => s.SoftwareName.Contains(query))
                .Select(s => new GetAssetDto
                {
                    AssetKind = 2,
                    AssetName = s.SoftwareName,
                    AssetID = s.SoftwareID
                });

        // use LINQ to concat 
        var combined = hardware.Union(software);

        return await combined.ToListAsync();

    }
    public async Task<List<GetAssetDto>> GetFirstNAssets(int n)
    {
        // search the hardware table
        var hardware = _db.HardwareAssets
        .Select(h => new GetAssetDto
        {
            AssetKind = 1,
            AssetName = h.AssetName,
            AssetID = h.HardwareID
        }).Take(n);

        // search the software table
        var software = _db.SoftwareAssets
                .Select(s => new GetAssetDto
                {
                    AssetKind = 2,
                    AssetName = s.SoftwareName,
                    AssetID = s.SoftwareID
                }).Take(n);

        // use LINQ to concat 
        var combined = hardware.Union(software);

        return await combined.ToListAsync();
    }
}

public class GetAssetDto
{
    public int AssetKind { get; set; }
    public string AssetName { get; set; } = string.Empty;

    public int AssetID { get; set; }
}