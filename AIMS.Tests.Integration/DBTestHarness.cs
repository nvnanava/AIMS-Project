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

    private async Task ResetDatabaseAsync()
    {
        using var con = new SqlConnection(ConnectionString);
        await con.OpenAsync();
        using var tx = con.BeginTransaction();

        // Delete in FK-safe order (children -> parents)
        await con.ExecuteAsync(@"
            DELETE FROM Assignments;
            DELETE FROM AuditLogs;
            DELETE FROM SoftwareAssets;
            DELETE FROM HardwareAssets;
            DELETE FROM Users;
            DELETE FROM Roles;
        ", transaction: tx);

        await tx.CommitAsync();
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
