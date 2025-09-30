using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIMS.Data;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Queries
{
    public class AssetQuery
    {
        private readonly AimsDbContext _db;
        public AssetQuery(AimsDbContext db) => _db = db;

        public async Task<List<GetAssetDto>> SearchAssetByName(string query)
        {
            query = (query ?? "").Trim();
            if (query.Length == 0)
            {
                return await GetFirstNAssets(20);
            }

            var q = query.ToLower();

            // Build base projections we can score
            var hardware = _db.HardwareAssets.AsNoTracking()
                .Select(h => new
                {
                    Name = h.AssetName ?? "",
                    Kind = "Hardware",
                    Tag = h.SerialNumber ?? ""
                });

            var software = _db.SoftwareAssets.AsNoTracking()
                .Select(s => new
                {
                    Name = s.SoftwareName ?? "",
                    Kind = "Software",
                    Tag = s.SoftwareLicenseKey ?? ""
                });

            // Combine, compute a relevance score, then sort
            // Scoring:
            //  +100 exact tag, +90 exact name
            //  +70 prefix tag, +60 prefix name
            //  +40 contains tag, +30 contains name
            var ranked = hardware.Concat(software)
                .Select(x => new
                {
                    x.Name,
                    x.Kind,
                    x.Tag,
                    Score =
                        (x.Tag.ToLower() == q ? 100 : 0) +
                        (x.Name.ToLower() == q ? 90 : 0) +
                        (x.Tag.ToLower().StartsWith(q) ? 70 : 0) +
                        (x.Name.ToLower().StartsWith(q) ? 60 : 0) +
                        (x.Tag.ToLower().Contains(q) ? 40 : 0) +
                        (x.Name.ToLower().Contains(q) ? 30 : 0)
                })
                // Filter out true non-matches (Score == 0) so we don’t return unrelated items
                .Where(r => r.Score > 0)
                .OrderByDescending(r => r.Score)
                .ThenBy(r => r.Name)
                .Take(50);

            var results = await ranked
                .Select(r => new GetAssetDto
                {
                    Name = r.Name,
                    Kind = r.Kind,
                    Tag = r.Tag
                })
                .ToListAsync();

            return results;
        }

        public async Task<List<GetAssetDto>> GetFirstNAssets(int n)
        {
            // Keep this simple: quick union for a generic “browse” experience
            var hardware = _db.HardwareAssets.AsNoTracking()
                .Select(h => new GetAssetDto
                {
                    Name = h.AssetName ?? "",
                    Kind = "Hardware",
                    Tag = h.SerialNumber ?? ""
                });

            var software = _db.SoftwareAssets.AsNoTracking()
                .Select(s => new GetAssetDto
                {
                    Name = s.SoftwareName ?? "",
                    Kind = "Software",
                    Tag = s.SoftwareLicenseKey ?? ""
                });

            return await hardware
                .Concat(software)
                .OrderBy(x => x.Name)
                .Take(n)
                .ToListAsync();
        }
        public async Task<List<string>> unique()
        {
            var hardware_query = _db.HardwareAssets
                .AsNoTracking()
                .Select(h => h.AssetType)
                .Distinct();
            var software_query = _db.SoftwareAssets
                .AsNoTracking()
                .Select(s => s.SoftwareType)
                .Distinct();
            return await hardware_query.Concat(software_query)
                    .Order()
                    .ToListAsync();
        }
    }
}