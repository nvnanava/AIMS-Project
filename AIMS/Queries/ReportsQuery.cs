using AIMS.Data;
using AIMS.Dtos.Reports;
using AIMS.Models;
using AIMS.ViewModels;
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
                        DateCreated = r.DateCreated,
                        GeneratedByUserName = r.GeneratedByUser != null ? r.GeneratedByUser.FullName : "",
                        GeneratedByOfficeString = r.GeneratedForOffice != null ? r.GeneratedForOffice.OfficeName : "",
                    })
                    .ToListAsync(ct);
    }
    public async Task<ReportsVm?> GetReportASync(int id, CancellationToken ct = default)
    {
        return await _db.Reports
           .AsNoTracking()
           .Where(r => r.ReportID == id)
           .Select(r => new ReportsVm
           {
               ReportID = r.ReportID,
               Name = r.Name,
               Type = r.Type,
               Description = r.Description,
               DateCreated = r.DateCreated,
               GeneratedByUserName = r.GeneratedByUser != null ? r.GeneratedByUser.FullName : "",
               GeneratedByOfficeString = r.GeneratedForOffice != null ? r.GeneratedForOffice.OfficeName : "",
           })
           .FirstOrDefaultAsync(ct);
    }
    public async Task<DownloadReportDto?> GetReportForDownload(int id, CancellationToken ct = default)
    {
        return await _db.Reports
           .AsNoTracking()
           .Where(r => r.ReportID == id)
           .Select(r => new DownloadReportDto
           {
               ReportID = r.ReportID,
               Name = r.Name,
               Type = r.Type,
               DateCreated = r.DateCreated,
               GeneratedByUserName = r.GeneratedByUser != null ? r.GeneratedByUser.FullName : "",
               GeneratedByOfficeString = r.GeneratedForOffice != null ? r.GeneratedForOffice.OfficeName : "",
               Content = r.Content
           })
           .FirstOrDefaultAsync(ct);
    }

    public async Task<int> CreateReport(CreateReportDto dto, CancellationToken ct)
    {
        if (dto is null) throw new ArgumentNullException(nameof(dto));
        if (dto.GeneratedByUserID is null) throw new ArgumentException("GeneratedByUserID is required.");
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Name is required.");
        if (string.IsNullOrWhiteSpace(dto.Type)) throw new ArgumentException("Type is required.");

        var report = new Report
        {
            DateCreated = DateTime.UtcNow,
            GeneratedByUserID = dto.GeneratedByUserID,
            GeneratedForOfficeID = dto.GeneratedForOfficeID,
            Name = dto.Name,
            Type = dto.Type,
            Description = dto.Description,
            Content = dto.Content
        };

        _db.Reports.Add(report);
        await _db.SaveChangesAsync(ct);
        return report.ReportID;
    }
}
