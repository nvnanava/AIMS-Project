using AIMS.Data;
using AIMS.Dtos.Reports;
using AIMS.Models;
using AIMS.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Queries;

// Use Queries to abstract complex logic
public sealed class OfficeQuery
{
    private readonly AimsDbContext _db;
    public OfficeQuery(AimsDbContext db) => _db = db;


    // return a list of offices in the LocalDB; Shape: OfficeVm

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


    // allow query search on the offices table; Shape: OfficeVm
    public async Task<List<OfficeVm>> SearchOfficesAsync(string query, CancellationToken ct = default)
    {
        return await _db.Offices
                    .AsNoTracking()
                    // make sure to trim whitespace
                    .Where(o => o.OfficeName.Contains(query.Trim()))
                    .Select(o => new OfficeVm
                    {
                        OfficeID = o.OfficeID,
                        OfficeName = o.OfficeName,
                        Location = o.Location
                    })
                    .ToListAsync();
    }

    // create a new office in the local DB
    public async Task<int> AddOffice(string officeName, CancellationToken ct = default)
    {
        var newOffice = new Office
        {
            OfficeName = officeName
        };

        _db.Offices.Add(newOffice);
        await _db.SaveChangesAsync(ct);
        // return the ID of the new office so that callers can consume and use this value
        return newOffice.OfficeID;
    }
}