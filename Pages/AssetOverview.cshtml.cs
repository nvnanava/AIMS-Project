using Microsoft.AspNetCore.Mvc.RazorPages;
using AssetTrackingSystem.Data;
using AssetTrackingSystem.Models;
using Microsoft.AspNetCore.Mvc;

namespace AssetTrackingSystem.Pages
{
    public class AssetOverviewModel : PageModel
    {
        private readonly AssetDbContext _dbContext;

        public AssetOverviewModel(AssetDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        // Properties to display asset details.
        public string AssetName { get; set; }
        public string AssetType { get; set; }
        public string AssetStatus { get; set; }
        public string Team { get; set; }

        // The OnGet method now accepts an asset ID from the route.
        public void OnGet(int id)
        {
            // Retrieve the asset with the specified ID.
            Asset asset = _dbContext.Assets.Find(id);
            if (asset != null)
            {
                AssetName = asset.Name;
                AssetType = asset.Type;
                AssetStatus = asset.Status;
                Team = asset.Team;
            }
            else
            {
                // Handle case where asset is not found.
                AssetName = "Asset Not Found";
                AssetType = string.Empty;
                AssetStatus = string.Empty;
                Team = string.Empty;
            }
        }
    }
}

