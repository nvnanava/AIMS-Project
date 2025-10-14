using AIMS.Data;
using AIMS.Models;
using AIMS.ViewModels.SummaryCards;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace AIMS.Services;

public class SummaryCardService
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
        // Normalize the filter to a HashSet for fast Contains (case-insensitive)
        HashSet<string>? typeFilter = null;
        if (types is not null)
        {
            var list = types.Where(t => !string.IsNullOrWhiteSpace(t))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
            if (list.Count > 0)
                typeFilter = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
        }

        // Load thresholds and re-materialize with case-insensitive comparer
        var thresholdList = await _db.Thresholds.AsNoTracking()
            .Select(t => new { t.AssetType, t.ThresholdValue })
            .ToListAsync(ct);

        var thresholds = thresholdList
            .GroupBy(x => x.AssetType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().ThresholdValue, StringComparer.OrdinalIgnoreCase);

        // Hardware grouped by AssetType; “available” == no open assignment
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

        // Software grouped by SoftwareType
        var sw = await (
            from s in _db.SoftwareAssets.AsNoTracking()
            join a in _db.Assignments.AsNoTracking()
                     .Where(a => a.AssetKind == AssetKind.Software && a.UnassignedAtUtc == null)
                on s.SoftwareID equals a.SoftwareID into gj
            from aa in gj.DefaultIfEmpty()
            let type = (s.SoftwareType ?? "Software")
            where typeFilter == null || typeFilter.Contains(type)
            group new { s, aa } by type into g
            select new
            {
                AssetType = g.Key,
                Total = g.Count(),
                Available = g.Count(x => x.aa == null)
            }
        ).ToListAsync(ct);

        // Merge hardware + software by type and apply thresholds
        var all = hw.Concat(sw)
            .GroupBy(x => x.AssetType, StringComparer.OrdinalIgnoreCase)
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
                    AvailablePercent = (int)Math.Round(100.0 * available / Math.Max(total, 1))
                };
            })
            .OrderBy(x => x.AssetType, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return all;
    }

}
