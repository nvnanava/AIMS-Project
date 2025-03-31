using Microsoft.AspNetCore.Mvc.RazorPages;
using AssetTrackingSystem.Data;
using AssetTrackingSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace AssetTrackingSystem.Pages
{
    public class AssetListModel : PageModel
    {
        private readonly AssetDbContext _db;

        public AssetListModel(AssetDbContext db) => _db = db;

        public IList<Asset> Assets { get; set; } = new List<Asset>();

        public async Task OnGetAsync()
        {
            Assets = await _db.Assets.ToListAsync();
        }
    }
}

