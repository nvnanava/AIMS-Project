using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;

namespace AIMS.Tests.Integration
{
    public class SchemaTests : IClassFixture<DbTestHarness>
    {
        private readonly DbTestHarness _harness;

        public SchemaTests(DbTestHarness harness)
        {
            _harness = harness;
        }

        // --- Small helper for valid 16-char hardware tags ---
        private static string NewTag16(string prefix = "T")
            => (prefix + Guid.NewGuid().ToString("N"))
                .ToUpperInvariant()
                .Substring(0, 16);

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

            string[] expected = { "Roles", "Users", "HardwareAssets", "SoftwareAssets", "Assignments", "AuditLogs" };

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
                JOIN sys.tables  t ON t.object_id = i.object_id
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
                JOIN sys.tables  t ON t.object_id = i.object_id
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

            // Return FK properties with actual column names
            var fkProps = con.Query<(string fk, string parent, string parentCol, string referenced, string referencedCol, string action)>(@"
                SELECT 
                  fk.name                                        AS fk,
                  tp.name                                        AS parent_table,
                  cp.name                                        AS parent_column,
                  tr.name                                        AS referenced_table,
                  cr.name                                        AS referenced_column,
                  fk.delete_referential_action_desc              AS delete_action
                FROM sys.foreign_keys fk
                JOIN sys.tables tp ON tp.object_id = fk.parent_object_id
                JOIN sys.tables tr ON tr.object_id = fk.referenced_object_id
                JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
                JOIN sys.columns cp ON cp.object_id = tp.object_id AND cp.column_id = fkc.parent_column_id
                JOIN sys.columns cr ON cr.object_id = tr.object_id AND cr.column_id = fkc.referenced_column_id
                WHERE tp.name IN ('Users','Assignments','AuditLogs')
                   OR tr.name IN ('Roles','Users','HardwareAssets','SoftwareAssets');
            ").ToList();

            // Users(RoleID) -> Roles (Restrict/NoAction)
            fkProps.Any(x => x.parent.Equals("Users", StringComparison.OrdinalIgnoreCase)
                             && x.parentCol.Equals("RoleID", StringComparison.OrdinalIgnoreCase)
                             && x.referenced.Equals("Roles", StringComparison.OrdinalIgnoreCase)
                             && x.action.Equals("NO_ACTION", StringComparison.OrdinalIgnoreCase))
                  .Should().BeTrue("Users.RoleID FK should be Restrict/NoAction");

            // Users(SupervisorID) -> Users (Restrict/NoAction)
            fkProps.Any(x => x.parent.Equals("Users", StringComparison.OrdinalIgnoreCase)
                             && x.parentCol.Equals("SupervisorID", StringComparison.OrdinalIgnoreCase)
                             && x.referenced.Equals("Users", StringComparison.OrdinalIgnoreCase)
                             && x.action.Equals("NO_ACTION", StringComparison.OrdinalIgnoreCase))
                  .Should().BeTrue("Users.SupervisorID FK should be Restrict/NoAction");

            // Assignments(UserID) -> Users (Restrict/NoAction)
            fkProps.Any(x => x.parent.Equals("Assignments", StringComparison.OrdinalIgnoreCase)
                             && x.parentCol.Equals("UserID", StringComparison.OrdinalIgnoreCase)
                             && x.referenced.Equals("Users", StringComparison.OrdinalIgnoreCase)
                             && x.action.Equals("NO_ACTION", StringComparison.OrdinalIgnoreCase))
                  .Should().BeTrue("Assignments.UserID FK should be Restrict/NoAction");

            // Assignments(HardwareID) -> HardwareAssets (Cascade)
            fkProps.Any(x => x.parent.Equals("Assignments", StringComparison.OrdinalIgnoreCase)
                             && x.parentCol.Equals("HardwareID", StringComparison.OrdinalIgnoreCase)
                             && x.referenced.Equals("HardwareAssets", StringComparison.OrdinalIgnoreCase)
                             && x.action.Equals("CASCADE", StringComparison.OrdinalIgnoreCase))
                  .Should().BeTrue("Assignments.HardwareID FK should be Cascade");

            // Assignments(SoftwareID) -> SoftwareAssets (Cascade)
            fkProps.Any(x => x.parent.Equals("Assignments", StringComparison.OrdinalIgnoreCase)
                             && x.parentCol.Equals("SoftwareID", StringComparison.OrdinalIgnoreCase)
                             && x.referenced.Equals("SoftwareAssets", StringComparison.OrdinalIgnoreCase)
                             && x.action.Equals("CASCADE", StringComparison.OrdinalIgnoreCase))
                  .Should().BeTrue("Assignments.SoftwareID FK should be Cascade");

            // AuditLogs(UserID) -> Users (Restrict/NoAction)
            fkProps.Any(x => x.parent.Equals("AuditLogs", StringComparison.OrdinalIgnoreCase)
                             && x.parentCol.Equals("UserID", StringComparison.OrdinalIgnoreCase)
                             && x.referenced.Equals("Users", StringComparison.OrdinalIgnoreCase)
                             && x.action.Equals("NO_ACTION", StringComparison.OrdinalIgnoreCase))
                  .Should().BeTrue("AuditLogs.UserID FK should be Restrict/NoAction");

            // AuditLogs(HardwareID) -> HardwareAssets (NoAction)
            fkProps.Any(x => x.parent.Equals("AuditLogs", StringComparison.OrdinalIgnoreCase)
                             && x.parentCol.Equals("HardwareID", StringComparison.OrdinalIgnoreCase)
                             && x.referenced.Equals("HardwareAssets", StringComparison.OrdinalIgnoreCase)
                             && x.action.Equals("NO_ACTION", StringComparison.OrdinalIgnoreCase))
                  .Should().BeTrue("AuditLogs.HardwareID FK should be NoAction");

            // AuditLogs(SoftwareID) -> SoftwareAssets (NoAction)
            fkProps.Any(x => x.parent.Equals("AuditLogs", StringComparison.OrdinalIgnoreCase)
                             && x.parentCol.Equals("SoftwareID", StringComparison.OrdinalIgnoreCase)
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

        // ---------------------------
        // NEW: Hardware.AssetTag rules
        // ---------------------------
        [Fact]
        public void Hardware_AssetTag_NotNull_And_MaxLen16()
        {
            using var con = new SqlConnection(_harness.ConnectionString);
            con.Open();

            // Column meta: NOT NULL + max length 16
            var meta = con.QuerySingle<(string isNullable, int maxLen)>(@"
                SELECT
                  IS_NULLABLE,
                  CHARACTER_MAXIMUM_LENGTH
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = 'HardwareAssets' AND COLUMN_NAME = 'AssetTag';
            ");

            meta.isNullable.Should().Be("NO", "AssetTag must be NOT NULL");
            meta.maxLen.Should().Be(16, "AssetTag max length should be 16");
        }

        // ---------------------------------------------------------------
        // NEW: Assignments XOR must reject when NEITHER target is present
        // ---------------------------------------------------------------
        [Fact]
        public void Assignments_XOR_Rejects_NeitherSet()
        {
            using var con = new SqlConnection(_harness.ConnectionString);
            con.Open();
            using var tx = con.BeginTransaction();

            // Minimal role + user (FKs)
            var roleId = con.QuerySingle<int>(@"
                INSERT INTO Roles(RoleName, Description) VALUES ('Tester','Temp');
                SELECT CAST(SCOPE_IDENTITY() AS int);", transaction: tx);

            var userId = con.QuerySingle<int>(@"
                INSERT INTO Users(ExternalId, FullName, Email, EmployeeNumber, IsActive, RoleID)
                VALUES (@eid,'Test User','test@x.y','E000',1,@rid);
                SELECT CAST(SCOPE_IDENTITY() AS int);",
                new { eid = Guid.NewGuid(), rid = roleId }, tx);

            // Neither HardwareID nor SoftwareID set
            Action act = () => con.Execute(@"
                INSERT INTO Assignments(UserID, AssetKind, HardwareID, SoftwareID, AssignedAtUtc, UnassignedAtUtc)
                VALUES (@uid, 1, NULL, NULL, SYSUTCDATETIME(), NULL);",
                new { uid = userId }, tx);

            act.Should().Throw<SqlException>(
                "check constraint CK_Assignment_ExactlyOneAsset should reject 'neither set'");

            tx.Rollback();
        }

        // -----------------------------------------------------------------------------------------
        // Agreements — XOR + FileUri NOT NULL + DateAdded NOT NULL (skip if table absent)
        // -----------------------------------------------------------------------------------------
        [Fact]
        public void Agreements_XOR_And_FileUri_NotNull()
        {
            using var con = new SqlConnection(_harness.ConnectionString);
            con.Open();

            // Skip gracefully if Agreements table doesn't exist yet
            var exists = con.ExecuteScalar<int>("SELECT COUNT(*) FROM sys.tables WHERE name = 'Agreements';");
            if (exists == 0)
                return;

            using var tx = con.BeginTransaction();

            // Minimal role + user
            var roleId = con.QuerySingle<int>(@"
                INSERT INTO Roles(RoleName, Description) VALUES ('Tester','Temp');
                SELECT CAST(SCOPE_IDENTITY() AS int);", transaction: tx);

            var userId = con.QuerySingle<int>(@"
                INSERT INTO Users(ExternalId, FullName, Email, EmployeeNumber, IsActive, RoleID)
                VALUES (@eid,'Test User','test@x.y','E000',1,@rid);
                SELECT CAST(SCOPE_IDENTITY() AS int);",
                new { eid = Guid.NewGuid(), rid = roleId }, tx);

            // One hardware + one software to reference
            var hwId = con.QuerySingle<int>(@"
                INSERT INTO HardwareAssets
                  (AssetName, AssetType, AssetTag, Status, Manufacturer, Model, SerialNumber, WarrantyExpiration, PurchaseDate)
                VALUES
                  ('Laptop A','Laptop', @tag, 'InStock','Brand','ModelX', @sn, '2030-01-01', '2025-01-01');
                SELECT CAST(SCOPE_IDENTITY() AS int);",
                new { tag = NewTag16(), sn = $"SN-{Guid.NewGuid():N}".Substring(0, 18) }, tx);

            var swId = con.QuerySingle<int>(@"
                INSERT INTO SoftwareAssets
                  (SoftwareName, SoftwareType, SoftwareVersion, SoftwareLicenseKey, SoftwareUsageData, SoftwareCost, SoftwareLicenseExpiration)
                VALUES
                  ('App','License','1.0', @key, 0, 12.34, NULL);
                SELECT CAST(SCOPE_IDENTITY() AS int);",
                new { key = $"KEY-{Guid.NewGuid():N}" }, tx);

            // (a) Reject BOTH null (neither target set) — include DateAdded to avoid NOT NULL failure
            Action a1 = () => con.Execute(@"
                INSERT INTO Agreements(HardwareID, SoftwareID, AssetKind, FileUri, DateAdded)
                VALUES (NULL, NULL, 1, 'https://blob/ok', SYSUTCDATETIME());", transaction: tx);
            a1.Should().Throw<SqlException>("Agreements XOR should reject neither set");

            // (b) Reject BOTH set — include DateAdded
            Action a2 = () => con.Execute(@"
                INSERT INTO Agreements(HardwareID, SoftwareID, AssetKind, FileUri, DateAdded)
                VALUES (@h, @s, 1, 'https://blob/ok', SYSUTCDATETIME());", new { h = hwId, s = swId }, tx);
            a2.Should().Throw<SqlException>("Agreements XOR should reject both set");

            // (c) Reject NULL FileUri — include DateAdded
            Action a3 = () => con.Execute(@"
                INSERT INTO Agreements(HardwareID, SoftwareID, AssetKind, FileUri, DateAdded)
                VALUES (@h, NULL, 1, NULL, SYSUTCDATETIME());", new { h = hwId }, tx);
            a3.Should().Throw<SqlException>("FileUri should be NOT NULL");

            // (d) Valid: exactly one target + FileUri present + DateAdded present
            var okId = con.QuerySingle<int>(@"
                INSERT INTO Agreements(HardwareID, SoftwareID, AssetKind, FileUri, DateAdded)
                VALUES (@h, NULL, 1, 'https://blob/ok', SYSUTCDATETIME());
                SELECT CAST(SCOPE_IDENTITY() AS int);", new { h = hwId }, tx);
            okId.Should().BeGreaterThan(0);

            tx.Rollback();
        }

        // -----------------------------------------------------------------------------------------
        // Enforce unique AssetTag on Hardware, verify it (silently pass otherwise)
        // -----------------------------------------------------------------------------------------
        [Fact]
        public void Hardware_AssetTag_Is_Unique_When_Enforced()
        {
            using var con = new SqlConnection(_harness.ConnectionString);
            con.Open();

            var hasUnique = con.QuerySingle<int>(@"
                SELECT COUNT(*)
                FROM sys.indexes i
                JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                JOIN sys.tables  t ON t.object_id = i.object_id
                WHERE i.is_unique = 1
                  AND t.name = 'HardwareAssets'
                  AND c.name = 'AssetTag';");

            if (hasUnique == 0) return; // do nothing if schema doesn't enforce uniqueness

            using var tx = con.BeginTransaction();
            var tag = NewTag16();

            con.Execute(@"
                INSERT INTO HardwareAssets
                  (AssetName, AssetType, AssetTag, Status, Manufacturer, Model, SerialNumber, WarrantyExpiration, PurchaseDate)
                VALUES
                  ('X','Laptop', @tag, 'InStock','Brand','M','SN1','2030-01-01','2025-01-01');",
                new { tag }, tx);

            Action dup = () => con.Execute(@"
                INSERT INTO HardwareAssets
                  (AssetName, AssetType, AssetTag, Status, Manufacturer, Model, SerialNumber, WarrantyExpiration, PurchaseDate)
                VALUES
                  ('Y','Laptop', @tag, 'InStock','Brand','M','SN2','2030-01-01','2025-01-01');",
                new { tag }, tx);

            dup.Should().Throw<SqlException>("unique index should reject duplicate AssetTag");
            tx.Rollback();
        }

        // -----------------------------------------------------------------------------------------
        // Ensures CK rejects when BOTH HardwareID and SoftwareID are set
        // -----------------------------------------------------------------------------------------
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

            // Hardware with a valid 16-char AssetTag
            var hwId = con.QuerySingle<int>(@"
                INSERT INTO HardwareAssets(AssetName, AssetType, AssetTag, Status, Manufacturer, Model, SerialNumber, WarrantyExpiration, PurchaseDate)
                VALUES ('Laptop A','Laptop', @tag, 'InStock','Brand','ModelX', @sn, '2030-01-01','2025-01-01');
                SELECT CAST(SCOPE_IDENTITY() AS int);",
                new
                {
                    tag = NewTag16("TESTTAG-"), // ensures <=16 chars
                    sn = $"SN-{Guid.NewGuid():N}".Substring(0, 18)
                }, tx);

            var swId = con.QuerySingle<int>(@"
                INSERT INTO SoftwareAssets(SoftwareName, SoftwareType, SoftwareVersion, SoftwareLicenseKey, SoftwareUsageData, SoftwareCost, SoftwareLicenseExpiration)
                VALUES ('App','License','1.0', @key, 0, 12.34, NULL);
                SELECT CAST(SCOPE_IDENTITY() AS int);",
                new { key = $"KEY-{Guid.NewGuid():N}" }, tx);

            // Act + Assert: CK must reject when both HardwareID and SoftwareID are set
            Action act = () => con.Execute(@"
                INSERT INTO Assignments(UserID, AssetKind, HardwareID, SoftwareID, AssignedAtUtc, UnassignedAtUtc)
                VALUES (@uid, 1, @hwId, @swId, SYSUTCDATETIME(), NULL);
            ", new { uid = userId, hwId = hwId, swId = swId }, tx);

            act.Should().Throw<SqlException>();

            tx.Rollback(); // keep DB clean
        }
    }
}
