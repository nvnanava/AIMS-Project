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
