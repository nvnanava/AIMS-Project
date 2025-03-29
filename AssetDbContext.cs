using Microsoft.EntityFrameworkCore;
using AssetTrackingSystem.Models;

namespace AssetTrackingSystem.Data
{
    public class AssetDbContext : DbContext
    {
        public AssetDbContext(DbContextOptions<AssetDbContext> options)
            : base(options)
        {
        }

        public DbSet<Asset> Assets { get; set; }
    }
}

