using AIMS.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIMS.Data;

public static class DbSeeder
{
    // Seeds Roles, Users (with supervisor chain), Hardware, Software, and one sample Assignment.
    // Idempotent: safe to run multiple times. Guarded from Prod unless explicitly enabled.
    public static async Task SeedAsync(
        AimsDbContext db,
        bool allowProdSeed,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        // ---- Guard Prod unless explicitly allowed ----
        var isProd = string.Equals(
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            "Production",
            StringComparison.OrdinalIgnoreCase);

        if (isProd && !allowProdSeed)
        {
            logger?.LogInformation("[DBSeeder] Skipped seeding in Production (AllowProdSeed=false).");
            return;
        }

        // 1) Roles
        var rolesWanted = new[]
        {
            new Role { RoleName = "Admin",        Description = "Full access" },
            new Role { RoleName = "IT Help Desk", Description = "IT staff, no admin actions" },
            new Role { RoleName = "Supervisor",   Description = "Manages direct reports" },
            new Role { RoleName = "Employee",     Description = "Standard user" }
        };

        foreach (var r in rolesWanted)
            await UpsertRoleAsync(db, r, ct);

        await db.SaveChangesAsync(ct);
        var roleByName = await db.Roles.AsNoTracking().ToDictionaryAsync(r => r.RoleName, ct);

        // 2) Users — compute deterministic ExternalId from EmployeeNumber (preferred) or Email
        Guid UId(string emp, string email) => FromString(string.IsNullOrWhiteSpace(emp) ? $"email:{email}" : $"emp:{emp}");

        var usersWanted = new[]
        {
            new User {
                FullName = "Alice Admin",
                Email = "alice.admin@aims.local",
                EmployeeNumber = "E1001",
                IsActive = true,
                RoleID = roleByName["Admin"].RoleID,
                ExternalId = UId("E1001","alice.admin@aims.local")
            },
            new User {
                FullName = "Ian IT",
                Email = "ian.it@aims.local",
                EmployeeNumber = "E1002",
                IsActive = true,
                RoleID = roleByName["IT Help Desk"].RoleID,
                ExternalId = UId("E1002","ian.it@aims.local")
            },
            new User {
                FullName = "Sam Supervisor",
                Email = "sam.supervisor@aims.local",
                EmployeeNumber = "E2001",
                IsActive = true,
                RoleID = roleByName["Supervisor"].RoleID,
                ExternalId = UId("E2001","sam.supervisor@aims.local")
            },
            new User {
                FullName = "Erin Employee",
                Email = "erin.employee@aims.local",
                EmployeeNumber = "E3001",
                IsActive = true,
                RoleID = roleByName["Employee"].RoleID,
                ExternalId = UId("E3001","erin.employee@aims.local")
            },
            new User {
                FullName = "Devon DirectReport",
                Email = "devon.report@aims.local",
                EmployeeNumber = "E3002",
                IsActive = true,
                RoleID = roleByName["Employee"].RoleID,
                ExternalId = UId("E3002","devon.report@aims.local")
            }
        };


        // Pass 1: create/update without SupervisorID (we need IDs first)
        foreach (var u in usersWanted)
            await UpsertUserAsync(db, u, ct);

        await db.SaveChangesAsync(ct);

        var usersByEmail = await db.Users.ToDictionaryAsync(u => u.Email, ct);

        // Pass 2: set supervisor (Sam supervises Erin + Devon)
        var sam = usersByEmail["sam.supervisor@aims.local"];
        await EnsureSupervisorAsync(db, usersByEmail["erin.employee@aims.local"], sam.UserID, ct);
        await EnsureSupervisorAsync(db, usersByEmail["devon.report@aims.local"],  sam.UserID, ct);
        await db.SaveChangesAsync(ct);

        // ---- 3) Hardware (natural key = SerialNumber) ----
        var hardwareWanted = new[]
        {
            new Hardware
            {
                AssetName = "MacBook Pro 14",
                AssetType = "Laptop",
                Status = "InUse",
                Manufacturer = "Apple",
                Model = "A2779",
                SerialNumber = "MBP14-AAA111",
                PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)),
                WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(2))
            },
            new Hardware
            {
                AssetName = "Dell Latitude",
                AssetType = "Laptop",
                Status = "Available",
                Manufacturer = "Dell",
                Model = "Latitude 7440",
                SerialNumber = "LAT7440-BBB222",
                PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6)),
                WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(2))
            },
            new Hardware
            {
                AssetName = "Logitech MX",
                AssetType = "Mouse",
                Status = "Available",
                Manufacturer = "Logitech",
                Model = "MX Master 3S",
                SerialNumber = "MOUSE-CCC333",
                PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-2)),
                WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1))
            }
        };
        foreach (var h in hardwareWanted)
            await UpsertHardwareAsync(db, h, ct);

        await db.SaveChangesAsync(ct);
        var hardwareBySerial = await db.HardwareAssets
            .AsNoTracking()
            .ToDictionaryAsync(h => h.SerialNumber, ct);

        // ---- 4) Software (natural key = SoftwareName + SoftwareVersion) ----
        var softwareWanted = new[]
        {
            new Software
            {
                SoftwareName = "Office 365",
                SoftwareType = "Productivity",
                SoftwareVersion = "2408",
                SoftwareLicenseKey = "O365-XXXX-YYYY",
                SoftwareLicenseExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                SoftwareUsageData = 0,
                SoftwareCost = 12.99m
            },
            new Software
            {
                SoftwareName = "Visual Studio",
                SoftwareType = "IDE",
                SoftwareVersion = "2022",
                SoftwareLicenseKey = "VS22-ABCD-EFGH",
                SoftwareLicenseExpiration = null,
                SoftwareUsageData = 0,
                SoftwareCost = 0.00m
            }
        };
        foreach (var s in softwareWanted)
            await UpsertSoftwareAsync(db, s, ct);

        await db.SaveChangesAsync(ct);
        var softwareByKey = await db.SoftwareAssets
            .AsNoTracking()
            .ToDictionaryAsync(s => (s.SoftwareName, s.SoftwareVersion), ct);

        // ---- 5) Sample assignment (idempotent) ----
        var now = DateTime.UtcNow;
        await EnsureAssignmentAsync(
            db,
            userId: usersByEmail["erin.employee@aims.local"].UserID,
            assetKind: AssetKind.Hardware,                       // <-- add this
            hardwareId: hardwareBySerial["MBP14-AAA111"].HardwareID,
            softwareId: null,
            assignedAtUtc: now.Date.AddDays(-10),
            ct: ct);
        
        await db.SaveChangesAsync(ct);

        logger?.LogInformation("[DBSeeder] Done. Roles:{Roles}, Users:{Users}, HW:{HW}, SW:{SW}",
            await db.Roles.CountAsync(ct),
            await db.Users.CountAsync(ct),
            await db.HardwareAssets.CountAsync(ct),
            await db.SoftwareAssets.CountAsync(ct));
    }

    private static Guid FromString(string input)
    {
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        var hash = sha1.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        return new Guid(bytes);
    }

    // ---------- helpers ----------

    private static async Task UpsertRoleAsync(AimsDbContext db, Role incoming, CancellationToken ct)
    {
        var existing = await db.Roles.FirstOrDefaultAsync(r => r.RoleName == incoming.RoleName, ct);
        if (existing is null)
        {
            await db.Roles.AddAsync(incoming, ct);
        }
        else
        {
            existing.Description = incoming.Description;
        }
    }

    private static async Task UpsertUserAsync(AimsDbContext db, User incoming, CancellationToken ct)
    {
        // ensure incoming.ExternalId is set deterministically
        if (incoming.ExternalId == Guid.Empty)
        {
            var key = !string.IsNullOrWhiteSpace(incoming.EmployeeNumber)
                ? $"emp:{incoming.EmployeeNumber}"
                : $"email:{incoming.Email}";
            incoming.ExternalId = FromString(key);
        }

        var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == incoming.Email, ct);
        if (existing is null)
        {
            await db.Users.AddAsync(incoming, ct);
        }
        else
        {
            // Backfill/repair ExternalId if missing or incorrect
            if (existing.ExternalId == Guid.Empty)
            {
                existing.ExternalId = incoming.ExternalId;
            }

            existing.FullName       = incoming.FullName;
            existing.EmployeeNumber = incoming.EmployeeNumber;
            existing.IsActive       = incoming.IsActive;
            existing.RoleID         = incoming.RoleID;
        }
    }

    private static async Task EnsureSupervisorAsync(AimsDbContext db, User user, int supervisorId, CancellationToken ct)
    {
        var tracked = await db.Users.FirstAsync(u => u.UserID == user.UserID, ct);
        if (tracked.SupervisorID != supervisorId)
            tracked.SupervisorID = supervisorId;
    }

    private static async Task UpsertHardwareAsync(AimsDbContext db, Hardware incoming, CancellationToken ct)
    {
        var existing = await db.HardwareAssets.FirstOrDefaultAsync(h => h.SerialNumber == incoming.SerialNumber, ct);
        if (existing is null)
        {
            await db.HardwareAssets.AddAsync(incoming, ct);
        }
        else
        {
            existing.AssetName          = incoming.AssetName;
            existing.AssetType          = incoming.AssetType;
            existing.Status             = incoming.Status;
            existing.Manufacturer       = incoming.Manufacturer;
            existing.Model              = incoming.Model;
            existing.PurchaseDate       = incoming.PurchaseDate;
            existing.WarrantyExpiration = incoming.WarrantyExpiration;
        }
    }

    private static async Task UpsertSoftwareAsync(AimsDbContext db, Software incoming, CancellationToken ct)
    {
        var existing = await db.SoftwareAssets
            .FirstOrDefaultAsync(s => s.SoftwareName == incoming.SoftwareName &&
                                      s.SoftwareVersion == incoming.SoftwareVersion, ct);

        if (existing is null)
        {
            await db.SoftwareAssets.AddAsync(incoming, ct);
        }
        else
        {
            existing.SoftwareType              = incoming.SoftwareType;
            existing.SoftwareLicenseKey        = incoming.SoftwareLicenseKey;
            existing.SoftwareLicenseExpiration = incoming.SoftwareLicenseExpiration;
            existing.SoftwareUsageData         = incoming.SoftwareUsageData;
            existing.SoftwareCost              = incoming.SoftwareCost;
        }
    }

    private static async Task EnsureAssignmentAsync(
    AimsDbContext db,
    int userId,
    AssetKind assetKind,          // Hardware or Software
    int? hardwareId,              // when Hardware, set; when Software, null
    int? softwareId,              // when Software, set; when Hardware, null
    DateTime assignedAtUtc,
    CancellationToken ct)
    {
        // Normalize: enforce exactly one side is set
        int? assetTag = null;
        int? softId   = null;

        if (assetKind == AssetKind.Hardware)
        {
            assetTag = hardwareId ?? throw new ArgumentNullException(nameof(hardwareId));
            softId   = null; // force NULL
        }
        else if (assetKind == AssetKind.Software)
        {
            assetTag = null;
            softId   = softwareId ?? throw new ArgumentNullException(nameof(softwareId));
        }
        else
        {
            throw new InvalidOperationException("Unknown AssetKind for assignment seeding.");
        }

        // Idempotency: same user + same concrete asset + same assigned date
        var exists = await db.Assignments.AnyAsync(a =>
            a.UserID == userId &&
            a.AssetKind == assetKind &&
            a.AssetTag == assetTag &&
            a.SoftwareID == softId &&
            a.AssignedAtUtc.Date == assignedAtUtc.Date, ct);

        if (!exists)
        {
            db.Assignments.Add(new Assignment
            {
                UserID = userId,
                AssetKind = assetKind,
                AssetTag = assetTag,
                SoftwareID = softId,
                AssignedAtUtc = assignedAtUtc,
                UnassignedAtUtc = null
            });
        }
    }
}