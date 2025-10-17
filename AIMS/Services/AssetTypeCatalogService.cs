using AIMS.Data;
using AIMS.ViewModels.Home;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Services;

public class AssetTypeCatalogService
{
    private readonly AimsDbContext _db;
    public AssetTypeCatalogService(AimsDbContext db) => _db = db;

    public async Task<List<AssetCardVm>> GetAllTypesAsync(CancellationToken ct = default)
    {
        // distinct, non-empty strings from the three places we care about
        var hw = await _db.HardwareAssets
            .AsNoTracking()
            .Select(h => h.AssetType)
            .Where(t => t != null && t != "")
            .ToListAsync(ct);

        var sw = await _db.SoftwareAssets
            .AsNoTracking()
            .Select(s => s.SoftwareType)
            .Where(t => t != null && t != "")
            .ToListAsync(ct);

        var th = await _db.Thresholds
            .AsNoTracking()
            .Select(t => t.AssetType)
            .Where(t => t != null && t != "")
            .ToListAsync(ct);

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in hw) set.Add(t!.Trim());
        foreach (var t in sw) set.Add(t!.Trim());
        foreach (var t in th) set.Add(t!.Trim());

        // Fallback to our original six if DB is empty
        if (set.Count == 0)
        {
            foreach (var t in new[] { "Monitor", "Laptop", "Desktop", "Software", "Headset", "Charging Cable" })
                set.Add(t);
        }

        // whitelist of known icon slugs (without "-icon.png")
        static string Slug(string type) => type.ToLowerInvariant().Replace(' ', '-');
        var known = new HashSet<string>(new[]
        {
                "monitor",
                "laptop",
                "desktop",
                "software",
                "headset",
                "charging-cable",
                "dock",
                "tablet"
            }, StringComparer.OrdinalIgnoreCase);

        // map a type name to an icon filename; unknown => blank icon
        string IconFor(string type)
        {
            var slug = Slug(type);
            return known.Contains(slug)
                ? $"/images/asset-icons/{slug}-icon.png"
                : "/images/asset-icons/blank-icon.png";
        }

        // build VMs (keep predictable order)
        return set
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Select(t => new AssetCardVm
            {
                AssetType = t,
                DisplayName = t.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? t : (t + (t == "Software" ? "" : "s")),
                IconUrl = IconFor(t),
                DetailsHref = $"/assets/{Uri.EscapeDataString(Slug(t))}?source=card"
            })
            .ToList();
    }
}
