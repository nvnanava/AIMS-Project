using System.Runtime.CompilerServices;
using AIMS.Data;
using AIMS.ViewModels;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Queries;

public enum ReportType { Assignment = 1, Office = 2, Custom = 3 };
public sealed class ReportsQuery
{
    private readonly AimsDbContext _db;
    public ReportsQuery(AimsDbContext db) => _db = db;


    public async Task<List<ReportsVm>> GetAllReportsAsync(CancellationToken ct = default)
    {
        return await _db.Reports
                    .AsNoTracking()
                    .Select(r => new ReportsVm
                    {
                        ReportID = r.ReportID,
                        Name = r.Name,
                        Type = r.Type,
                        Description = r.Description,
                        DateCreated = r.DateCreated
                    })
                    .ToListAsync(ct);
    }
}