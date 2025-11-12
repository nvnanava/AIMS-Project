using AIMS.Data;
using AIMS.Dtos.Dashboard;
using AIMS.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace AIMS.Services;

public class SummaryCardService : ISummaryCardService
{
    private readonly AimsDbContext _db;
    private readonly IMemoryCache _cache;

    private CancellationTokenSource _summaryCts = new();

    private static readonly string CacheKeyBase = "summary:cards";

    public SummaryCardService(AimsDbContext db, IMemoryCache cache)
    {
        _db = db; _cache = cache;
    }

    // Invalidates the cached summary card data by replacing the cancellation token
    // and evicting the primary "summary:cards:all" cache entry.
    public void InvalidateSummaryCache()
    {
        var old = _summaryCts;
        _summaryCts = new CancellationTokenSource();
        _cache.Remove("summary:cards:all");
        old.Cancel();
        old.Dispose();
    }

    public async Task<List<SummaryCardDto>> GetSummaryAsync(IEnumerable<string>? types = null, CancellationToken ct = default)
    {
        var filterKey = NormalizeFilter(types);              // e.g., "all" or "laptop,monitor"
        var cacheKey = $"{CacheKeyBase}:{filterKey}";

        if (_cache.TryGetValue(cacheKey, out List<SummaryCardDto>? cached))
            return cached!;

        var data = await ComputeAsync(types, ct);

        var opts = new MemoryCacheEntryOptions()
            .AddExpirationToken(new CancellationChangeToken(_summaryCts.Token))
            .SetAbsoluteExpiration(TimeSpan.FromSeconds(15)); // optional short TTL

        _cache.Set(cacheKey, data, opts);
        return data;
    }

    // ------------ helpers ------------

    // Builds a stable cache key for a given filter set.
    private static string NormalizeFilter(IEnumerable<string>? types)
    {
        if (types is null) return "all";

        var arr = types
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return arr.Length == 0 ? "all" : string.Join(",", arr).ToLowerInvariant();
    }

    // The actual DB computation (group counts + thresholds).
    private async Task<List<SummaryCardDto>> ComputeAsync(IEnumerable<string>? types, CancellationToken ct)
    {
        // 1) Normalize filter -> HashSet for fast, case-insensitive Contains
        HashSet<string>? typeFilter = null;
        if (types is not null)
        {
            var list = types
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (list.Count > 0)
                typeFilter = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
        }

        // 2) Load thresholds once (case-insensitive)
        var thresholdList = await _db.Thresholds.AsNoTracking()
            .Select(t => new { t.AssetType, t.ThresholdValue })
            .ToListAsync(ct);

        var thresholds = thresholdList
            .GroupBy(x => x.AssetType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Last().ThresholdValue,
                StringComparer.OrdinalIgnoreCase
            );

        // 3) HARDWARE: per type totals and "available" (no open assignment)
        var hw = await (
            from h in _db.HardwareAssets.AsNoTracking()
            join a in _db.Assignments.AsNoTracking()
                     .Where(a => a.AssetKind == AssetKind.Hardware && a.UnassignedAtUtc == null)
                on h.HardwareID equals a.HardwareID into gj
            from aa in gj.DefaultIfEmpty()
            let type = (h.AssetType ?? "Hardware")
            where typeFilter == null || typeFilter.Contains(type)
            group new { h, aa } by type into g
            select new
            {
                AssetType = g.Key,
                Total = g.Count(),
                Available = g.Count(x => x.aa == null)
            }
        ).ToListAsync(ct);

        // 4) SOFTWARE: each SoftwareID counts as one "asset".
        //    It's "available" iff open-assigned seats < capacity.
        var swPerAsset = await _db.SoftwareAssets
            .AsNoTracking()
            .Where(s => typeFilter == null || typeFilter.Contains((s.SoftwareType ?? "Software").Trim()))
            .Select(s => new
            {
                AssetType = (s.SoftwareType ?? "Software").Trim(),
                // treat 0-capacity as 1 to avoid divide-by-zero / always-full edge case
                SeatCap = (s.LicenseTotalSeats == 0 ? 1 : s.LicenseTotalSeats),
                OpenAssigned = _db.Assignments
                    .AsNoTracking()
                    .Count(a => a.AssetKind == AssetKind.Software
                                && a.SoftwareID == s.SoftwareID
                                && a.UnassignedAtUtc == null)
            })
            .Select(x => new
            {
                x.AssetType,
                Total = 1,
                Available = (x.OpenAssigned < x.SeatCap) ? 1 : 0
            })
            .ToListAsync(ct);

        // Roll up software per type (done in-memory to avoid tricky EF translations)
        var sw = swPerAsset
            .GroupBy(x => x.AssetType, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                AssetType = g.Key,
                Total = g.Sum(x => x.Total),
                Available = g.Sum(x => x.Available)
            })
            .ToList();

        // 5) MERGE + APPLY THRESHOLDS
        var merged = hw
            .Concat(sw) // both sides share the same anonymous shape: { AssetType, Total, Available }
            .GroupBy(x => x.AssetType, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var assetType = g.Key;
                var total = g.Sum(x => x.Total);
                var available = g.Sum(x => x.Available);

                thresholds.TryGetValue(assetType, out var threshold);
                threshold = Math.Max(0, threshold);

                var isSoftwareType = assetType.Equals("Software", StringComparison.OrdinalIgnoreCase);

                // For Software, we'll interpret the threshold as "% of assets fully consumed"
                // where "used assets" = total - available (i.e., at capacity).
                var usedAssets = Math.Max(0, total - available);
                var usedPctAssets = (int)Math.Round(100.0 * usedAssets / Math.Max(total, 1));

                var isLow = threshold > 0
                    ? (isSoftwareType
                        ? usedPctAssets >= threshold   // Software: % fully-consumed assets vs threshold
                        : available < threshold)       // Hardware: available count vs threshold
                    : false;

                return new SummaryCardDto
                {
                    AssetType = assetType,
                    Total = total,
                    Available = available,
                    Threshold = threshold,
                    IsLow = isLow,
                    // Display % available across both hardware/software consistently
                    AvailablePercent = (int)Math.Round(100.0 * available / Math.Max(total, 1))
                };
            })
            .OrderBy(x => x.AssetType, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return merged;
    }

}
