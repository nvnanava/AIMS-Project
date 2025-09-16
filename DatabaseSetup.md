# Database Setup (AIMS)

This guide explains how to set up, migrate, seed, and **smoke test the AIMS database**.  
All smoke tests in this document use **sqlcmd only** (no curl). A fully automated SQL smoke script is provided at the end.

---

## Normal Workflow (Scripts)

For day‑to‑day dev work, use our helper scripts:

```bash
# bring up stack, wait for SQL to be healthy
./scripts/up_stack.sh dev

# ensure DB exists + apply EF migrations (and deterministic seed in Dev)
./scripts/db_ready.sh dev ensure
```

For a full reseed (**dangerous, wipes data**):

```bash
./scripts/db_ready.sh dev reseed
```

In **Production**, migrations are skipped automatically; seeding only runs if `AllowProdSeed=true`.

---

## Troubleshooting (Containers & Connectivity)

### Check container health

```bash
docker compose -f docker-compose.dev.yml ps
docker inspect -f '{{.State.Health.Status}}' $(docker compose -f docker-compose.dev.yml ps -q sqlserver-dev)
docker compose -f docker-compose.dev.yml logs -f sqlserver-dev
docker compose -f docker-compose.dev.yml logs -f web-dev
```

### Connect to SQL Server with `sqlcmd`

**Via the SQL container (works without installing sqlcmd locally):**
```bash
docker exec -it $(docker compose -f docker-compose.dev.yml ps -q sqlserver-dev)   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'StrongP@ssword!' -C
```

**If you installed sqlcmd locally (Mac Homebrew/Windows installer):**
```bash
sqlcmd -S localhost,1433 -U sa -P 'StrongP@ssword!' -C
```

> Tip: Add `-No` to suppress “rows affected” output; add `-W` to trim spaces; add `-h-1` to hide column headers for clean diffs.

---

## Manual Smoke Tests (sqlcmd)

> Replace `$(docker compose -f docker-compose.dev.yml ps -q sqlserver-dev)` with your SQL container ID if you’re not using Compose.

### 1) Database presence

```bash
docker exec -it $(docker compose -f docker-compose.dev.yml ps -q sqlserver-dev)   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'StrongP@ssword!' -No -C -Q "SELECT name FROM sys.databases ORDER BY name;"
```

**Expected**: `AIMS` listed alongside system DBs.

---

### 2) Target DB & tables

```bash
docker exec -it $(docker compose -f docker-compose.dev.yml ps -q sqlserver-dev) /opt/mssql-tools18/bin/sqlcmd   -S localhost -U sa -P 'StrongP@ssword!' -No -C -Q "USE AIMS; SELECT s.name AS [schema], t.name AS [table] FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id ORDER BY s.name,t.name;"
```

**Key tables expected**: `Users`, `Roles`, `HardwareAssets`, `SoftwareAssets`, `Assignments`.

---

### 3) Basic counts (sanity)

```bash
docker exec -it $(docker compose -f docker-compose.dev.yml ps -q sqlserver-dev) /opt/mssql-tools18/bin/sqlcmd   -S localhost -U sa -P 'StrongP@ssword!' -No -C -Q "USE AIMS; SELECT 'Roles' AS [Table], COUNT(*) FROM dbo.Roles UNION ALL SELECT 'Users', COUNT(*) FROM dbo.Users UNION ALL SELECT 'HardwareAssets', COUNT(*) FROM dbo.HardwareAssets UNION ALL SELECT 'SoftwareAssets', COUNT(*) FROM dbo.SoftwareAssets UNION ALL SELECT 'Assignments', COUNT(*) FROM dbo.Assignments;"
```

---

### 4) Role & user sanity

```bash
docker exec -it $(docker compose -f docker-compose.dev.yml ps -q sqlserver-dev) /opt/mssql-tools18/bin/sqlcmd   -S localhost -U sa -P 'StrongP@ssword!' -No -C -Q "USE AIMS; SELECT RoleName, COUNT(*) AS Users FROM dbo.Roles r JOIN dbo.Users u ON u.RoleID=r.RoleID GROUP BY RoleName ORDER BY RoleName;"
```

List a few seeded users:
```bash
docker exec -it $(docker compose -f docker-compose.dev.yml ps -q sqlserver-dev) /opt/mssql-tools18/bin/sqlcmd   -S localhost -U sa -P 'StrongP@ssword!' -No -C -Q "USE AIMS; SELECT TOP(10) UserID, FullName, Email, EmployeeNumber, RoleID FROM dbo.Users ORDER BY UserID;"
```

---

### 5) Supervisor chain (direct reports sample)

```bash
docker exec -it $(docker compose -f docker-compose.dev.yml ps -q sqlserver-dev) /opt/mssql-tools18/bin/sqlcmd   -S localhost -U sa -P 'StrongP@ssword!' -No -C -Q "USE AIMS; SELECT sup.FullName AS Supervisor, emp.FullName AS Report, emp.EmployeeNumber FROM dbo.Users emp JOIN dbo.Users sup ON emp.SupervisorID=sup.UserID ORDER BY sup.FullName, emp.FullName;"
```

---

### 6) Hardware & software inventories

**Hardware with assigned user (if any):**
```bash
docker exec -it $(docker compose -f docker-compose.dev.yml ps -q sqlserver-dev) /opt/mssql-tools18/bin/sqlcmd   -S localhost -U sa -P 'StrongP@ssword!' -No -C -Q "USE AIMS; SELECT h.HardwareID, h.AssetName, h.AssetType, h.SerialNumber, h.Status, u.FullName AS AssignedTo FROM dbo.HardwareAssets h LEFT JOIN dbo.Assignments a ON a.AssetKind=1 AND a.UnassignedAtUtc IS NULL AND a.AssetTag=h.HardwareID LEFT JOIN dbo.Users u ON u.UserID=a.UserID ORDER BY h.HardwareID;"
```

**Software with assigned user (if any):**
```bash
docker exec -it $(docker compose -f docker-compose.dev.yml ps -q sqlserver-dev) /opt/mssql-tools18/bin/sqlcmd   -S localhost -U sa -P 'StrongP@ssword!' -No -C -Q "USE AIMS; SELECT s.SoftwareID, s.SoftwareName, s.SoftwareVersion, s.SoftwareLicenseKey, u.FullName AS AssignedTo FROM dbo.SoftwareAssets s LEFT JOIN dbo.Assignments a ON a.AssetKind=2 AND a.UnassignedAtUtc IS NULL AND a.SoftwareID=s.SoftwareID LEFT JOIN dbo.Users u ON u.UserID=a.UserID ORDER BY s.SoftwareID;"
```

---

### 7) Active vs. closed assignments

**Active (open) assignments:**
```bash
docker exec -it $(docker compose -f docker-compose.dev.yml ps -q sqlserver-dev) /opt/mssql-tools18/bin/sqlcmd   -S localhost -U sa -P 'StrongP@ssword!' -No -C -Q "USE AIMS; SELECT a.AssignmentID, a.AssetKind, a.AssetTag, a.SoftwareID, a.UserID, u.FullName, a.AssignedAtUtc FROM dbo.Assignments a JOIN dbo.Users u ON u.UserID=a.UserID WHERE a.UnassignedAtUtc IS NULL ORDER BY a.AssignedAtUtc DESC;"
```

**Closed (historical) assignments:**
```bash
docker exec -it $(docker compose -f docker-compose.dev.yml ps -q sqlserver-dev) /opt/mssql-tools18/bin/sqlcmd   -S localhost -U sa -P 'StrongP@ssword!' -No -C -Q "USE AIMS; SELECT a.AssignmentID, a.AssetKind, a.AssetTag, a.SoftwareID, a.UserID, u.FullName, a.AssignedAtUtc, a.UnassignedAtUtc FROM dbo.Assignments a JOIN dbo.Users u ON u.UserID=a.UserID WHERE a.UnassignedAtUtc IS NOT NULL ORDER BY a.AssignedAtUtc DESC;"
```

---

### 8) “Available” hardware (not assigned)

```bash
docker exec -it $(docker compose -f docker-compose.dev.yml ps -q sqlserver-dev) /opt/mssql-tools18/bin/sqlcmd   -S localhost -U sa -P 'StrongP@ssword!' -No -C -Q "USE AIMS; SELECT h.HardwareID, h.AssetName, h.SerialNumber, h.Status FROM dbo.HardwareAssets h LEFT JOIN dbo.Assignments a ON a.AssetKind=1 AND a.UnassignedAtUtc IS NULL AND a.AssetTag=h.HardwareID WHERE a.AssignmentID IS NULL ORDER BY h.HardwareID;"
```

---

### 9) Idempotency checks (duplicate guards)

**Users by email (duplicates should be ZERO):**
```bash
docker exec -it $(docker compose -f docker-compose.dev.yml ps -q sqlserver-dev) /opt/mssql-tools18/bin/sqlcmd   -S localhost -U sa -P 'StrongP@ssword!' -No -C -Q "USE AIMS; SELECT Email, COUNT(*) AS Cnt FROM dbo.Users GROUP BY Email HAVING COUNT(*)>1;"
```

**Hardware by SerialNumber (duplicates should be ZERO):**
```bash
docker exec -it $(docker compose -f docker-compose.dev.yml ps -q sqlserver-dev) /opt/mssql-tools18/bin/sqlcmd   -S localhost -U sa -P 'StrongP@ssword!' -No -C -Q "USE AIMS; SELECT SerialNumber, COUNT(*) AS Cnt FROM dbo.HardwareAssets GROUP BY SerialNumber HAVING COUNT(*)>1;"
```

**Software uniqueness (Name+Version):**
```bash
docker exec -it $(docker compose -f docker-compose.dev.yml ps -q sqlserver-dev) /opt/mssql-tools18/bin/sqlcmd   -S localhost -U sa -P 'StrongP@ssword!' -No -C -Q "USE AIMS; SELECT SoftwareName, SoftwareVersion, COUNT(*) AS Cnt FROM dbo.SoftwareAssets GROUP BY SoftwareName, SoftwareVersion HAVING COUNT(*)>1;"
```

---

### 10) Transactional “no‑persist” tests (safe to run)

**Simulate creating & closing a hardware assignment in a transaction (ROLLBACK):**
```bash
docker exec -i $(docker compose -f docker-compose.dev.yml ps -q sqlserver-dev) /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'StrongP@ssword!' -No -C <<'SQL'
USE AIMS;
SET NOCOUNT ON;
BEGIN TRAN;
DECLARE @UserID int = (SELECT TOP(1) UserID FROM dbo.Users ORDER BY UserID);
DECLARE @HardwareID int = (SELECT TOP(1) HardwareID FROM dbo.HardwareAssets ORDER BY HardwareID);

PRINT '--- BEFORE (open assignments count for chosen HW) ---';
SELECT COUNT(*) AS OpenCount FROM dbo.Assignments WHERE AssetKind=1 AND AssetTag=@HardwareID AND UnassignedAtUtc IS NULL;

INSERT INTO dbo.Assignments(AssetKind, AssetTag, UserID, AssignedAtUtc, UnassignedAtUtc)
VALUES (1, @HardwareID, @UserID, SYSUTCDATETIME(), NULL);

PRINT '--- AFTER INSERT (open assignments count) ---';
SELECT COUNT(*) AS OpenCount FROM dbo.Assignments WHERE AssetKind=1 AND AssetTag=@HardwareID AND UnassignedAtUtc IS NULL;

UPDATE dbo.Assignments SET UnassignedAtUtc = SYSUTCDATETIME() WHERE AssetKind=1 AND AssetTag=@HardwareID AND UnassignedAtUtc IS NULL;

PRINT '--- AFTER CLOSE (open assignments count) ---';
SELECT COUNT(*) AS OpenCount FROM dbo.Assignments WHERE AssetKind=1 AND AssetTag=@HardwareID AND UnassignedAtUtc IS NULL;

ROLLBACK;
PRINT '--- ROLLBACK DONE (no changes persisted) ---';
SQL
```

---

### 11) Index & FK sanity

```bash
# Indexes on key tables
docker exec -it $(docker compose -f docker-compose.dev.yml ps -q sqlserver-dev) /opt/mssql-tools18/bin/sqlcmd   -S localhost -U sa -P 'StrongP@ssword!' -W -C -Q "USE AIMS; SELECT t.name AS TableName, i.name AS IndexName, i.is_unique, i.type_desc FROM sys.indexes i JOIN sys.tables t ON t.object_id=i.object_id WHERE t.name IN ('Users','HardwareAssets','SoftwareAssets','Assignments') AND i.index_id>0 ORDER BY t.name, i.is_unique DESC, i.name;"

# Foreign keys
docker exec -it $(docker compose -f docker-compose.dev.yml ps -q sqlserver-dev) /opt/mssql-tools18/bin/sqlcmd   -S localhost -U sa -P 'StrongP@ssword!' -W -C -Q "USE AIMS; SELECT fk.name AS FKName, OBJECT_NAME(fk.parent_object_id) AS ChildTable, cpa.name AS ChildColumn, OBJECT_NAME(fk.referenced_object_id) AS ParentTable, cref.name AS ParentColumn FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id JOIN sys.columns cpa ON cpa.object_id=fkc.parent_object_id AND cpa.column_id=fkc.parent_column_id JOIN sys.columns cref ON cref.object_id=fkc.referenced_object_id AND cref.column_id=fkc.referenced_column_id ORDER BY ChildTable, FKName;"
```

---

## Fully Automated SQL Smoke (single command)

Run this to execute the **comprehensive SQL smoke suite** and print clean, labeled sections:

```bash
docker exec -i $(docker compose -f docker-compose.dev.yml ps -q sqlserver-dev)   /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'StrongP@ssword!' -W -C -i /sql/aims_smoke.sql
```

> The file `sql/aims_smoke.sql` is included below and prints friendly section headers + compact, readable output.

If your repo path in the container differs, you can upload and run it like this:

```bash
# copy the script into the SQL container (adjust source path as needed)
docker cp ./sql/aims_smoke.sql $(docker compose -f docker-compose.dev.yml ps -q sqlserver-dev):/sql/aims_smoke.sql

# now run it
docker exec -i $(docker compose -f docker-compose.dev.yml ps -q sqlserver-dev) \
  /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'StrongP@ssword!' -W -C -I \
  -i /sql/aims_smoke.sql
```

---

## File: `sql/aims_smoke.sql` (automated suite)

```sql
-- ===========================================================
-- AIMS Smoke Test Script
-- Verifies schema, seed data, and idempotency.
-- ===========================================================

-- Required SET options for filtered/computed indexes
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

:setvar DB AIMS
PRINT '== AIMS SQL Smoke ==';
PRINT 'Database: $(DB)';
PRINT 'Started at: ' + CONVERT(varchar(30), SYSUTCDATETIME(), 126);
PRINT '--------------------------------------------';
SET NOCOUNT ON;
USE $(DB);
GO

PRINT '';
PRINT '--- (1) Databases ---';
SELECT name FROM sys.databases ORDER BY name;
GO

PRINT '';
PRINT '--- (2) Tables ---';
SELECT s.name AS [schema], t.name AS [table] FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id ORDER BY s.name,t.name;
GO

PRINT '';
PRINT '--- (3) Row counts ---';
SELECT 'Roles' AS [Table], COUNT(*) AS Cnt FROM dbo.Roles
UNION ALL SELECT 'Users', COUNT(*) FROM dbo.Users
UNION ALL SELECT 'HardwareAssets', COUNT(*) FROM dbo.HardwareAssets
UNION ALL SELECT 'SoftwareAssets', COUNT(*) FROM dbo.SoftwareAssets
UNION ALL SELECT 'Assignments', COUNT(*) FROM dbo.Assignments;
GO

PRINT '';
PRINT '--- (4) Users by Role ---';
SELECT r.RoleName, COUNT(*) AS Users
FROM dbo.Roles r
JOIN dbo.Users u ON u.RoleID=r.RoleID
GROUP BY r.RoleName
ORDER BY r.RoleName;
GO

PRINT '';
PRINT '--- (5) Example Users ---';
SELECT TOP(10) u.UserID, u.FullName, u.Email, u.EmployeeNumber, u.RoleID
FROM dbo.Users u
ORDER BY u.UserID;
GO

PRINT '';
PRINT '--- (6) Supervisor → Direct Reports ---';
SELECT sup.FullName AS Supervisor, emp.FullName AS Report, emp.EmployeeNumber
FROM dbo.Users emp
JOIN dbo.Users sup ON emp.SupervisorID=sup.UserID
ORDER BY sup.FullName, emp.FullName;
GO

PRINT '';
PRINT '--- (7) Hardware w/ Assigned User (open only) ---';
SELECT h.HardwareID, h.AssetName, h.AssetType, h.SerialNumber, h.Status,
       u.FullName AS AssignedTo
FROM dbo.HardwareAssets h
LEFT JOIN dbo.Assignments a ON a.AssetKind=1 AND a.UnassignedAtUtc IS NULL AND a.AssetTag=h.HardwareID
LEFT JOIN dbo.Users u ON u.UserID=a.UserID
ORDER BY h.HardwareID;
GO

PRINT '';
PRINT '--- (8) Software w/ Assigned User (open only) ---';
SELECT s.SoftwareID, s.SoftwareName, s.SoftwareVersion, s.SoftwareLicenseKey,
       u.FullName AS AssignedTo
FROM dbo.SoftwareAssets s
LEFT JOIN dbo.Assignments a ON a.AssetKind=2 AND a.UnassignedAtUtc IS NULL AND a.SoftwareID=s.SoftwareID
LEFT JOIN dbo.Users u ON u.UserID=a.UserID
ORDER BY s.SoftwareID;
GO

PRINT '';
PRINT '--- (9) Active Assignments ---';
SELECT a.AssignmentID, a.AssetKind, a.AssetTag, a.SoftwareID, a.UserID, u.FullName, a.AssignedAtUtc
FROM dbo.Assignments a
JOIN dbo.Users u ON u.UserID=a.UserID
WHERE a.UnassignedAtUtc IS NULL
ORDER BY a.AssignedAtUtc DESC;
GO

PRINT '';
PRINT '--- (10) Closed Assignments ---';
SELECT a.AssignmentID, a.AssetKind, a.AssetTag, a.SoftwareID, a.UserID, u.FullName, a.AssignedAtUtc, a.UnassignedAtUtc
FROM dbo.Assignments a
JOIN dbo.Users u ON u.UserID=a.UserID
WHERE a.UnassignedAtUtc IS NOT NULL
ORDER BY a.AssignedAtUtc DESC;
GO

PRINT '';
PRINT '--- (11) Available (Unassigned) Hardware ---';
SELECT h.HardwareID, h.AssetName, h.SerialNumber, h.Status
FROM dbo.HardwareAssets h
LEFT JOIN dbo.Assignments a ON a.AssetKind=1 AND a.UnassignedAtUtc IS NULL AND a.AssetTag=h.HardwareID
WHERE a.AssignmentID IS NULL
ORDER BY h.HardwareID;
GO

PRINT '';
PRINT '--- (12) Idempotency Checks (duplicates should be zero rows) ---';
PRINT 'Users by Email (dupes)';
SELECT Email, COUNT(*) AS Cnt FROM dbo.Users GROUP BY Email HAVING COUNT(*)>1;
PRINT 'Hardware by SerialNumber (dupes)';
SELECT SerialNumber, COUNT(*) AS Cnt FROM dbo.HardwareAssets GROUP BY SerialNumber HAVING COUNT(*)>1;
PRINT 'Software by Name+Version (dupes)';
SELECT SoftwareName, SoftwareVersion, COUNT(*) AS Cnt FROM dbo.SoftwareAssets GROUP BY SoftwareName, SoftwareVersion HAVING COUNT(*)>1;
GO

PRINT '';
PRINT '--- (13) Indexes ---';
SELECT t.name AS TableName, i.name AS IndexName, i.is_unique, i.type_desc
FROM sys.indexes i
JOIN sys.tables t ON t.object_id=i.object_id
WHERE t.name IN ('Users','HardwareAssets','SoftwareAssets','Assignments') AND i.index_id>0
ORDER BY t.name, i.is_unique DESC, i.name;
GO

PRINT '';
PRINT '--- (14) Foreign Keys ---';
SELECT fk.name AS FKName,
       OBJECT_NAME(fk.parent_object_id)  AS ChildTable,
       cpa.name                          AS ChildColumn,
       OBJECT_NAME(fk.referenced_object_id) AS ParentTable,
       cref.name                         AS ParentColumn
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
JOIN sys.columns cpa ON cpa.object_id=fkc.parent_object_id AND cpa.column_id=fkc.parent_column_id
JOIN sys.columns cref ON cref.object_id=fkc.referenced_object_id AND cref.column_id=fkc.referenced_column_id
ORDER BY ChildTable, FKName;
GO

PRINT '';
PRINT '--- (15) Transactional No-Persist Assignment Test (robust) ---';
BEGIN TRAN;

DECLARE @UserID int = (SELECT TOP(1) UserID FROM dbo.Users ORDER BY UserID);
DECLARE @HardwareID int = NULL;

-- Prefer an AVAILABLE (unassigned) hardware
SELECT TOP(1) @HardwareID = h.HardwareID
FROM dbo.HardwareAssets h
LEFT JOIN dbo.Assignments a
  ON a.AssetKind = 1 AND a.UnassignedAtUtc IS NULL AND a.AssetTag = h.HardwareID
WHERE a.AssignmentID IS NULL
ORDER BY h.HardwareID;

-- If none available, pick a currently assigned one and (within this TRAN) close it to make room
IF @HardwareID IS NULL
BEGIN
  SELECT TOP(1) @HardwareID = h.HardwareID
  FROM dbo.HardwareAssets h
  JOIN dbo.Assignments a
    ON a.AssetKind = 1 AND a.UnassignedAtUtc IS NULL AND a.AssetTag = h.HardwareID
  ORDER BY h.HardwareID;

  PRINT 'No available hardware found; temporarily closing current open assignment in-transaction...';
  UPDATE dbo.Assignments
  SET UnassignedAtUtc = SYSUTCDATETIME()
  WHERE AssetKind = 1 AND AssetTag = @HardwareID AND UnassignedAtUtc IS NULL;
END

PRINT 'Before insert (open count):';
SELECT COUNT(*) AS OpenCount
FROM dbo.Assignments
WHERE AssetKind = 1 AND AssetTag = @HardwareID AND UnassignedAtUtc IS NULL;

-- Insert a new OPEN assignment
INSERT INTO dbo.Assignments (AssetKind, AssetTag, UserID, AssignedAtUtc, UnassignedAtUtc)
VALUES (1, @HardwareID, @UserID, SYSUTCDATETIME(), NULL);

PRINT 'After insert (open count):';
SELECT COUNT(*) AS OpenCount
FROM dbo.Assignments
WHERE AssetKind = 1 AND AssetTag = @HardwareID AND UnassignedAtUtc IS NULL;

-- Close it to simulate return
UPDATE dbo.Assignments
SET UnassignedAtUtc = SYSUTCDATETIME()
WHERE AssetKind = 1 AND AssetTag = @HardwareID AND UnassignedAtUtc IS NULL;

PRINT 'After close (open count):';
SELECT COUNT(*) AS OpenCount
FROM dbo.Assignments
WHERE AssetKind = 1 AND AssetTag = @HardwareID AND UnassignedAtUtc IS NULL;

ROLLBACK;
PRINT 'Rolled back; no changes persisted.';
GO

PRINT '--------------------------------------------';
PRINT 'Completed at: ' + CONVERT(varchar(30), SYSUTCDATETIME(), 126);
GO
```