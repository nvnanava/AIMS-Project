using AIMS.Data;
using AIMS.Dtos.Reports;
using AIMS.Models;
using AIMS.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Queries;

public sealed class OfficeQuery
{
    private readonly AimsDbContext _db;
    public OfficeQuery(AimsDbContext db) => _db = db;


    public async Task<List<OfficeVm>> GetAllOfficesAsync(CancellationToken ct = default)
    {
        return await _db.Offices
                    .AsNoTracking()
                    .Select(o => new OfficeVm
                    {
                        OfficeID = o.OfficeID,
                        OfficeName = o.OfficeName,
                        Location = o.Location
                    })
                    .ToListAsync(ct);
    }
}