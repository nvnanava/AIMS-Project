using System;
using System.Data;
using System.Linq;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AIMS.Tests.Integration;

public class SchemaTests : IClassFixture<DbTestHarness>
{
    private readonly DbTestHarness _harness;

    public SchemaTests(DbTestHarness harness)
    {
        _harness = harness;
    }

    [Fact]
    public void Connection_Opens()
    {
        using var con = new SqlConnection(_harness.ConnectionString);
        con.Open();
        var one = con.ExecuteScalar<int>("SELECT 1");
        one.Should().Be(1);
    }

    [Fact]
    public void PrimaryKeys_Exist()
    {
        using var con = new SqlConnection(_harness.ConnectionString);
        con.Open();

        var pkTables = con.Query<string>(@"
            SELECT t.name
            FROM sys.tables t
            JOIN sys.key_constraints kc ON kc.parent_object_id = t.object_id
            WHERE kc.type = 'PK'
        ").ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Current schema (old setup): no FeedbackEntries
        string[] expected = { "Roles", "Users", "HardwareAssets", "SoftwareAssets", "Assignments", "AuditLogs" };

        // Ensure each expected table has a PK (allowing for extra tables in DB)
        foreach (var tbl in expected)
            pkTables.Should().Contain(tbl, $"table '{tbl}' should have a primary key");
    }

    [Fact]
    public void UniqueIndexes_Exist()
    {
        using var con = new SqlConnection(_harness.ConnectionString);
        con.Open();

        // HardwareAssets.SerialNumber UNIQUE
        var hwSerialUnique = con.QuerySingle<int>(@"
            SELECT COUNT(*)
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            JOIN sys.tables t ON t.object_id = i.object_id
            WHERE i.is_unique = 1
              AND t.name = 'HardwareAssets'
              AND c.name = 'SerialNumber';
        ");
        hwSerialUnique.Should().BeGreaterThan(0, "Hardware.SerialNumber should be unique");

        // SoftwareAssets.SoftwareLicenseKey UNIQUE
        var swKeyUnique = con.QuerySingle<int>(@"
            SELECT COUNT(*)
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            JOIN sys.tables t ON t.object_id = i.object_id
            WHERE i.is_unique = 1
              AND t.name = 'SoftwareAssets'
              AND c.name = 'SoftwareLicenseKey';
        ");
        swKeyUnique.Should().BeGreaterThan(0, "Software.SoftwareLicenseKey should be unique");
    }

    [Fact]
    public void ForeignKeys_Exist_With_Correct_DeleteBehavior()
    {
        using var con = new SqlConnection(_harness.ConnectionString);
        con.Open();

        var fkProps = con.Query<(string fk, string parent, string referenced, string action)>(@"
            SELECT fk.name,
                   tp.name  AS parent_table,
                   tr.name  AS referenced_table,
                   fk.delete_referential_action_desc AS delete_action
            FROM sys.foreign_keys fk
            JOIN sys.tables tp ON tp.object_id = fk.parent_object_id
            JOIN sys.tables tr ON tr.object_id = fk.referenced_object_id
            WHERE tp.name IN ('Users','Assignments','AuditLogs')
               OR tr.name IN ('Roles','Users','HardwareAssets','SoftwareAssets');
        ").ToList();

        // Users(RoleID) -> Roles (Restrict/NoAction)
        fkProps.Any(x => x.parent.Equals("Users", StringComparison.OrdinalIgnoreCase)
                         && x.referenced.Equals("Roles", StringComparison.OrdinalIgnoreCase)
                         && x.action.Equals("NO_ACTION", StringComparison.OrdinalIgnoreCase))
              .Should().BeTrue("Users.RoleID FK should be Restrict/NoAction");

        // Users(SupervisorID) -> Users (Restrict/NoAction)
        fkProps.Any(x => x.parent.Equals("Users", StringComparison.OrdinalIgnoreCase)
                         && x.referenced.Equals("Users", StringComparison.OrdinalIgnoreCase)
                         && x.action.Equals("NO_ACTION", StringComparison.OrdinalIgnoreCase))
              .Should().BeTrue("Users.SupervisorID FK should be Restrict/NoAction");

        // Assignments(UserID) -> Users (Restrict/NoAction)
        fkProps.Any(x => x.parent.Equals("Assignments", StringComparison.OrdinalIgnoreCase)
                         && x.referenced.Equals("Users", StringComparison.OrdinalIgnoreCase)
                         && x.action.Equals("NO_ACTION", StringComparison.OrdinalIgnoreCase))
              .Should().BeTrue("Assignments.UserID FK should be Restrict/NoAction");

        // Assignments(AssetTag) -> HardwareAssets (Cascade)
        fkProps.Any(x => x.parent.Equals("Assignments", StringComparison.OrdinalIgnoreCase)
                         && x.referenced.Equals("HardwareAssets", StringComparison.OrdinalIgnoreCase)
                         && x.action.Equals("CASCADE", StringComparison.OrdinalIgnoreCase))
              .Should().BeTrue("Assignments.AssetTag FK should be Cascade");

        // Assignments(SoftwareID) -> SoftwareAssets (Cascade)
        fkProps.Any(x => x.parent.Equals("Assignments", StringComparison.OrdinalIgnoreCase)
                         && x.referenced.Equals("SoftwareAssets", StringComparison.OrdinalIgnoreCase)
                         && x.action.Equals("CASCADE", StringComparison.OrdinalIgnoreCase))
              .Should().BeTrue("Assignments.SoftwareID FK should be Cascade");

        // AuditLogs(UserID) -> Users (Restrict/NoAction)
        fkProps.Any(x => x.parent.Equals("AuditLogs", StringComparison.OrdinalIgnoreCase)
                         && x.referenced.Equals("Users", StringComparison.OrdinalIgnoreCase)
                         && x.action.Equals("NO_ACTION", StringComparison.OrdinalIgnoreCase))
              .Should().BeTrue("AuditLogs.UserID FK should be Restrict/NoAction");

        // AuditLogs(AssetTag) -> HardwareAssets (NoAction)
        fkProps.Any(x => x.parent.Equals("AuditLogs", StringComparison.OrdinalIgnoreCase)
                         && x.referenced.Equals("HardwareAssets", StringComparison.OrdinalIgnoreCase)
                         && x.action.Equals("NO_ACTION", StringComparison.OrdinalIgnoreCase))
              .Should().BeTrue("AuditLogs.AssetTag FK should be NoAction");

        // AuditLogs(SoftwareID) -> SoftwareAssets (NoAction)
        fkProps.Any(x => x.parent.Equals("AuditLogs", StringComparison.OrdinalIgnoreCase)
                         && x.referenced.Equals("SoftwareAssets", StringComparison.OrdinalIgnoreCase)
                         && x.action.Equals("NO_ACTION", StringComparison.OrdinalIgnoreCase))
              .Should().BeTrue("AuditLogs.SoftwareID FK should be NoAction");
    }

    [Fact]
    public void CheckConstraint_Exists_And_Enabled()
    {
        using var con = new SqlConnection(_harness.ConnectionString);
        con.Open();

        var enabled = con.QuerySingle<int>(@"
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM sys.check_constraints
                WHERE name = 'CK_Assignment_ExactlyOneAsset'
                  AND is_disabled = 0
            ) THEN 1 ELSE 0 END;
        ");
        enabled.Should().Be(1, "CK_Assignment_ExactlyOneAsset should exist and be enabled");
    }

    [Fact]
    public void SoftwareCost_Has_Decimal_10_2()
    {
        using var con = new SqlConnection(_harness.ConnectionString);
        con.Open();

        var result = con.QuerySingle<(int precision, int scale)>(@"
            SELECT
              numeric_precision AS precision,
              numeric_scale     AS scale
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = 'SoftwareAssets'
              AND COLUMN_NAME = 'SoftwareCost';
        ");

        result.precision.Should().Be(10);
        result.scale.Should().Be(2);
    }

    [Fact]
    public void Invalid_Assignment_BothAssets_Fails()
    {
        using var con = new SqlConnection(_harness.ConnectionString);
        con.Open();

        using var tx = con.BeginTransaction();

        // Arrange minimal graph
        var roleId = con.QuerySingle<int>(@"
            INSERT INTO Roles(RoleName, Description) VALUES (@n,@d);
            SELECT CAST(SCOPE_IDENTITY() AS int);",
            new { n = "Tester", d = "Temp role for test" }, tx);

        var userId = con.QuerySingle<int>(@"
            INSERT INTO Users(ExternalId, FullName, Email, EmployeeNumber, IsActive, RoleID)
            VALUES (@eid, @fn, @em, @emp, 1, @rid);
            SELECT CAST(SCOPE_IDENTITY() AS int);",
            new { eid = Guid.NewGuid(), fn = "Test User", em = "test@x.y", emp = "E000", rid = roleId }, tx);

        var hwId = con.QuerySingle<int>(@"
            INSERT INTO HardwareAssets(AssetName, AssetType, Status, Manufacturer, Model, SerialNumber, WarrantyExpiration, PurchaseDate)
            VALUES ('Laptop A','Laptop','InStock','Brand','ModelX',@sn, '2030-01-01','2025-01-01');
            SELECT CAST(SCOPE_IDENTITY() AS int);",
            new { sn = $"SN-{Guid.NewGuid():N}".Substring(0, 18) }, tx);

        var swId = con.QuerySingle<int>(@"
            INSERT INTO SoftwareAssets(SoftwareName, SoftwareType, SoftwareVersion, SoftwareLicenseKey, SoftwareUsageData, SoftwareCost, SoftwareLicenseExpiration)
            VALUES ('App','License','1.0',@key,0, 12.34, NULL);
            SELECT CAST(SCOPE_IDENTITY() AS int);",
            new { key = $"KEY-{Guid.NewGuid():N}" }, tx);

        // Act + Assert: CK must reject when both AssetTag and SoftwareID are set
        Action act = () => con.Execute(@"
            INSERT INTO Assignments(UserID, AssetKind, AssetTag, SoftwareID, AssignedAtUtc, UnassignedAtUtc)
            VALUES (@uid, 1, @hw, @sw, SYSUTCDATETIME(), NULL);
        ", new { uid = userId, hw = hwId, sw = swId }, tx);

        act.Should().Throw<SqlException>();

        tx.Rollback(); // keep DB clean
    }
}