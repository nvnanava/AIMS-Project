using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AIMS.Tests.Integration;

public sealed class DbTestHarness : IAsyncLifetime
{
    public string ConnectionString { get; }

    public bool AutoDelete { get; set; } = true;
    public IDbConnection OpenConnection() => new SqlConnection(ConnectionString);

    public DbTestHarness()
    {
        // Load test config (appsettings.json in AIMS.Tests.Integration)
        var cfg = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        ConnectionString = cfg.GetConnectionString("DockerConnection")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:DockerConnection in test settings.");
    }

    public async Task InitializeAsync()
    {
        // Make sure schema exists and migrations are applied
        await using var ctx = MigrateDb.CreateContext(ConnectionString);
        await ctx.Database.EnsureCreatedAsync(); // no-op if already created
        await ctx.Database.MigrateAsync();       // apply latest migration

        // Start each test class from a known-clean state
        await ResetDatabaseAsync();

        // Seed minimal baseline data needed by tests
        await SeedBaselineAsync();
    }

    public async Task DisposeAsync()
    {
        if (AutoDelete)
            await ResetDatabaseAsync();
    }

    // ---------- FIXED: delete children -> parents in FK-safe order ----------
    private async Task ResetDatabaseAsync()
    {
        using var con = new SqlConnection(ConnectionString);
        await con.OpenAsync();

        // Keep the whole wipe atomic and fail-fast on errors
        var sql = @"
SET XACT_ABORT ON;
BEGIN TRAN;

-- Pure children first
IF OBJECT_ID('dbo.AuditLogChanges','U') IS NOT NULL DELETE FROM dbo.AuditLogChanges;   -- FK -> AuditLogs
IF OBJECT_ID('dbo.AuditLogs','U')        IS NOT NULL DELETE FROM dbo.AuditLogs;        -- may FK -> Users, HW, SW
IF OBJECT_ID('dbo.Agreements','U')       IS NOT NULL DELETE FROM dbo.Agreements;       -- FK -> HardwareAssets / SoftwareAssets
IF OBJECT_ID('dbo.Assignments','U')      IS NOT NULL DELETE FROM dbo.Assignments;      -- FK -> Users/HardwareAssets/SoftwareAssets
IF OBJECT_ID('dbo.Reports','U')          IS NOT NULL DELETE FROM dbo.Reports;          -- FK -> Users/Offices

-- Mid-level / referenced by children
IF OBJECT_ID('dbo.HardwareAssets','U')   IS NOT NULL DELETE FROM dbo.HardwareAssets;
IF OBJECT_ID('dbo.SoftwareAssets','U')   IS NOT NULL DELETE FROM dbo.SoftwareAssets;
IF OBJECT_ID('dbo.Thresholds','U')       IS NOT NULL DELETE FROM dbo.Thresholds;

-- Lookups / owners last
IF OBJECT_ID('dbo.Offices','U')          IS NOT NULL DELETE FROM dbo.Offices;
IF OBJECT_ID('dbo.Users','U')            IS NOT NULL DELETE FROM dbo.Users;
IF OBJECT_ID('dbo.Roles','U')            IS NOT NULL DELETE FROM dbo.Roles;

COMMIT;

-- Optional: reseed identities for deterministic tests (safe if table exists + is identity)
IF OBJECT_ID('dbo.Roles','U')            IS NOT NULL DBCC CHECKIDENT('dbo.Roles', RESEED, 0);
IF OBJECT_ID('dbo.Users','U')            IS NOT NULL DBCC CHECKIDENT('dbo.Users', RESEED, 0);
IF OBJECT_ID('dbo.Offices','U')          IS NOT NULL DBCC CHECKIDENT('dbo.Offices', RESEED, 0);
IF OBJECT_ID('dbo.Thresholds','U')       IS NOT NULL DBCC CHECKIDENT('dbo.Thresholds', RESEED, 0);
IF OBJECT_ID('dbo.HardwareAssets','U')   IS NOT NULL DBCC CHECKIDENT('dbo.HardwareAssets', RESEED, 0);
IF OBJECT_ID('dbo.SoftwareAssets','U')   IS NOT NULL DBCC CHECKIDENT('dbo.SoftwareAssets', RESEED, 0);
IF OBJECT_ID('dbo.Assignments','U')      IS NOT NULL DBCC CHECKIDENT('dbo.Assignments', RESEED, 0);
IF OBJECT_ID('dbo.Reports','U')          IS NOT NULL DBCC CHECKIDENT('dbo.Reports', RESEED, 0);
IF OBJECT_ID('dbo.AuditLogs','U')        IS NOT NULL DBCC CHECKIDENT('dbo.AuditLogs', RESEED, 0);
IF OBJECT_ID('dbo.AuditLogChanges','U')  IS NOT NULL DBCC CHECKIDENT('dbo.AuditLogChanges', RESEED, 0);
";
        await con.ExecuteAsync(sql);
    }

    private static string NewSerial() =>
        $"SN-{Guid.NewGuid():N}".Substring(0, 18);

    private static string NewTag(string prefix = "TST") =>
        // keep within VARCHAR(16)
        $"{prefix}-{Guid.NewGuid():N}".Substring(0, 16).ToUpperInvariant();

    private static string NewSwKey() =>
        $"KEY-{Guid.NewGuid():N}".ToUpperInvariant();

    private async Task SeedBaselineAsync()
    {
        using var con = new SqlConnection(ConnectionString);
        await con.OpenAsync();
        using var tx = con.BeginTransaction();

        // Minimal roles so tests can create Users (RoleID is NOT NULL)
        await con.ExecuteAsync(@"
            INSERT INTO Roles (RoleName, Description) VALUES
            (N'Employee', N'Default employee role'),
            (N'Admin',    N'Administrator role');
        ", transaction: tx);

        // Seed baseline hardware rows — include AssetTag (NOT NULL)
        var types = new[] { "Charging Cable", "Desktop", "Headset", "Laptop", "Monitor" };
        foreach (var t in types)
        {
            await con.ExecuteAsync(@"
                INSERT INTO HardwareAssets
                (AssetName, AssetType, AssetTag, Status, Manufacturer, Model, SerialNumber, WarrantyExpiration, PurchaseDate, Comment)
                VALUES
                (@name, @type, @tag, 'Available', 'Brand', 'ModelX', @sn, '2030-01-01', '2025-01-01', N'Baseline seed');
            ",
            new
            {
                name = $"{t} A",
                type = t,
                tag = NewTag(t switch
                {
                    "Charging Cable" => "CAB",
                    "Desktop" => "DTP",
                    "Headset" => "HDS",
                    "Laptop" => "LTP",
                    "Monitor" => "MON",
                    _ => "TST"
                }),
                sn = NewSerial()
            }, tx);
        }

        // Seed one software row so 'Software' exists
        await con.ExecuteAsync(@"
            INSERT INTO SoftwareAssets
            (SoftwareName, SoftwareType, SoftwareVersion, SoftwareLicenseKey, SoftwareUsageData, SoftwareCost, SoftwareLicenseExpiration, Comment)
            VALUES
            (N'App A', N'Software', N'1.0', @key, 0, 12.34, NULL, N'Baseline seed');
        ", new { key = NewSwKey() }, tx);

        await tx.CommitAsync();
    }
}
