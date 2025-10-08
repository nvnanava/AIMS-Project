using AIMS.Data;
using AIMS.Models;
using AIMS.ViewModels.SummaryCards;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Services;

public class SummaryCardService
{
    private readonly AimsDbContext _db;
    public SummaryCardService(AimsDbContext db) => _db = db;

    // New signature: optional type filter
    public async Task<List<SummaryCardDto>> GetSummaryAsync(IEnumerable<string>? types = null, CancellationToken ct = default)
    {
        // Normalize filter (case-insensitive)
        HashSet<string>? typeFilter = null;
        if (types is not null)
        {
            var list = types.Where(t => !string.IsNullOrWhiteSpace(t))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
            if (list.Count > 0)
                typeFilter = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
        }

        var thresholds = await _db.Thresholds.AsNoTracking()
            .ToDictionaryAsync(t => t.AssetType, t => t.ThresholdValue, ct);

        // Hardware
        var hwQ =
            from h in _db.HardwareAssets.AsNoTracking()
            join a in _db.Assignments.AsNoTracking()
                    .Where(a => a.AssetKind == AssetKind.Hardware && a.UnassignedAtUtc == null)
                on h.HardwareID equals a.HardwareID into gj
            from aa in gj.DefaultIfEmpty()
            let t = (h.AssetType ?? "Hardware")
            where typeFilter == null || typeFilter.Contains(t)
            group new { h, aa } by t into g
            select new { AssetType = g.Key, Total = g.Count(), Available = g.Count(x => x.aa == null) };

        var hw = await hwQ.ToListAsync(ct);

        // Software
        var swQ =
            from s in _db.SoftwareAssets.AsNoTracking()
            join a in _db.Assignments.AsNoTracking()
                    .Where(a => a.AssetKind == AssetKind.Software && a.UnassignedAtUtc == null)
                on s.SoftwareID equals a.SoftwareID into gj
            from aa in gj.DefaultIfEmpty()
            let t = (s.SoftwareType ?? "Software")
            where typeFilter == null || typeFilter.Contains(t)
            group new { s, aa } by t into g
            select new { AssetType = g.Key, Total = g.Count(), Available = g.Count(x => x.aa == null) };

        var sw = await swQ.ToListAsync(ct);

        // Merge + apply thresholds
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
                    IsLow = (threshold > 0) && (available < threshold),
                    AvailablePercent = total == 0 ? 0 : (int)Math.Round(100.0 * available / total)
                };
            })
            .OrderBy(x => x.AssetType)
            .ToList();

        return all;
    }
}
