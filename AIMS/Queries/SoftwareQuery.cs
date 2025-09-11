using AIMS.Data;
using AIMS.Models;
using Microsoft.EntityFrameworkCore;

public class SoftwareQuery
{
    private readonly AimsDbContext _db;
    public SoftwareQuery(AimsDbContext db) => _db = db;

    public async Task<List<GetSoftwareDto>> GetAllSoftwareAsync()
    {
        // Example query, adjust as needed
        return await _db.SoftwareAssets
            .Select(s => new GetSoftwareDto
            {
                SoftwareID = s.SoftwareID,
                SoftwareName = s.SoftwareName,
                SoftwareType = s.SoftwareType,
                SoftwareVersion = s.SoftwareVersion,
                SoftwareLicenseKey = s.SoftwareLicenseKey,
                SoftwareLicenseExpiration = s.SoftwareLicenseExpiration,
                SoftwareUsageData = s.SoftwareUsageData,
                SoftwareCost = s.SoftwareCost
            })
            .ToListAsync();
    }
}



public class GetSoftwareDto
{

    public int SoftwareID { get; set; }

    // Columns
    public string SoftwareName { get; set; } = string.Empty;
    public string SoftwareType { get; set; } = string.Empty;
    public string SoftwareVersion { get; set; } = string.Empty;
    public string SoftwareLicenseKey { get; set; } = string.Empty; // unique
    public DateOnly? SoftwareLicenseExpiration { get; set; }
    public long SoftwareUsageData { get; set; }
    public decimal SoftwareCost { get; set; }
}
