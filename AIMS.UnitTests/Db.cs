namespace AIMS.UnitTests;

using System.Threading.Tasks;
using AIMS.Data;
using AIMS.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

public static class Db
{
    // Make sure the native e_sqlite3 library is loaded on macOS/Linux/Windows
    static Db()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    public static async Task<(AimsDbContext db, SqliteConnection conn)> CreateContextAsync()
    {
        // Keep the connection open for the lifetime of the context so the in-memory DB persists
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var options = new DbContextOptionsBuilder<AimsDbContext>()
            .UseSqlite(conn)
            .EnableSensitiveDataLogging()
            .Options;

        var db = new AimsDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return (db, conn);
    }

    public static async Task SeedAsync(AimsDbContext db)
    {
        db.HardwareAssets.AddRange(
            new Hardware { AssetName = "ThinkPad T14", AssetType = "Laptop", SerialNumber = "SN-AAA-001" },
            new Hardware { AssetName = "MacBook Pro", AssetType = "Laptop", SerialNumber = "MBP-XYZ-999" },
            new Hardware { AssetName = "Dell U2720Q", AssetType = "Monitor", SerialNumber = "MON-123456" }
        );

        db.SoftwareAssets.AddRange(
            new Software { SoftwareName = "Visual Studio", SoftwareType = "IDE", SoftwareLicenseKey = "VS-123-KEY" },
            new Software { SoftwareName = "Slack", SoftwareType = "Collaboration", SoftwareLicenseKey = "SLK-KEY-777" },
            new Software { SoftwareName = "Adobe Photoshop", SoftwareType = "Graphics", SoftwareLicenseKey = "PH-ABC-123" },
            new Software { SoftwareName = "thinktool", SoftwareType = "Utility", SoftwareLicenseKey = "SN-AAA-001" } // reuse key to test scoring
        );

        await db.SaveChangesAsync();
    }
}
