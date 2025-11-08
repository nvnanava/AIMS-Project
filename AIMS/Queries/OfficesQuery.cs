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


    public async Task<List<OfficeVm>> SearchOfficesAsync(string query, CancellationToken ct = default)
    {
        return await _db.Offices
                    .AsNoTracking()
                    .Where(o => o.OfficeName.Contains(query.Trim()))
                    .Select(o => new OfficeVm
                    {
                        OfficeID = o.OfficeID,
                        OfficeName = o.OfficeName,
                        Location = o.Location
                    })
                    .ToListAsync();
    }

    public async Task<int> AddOffice(string officeName, CancellationToken ct = default)
    {
        var newOffice = new Office
        {
            OfficeName = officeName
        };

        _db.Offices.Add(newOffice);
        await _db.SaveChangesAsync(ct);
        return newOffice.OfficeID;
    }
}