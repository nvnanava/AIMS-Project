using AIMS.Data;
using AIMS.Dtos.Software;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Queries;

public class SoftwareQuery
{
    private readonly AimsDbContext _db;
    public SoftwareQuery(AimsDbContext db) => _db = db;

    public async Task<List<GetSoftwareDto>> GetAllSoftwareAsync(CancellationToken ct = default)
    {
        return await _db.SoftwareAssets
            .AsNoTracking()
            .Select(s => new GetSoftwareDto
            {
                SoftwareID = s.SoftwareID,
                SoftwareName = s.SoftwareName,
                SoftwareType = s.SoftwareType,
                SoftwareVersion = s.SoftwareVersion,
                SoftwareLicenseKey = s.SoftwareLicenseKey,
                SoftwareLicenseExpiration = s.SoftwareLicenseExpiration,
                SoftwareUsageData = s.SoftwareUsageData,
                SoftwareCost = s.SoftwareCost,
                LicenseTotalSeats = s.LicenseTotalSeats,
                LicenseSeatsUsed = s.LicenseSeatsUsed,
                Comment = s.Comment
            })
            .ToListAsync(ct);
    }
}
