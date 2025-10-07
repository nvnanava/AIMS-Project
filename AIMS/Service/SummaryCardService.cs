using AIMS.Data;
using AIMS.Models;
using AIMS.ViewModels.SummaryCards;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Services;

public class SummaryCardService
{
    private readonly AimsDbContext _db;
    public SummaryCardService(AimsDbContext db) => _db = db;

    public async Task<List<SummaryCardDto>> GetSummaryAsync(CancellationToken ct = default)
    {
        // Load thresholds once
        var thresholds = await _db.Thresholds.AsNoTracking()
            .ToDictionaryAsync(t => t.AssetType, t => t.ThresholdValue, ct);

        // --- Hardware: group by AssetType, available = rows with no open assignment
        var hw = await (
            from h in _db.HardwareAssets.AsNoTracking()
            join a in _db.Assignments.AsNoTracking()
                     .Where(a => a.AssetKind == AssetKind.Hardware && a.UnassignedAtUtc == null)
                on h.HardwareID equals a.HardwareID into gj
            from aa in gj.DefaultIfEmpty()
            group new { h, aa } by (h.AssetType ?? "Hardware") into g
            select new
            {
                AssetType = g.Key,
                Total = g.Count(),
                Available = g.Count(x => x.aa == null) // no open assignment => available
            }
        ).ToListAsync(ct);

        // --- Software: group by SoftwareType, same logic
        var sw = await (
            from s in _db.SoftwareAssets.AsNoTracking()
            join a in _db.Assignments.AsNoTracking()
                     .Where(a => a.AssetKind == AssetKind.Software && a.UnassignedAtUtc == null)
                on s.SoftwareID equals a.SoftwareID into gj
            from aa in gj.DefaultIfEmpty()
            group new { s, aa } by (s.SoftwareType ?? "Software") into g
            select new
            {
                AssetType = g.Key,
                Total = g.Count(),
                Available = g.Count(x => x.aa == null)
            }
        ).ToListAsync(ct);

        // Union hardware + software by type and apply thresholds
        var all = hw.Concat(sw)
            .GroupBy(x => x.AssetType)
            .Select(g =>
            {
                var total = g.Sum(x => x.Total);
                var available = g.Sum(x => x.Available);
                thresholds.TryGetValue(g.Key, out var threshold);

                return new SummaryCardDto
                {
                    AssetType = g.Key,
                    Total = total,
                    Available = available,
                    Threshold = threshold,
                    IsLow = threshold > 0 && available < threshold,
                    AvailablePercent = total == 0 ? 0 : (int)Math.Round(100.0 * available / total)
                };
            })
            .OrderBy(x => x.AssetType)
            .ToList();

        return all;
    }
}
