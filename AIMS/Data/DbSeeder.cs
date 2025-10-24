using AIMS.Models;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Data;

public static class DbSeeder
{
    // Seeds Roles, Users (with supervisor chain), Hardware, Software, and demo Assignments.
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

        // ---------------- 1) Roles ----------------
        var rolesWanted = new[]
        {
            new Role { RoleName = "Admin",        Description = "Full access" },
            new Role { RoleName = "IT Help Desk", Description = "IT staff, no admin actions" },
            new Role { RoleName = "Supervisor",   Description = "Manages direct reports" },
            new Role { RoleName = "Employee",     Description = "Standard user" }
        };
        foreach (var r in rolesWanted) await UpsertRoleAsync(db, r, ct);
        await db.SaveChangesAsync(ct);

        var roleByName = await db.Roles.AsNoTracking().ToDictionaryAsync(r => r.RoleName, ct);

        // ---------------- 2) Users ----------------
        Guid UId(string emp, string email) =>
            FromString(string.IsNullOrWhiteSpace(emp) ? $"email:{email}" : $"emp:{emp}");

        var usersWanted = new[]
        {
            new User { FullName = "John Smith", Email = "john.smith@aims.local", EmployeeNumber = "28809",
                       IsActive = true, RoleID = roleByName["Supervisor"].RoleID, ExternalId = UId("28809","john.smith@aims.local") },

            new User { FullName = "Jane Doe", Email = "jane.doe@aims.local", EmployeeNumber = "69444",
                       IsActive = true, RoleID = roleByName["IT Help Desk"].RoleID, ExternalId = UId("69444","jane.doe@aims.local") },

            new User { FullName = "Randy Orton", Email = "randy.orton@aims.local", EmployeeNumber = "58344",
                       IsActive = true, RoleID = roleByName["IT Help Desk"].RoleID, ExternalId = UId("58344","randy.orton@aims.local") },

            new User { FullName = "Robin Williams", Email = "robin.williams@aims.local", EmployeeNumber = "10971",
                       IsActive = true, RoleID = roleByName["IT Help Desk"].RoleID, ExternalId = UId("10971","robin.williams@aims.local") },

            new User { FullName = "Sarah Johnson", Email = "sarah.johnson@aims.local", EmployeeNumber = "62241",
                       IsActive = true, RoleID = roleByName["Supervisor"].RoleID, ExternalId = UId("62241","sarah.johnson@aims.local") },

            new User { FullName = "Caitlin Clark", Email = "caitlin.clark@aims.local", EmployeeNumber = "90334",
                       IsActive = true, RoleID = roleByName["Supervisor"].RoleID, ExternalId = UId("90334","caitlin.clark@aims.local") },

            new User { FullName = "Brian Regan", Email = "brian.regan@aims.local", EmployeeNumber = "27094",
                       IsActive = true, RoleID = roleByName["Supervisor"].RoleID, ExternalId = UId("27094","brian.regan@aims.local") },

            new User { FullName = "Maximillian Brandt", Email = "max.brandt@aims.local", EmployeeNumber = "20983",
                       IsActive = true, RoleID = roleByName["Admin"].RoleID, ExternalId = UId("20983","max.brandt@aims.local") },

            new User { FullName = "Kate Rosenberg", Email = "kate.rosenberg@aims.local", EmployeeNumber = "93232",
                       IsActive = true, RoleID = roleByName["Admin"].RoleID, ExternalId = UId("93232","kate.rosenberg@aims.local") },

            new User { FullName = "Emily Carter", Email = "emily.carter@aims.local", EmployeeNumber = "47283",
                       IsActive = true, RoleID = roleByName["IT Help Desk"].RoleID, ExternalId = UId("47283","emily.carter@aims.local") },

            new User { FullName = "Bruce Wayne", Email = "bruce.wayne@aims.local", EmployeeNumber = "34532",
                       IsActive = true, RoleID = roleByName["IT Help Desk"].RoleID, ExternalId = UId("34532","bruce.wayne@aims.local") },

            // --- NEW: Tyler (Supervisor 80003) ---
            new User { FullName = "Tyler Burguillos", Email = "tnburg@pacbell.net", EmployeeNumber = "80003",
                       IsActive = true, RoleID = roleByName["Supervisor"].RoleID, ExternalId = UId("80003","tnburg@pacbell.net") },
        };

        foreach (var u in usersWanted) await UpsertUserAsync(db, u, ct);
        await db.SaveChangesAsync(ct);

        // Supervisor chain: Jane, Randy, Robin, Emily, Bruce report to John (28809)
        var usersByEmp = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.EmployeeNumber!, ct);
        int johnId = usersByEmp["28809"].UserID;
        string[] reportsOfJohn = { "69444", "58344", "10971", "47283", "34532" };
        foreach (var emp in reportsOfJohn)
        {
            if (usersByEmp.TryGetValue(emp, out var user))
                await EnsureSupervisorAsync(db, user, johnId, ct);
        }
        await db.SaveChangesAsync(ct);

        // --- NEW: Map two existing users to Tyler (80003) as direct reports ---
        int tylerId = usersByEmp["80003"].UserID;
        string[] reportsOfTyler = { "34532", "62241" }; // Bruce, Sarah
        foreach (var emp in reportsOfTyler)
        {
            if (usersByEmp.TryGetValue(emp, out var user))
                await EnsureSupervisorAsync(db, user, tylerId, ct);
        }
        await db.SaveChangesAsync(ct);

        // ---------------- 3) Hardware ----------------
        var hardwareWanted = new[]
        {
            // Laptops
            new Hardware { AssetName="Lenovo ThinkPad E16", AssetType="Laptop", Status="Assigned", Manufacturer="Lenovo", Model="", SerialNumber="LT-0020",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) },
            new Hardware { AssetName="Samsung Galaxy Book4", AssetType="Laptop", Status="Damaged", Manufacturer="Samsung", Model="", SerialNumber="LT-0005",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) },
            new Hardware { AssetName="Dell Inspiron 15", AssetType="Laptop", Status="Assigned", Manufacturer="Dell", Model="", SerialNumber="LT-0115",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) },

            // Monitors
            new Hardware { AssetName="Dell S2421NX", AssetType ="Monitor", Status="Assigned", Manufacturer="Dell", Model ="", SerialNumber="MN-0001",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) },
            new Hardware { AssetName="HP 527SH", AssetType="Monitor", Status="Assigned", Manufacturer="HP", Model="", SerialNumber="MN-0023",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) },

            // Desktops
            new Hardware { AssetName="Lenovo IdeaCentre 3", AssetType="Desktop", Status="Damaged", Manufacturer="Lenovo", Model="", SerialNumber="DT-0011",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) },
            new Hardware { AssetName="HP Pavillion TP01-2234", AssetType="Desktop", Status="Available", Manufacturer="HP", Model="", SerialNumber="DT-0075",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) },
            new Hardware { AssetName="Dell Inspiron 3030", AssetType="Desktop", Status="Assigned", Manufacturer="Dell", Model="", SerialNumber="DT-0100",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) },

            // Headsets
            new Hardware { AssetName="Logitech Zone 300", AssetType="Headset", Status="Available", Manufacturer ="Logi", Model="", SerialNumber="HS-0080",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) },
            new Hardware { AssetName="Logitech Zone Vibe 100", AssetType ="Headset", Status ="In Repair", Manufacturer ="Logi", Model="", SerialNumber="HS-0015",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) },
            new Hardware { AssetName="Poly Voyager 4320", AssetType="Headset", Status="In Repair", Manufacturer ="Poly", Model="", SerialNumber="HS-0001",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) },

            // Cables
            new Hardware { AssetName="Belkin BoostCharge 3.3ft USB-C", AssetType="Charging Cable", Status="Available", Manufacturer="Belkin", Model="", SerialNumber="CC-0088",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) },
            new Hardware { AssetName="j5create 100W Super Charger", AssetType="Charging Cable", Status="Assigned", Manufacturer="j5create", Model ="", SerialNumber="CC-0019",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) },
        };
        foreach (var h in hardwareWanted) await UpsertHardwareAsync(db, h, ct);
        await db.SaveChangesAsync(ct);

        var hardwareBySerial = await db.HardwareAssets.AsNoTracking()
            .ToDictionaryAsync(h => h.SerialNumber, ct);

        // ---------------- 3b) Reset HW assignment state to match seeded Status ----------------
        // Close open assignments for any hardware whose seeded status is not "Assigned"
        var mustBeUnassignedIds = await db.HardwareAssets
            .Where(h => h.Status != "Assigned")
            .Select(h => h.HardwareID)
            .ToListAsync(ct);

        if (mustBeUnassignedIds.Count > 0)
        {
            var openToClose = await db.Assignments
                .Where(a => a.AssetKind == AssetKind.Hardware
                         && a.UnassignedAtUtc == null
                         && a.HardwareID != null
                         && mustBeUnassignedIds.Contains(a.HardwareID.Value))
                .ToListAsync(ct);

            foreach (var a in openToClose)
                a.UnassignedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
        }

        // ---------------- 4) Software ----------------
        var softwareWanted = new[]
        {
            new Software { SoftwareName = "Microsoft 365 Business", SoftwareType = "Software", SoftwareVersion = "1.0",
                           SoftwareLicenseKey = "SW-0100", SoftwareLicenseExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)), SoftwareUsageData = 0, SoftwareCost = 12.99m },
            new Software { SoftwareName = "Adobe Photoshop", SoftwareType = "Software", SoftwareVersion = "2024",
                           SoftwareLicenseKey = "SW-0200", SoftwareLicenseExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)), SoftwareUsageData = 0, SoftwareCost = 20.99m },
            new Software { SoftwareName = "Slack", SoftwareType = "Software", SoftwareVersion = "5.3",
                           SoftwareLicenseKey = "SW-0300", SoftwareLicenseExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)), SoftwareUsageData = 0, SoftwareCost = 8.00m },
            new Software { SoftwareName = "Zoom Pro", SoftwareType = "Software", SoftwareVersion = "7.1",
                           SoftwareLicenseKey = "SW-0400", SoftwareLicenseExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)), SoftwareUsageData = 0, SoftwareCost = 15.99m },
            new Software { SoftwareName = "IntelliJ IDEA", SoftwareType = "Software", SoftwareVersion = "2024.2",
                           SoftwareLicenseKey = "SW-0500", SoftwareLicenseExpiration = null, SoftwareUsageData = 0, SoftwareCost = 499.00m },
            new Software { SoftwareName = "QuickBooks Online", SoftwareType = "Software", SoftwareVersion = "2024",
                           SoftwareLicenseKey = "SW-0600", SoftwareLicenseExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(2)), SoftwareUsageData = 0, SoftwareCost = 39.99m },
        };
        foreach (var s in softwareWanted) await UpsertSoftwareAsync(db, s, ct);
        await db.SaveChangesAsync(ct);

        var softwareByKey = await db.SoftwareAssets.AsNoTracking()
            .ToDictionaryAsync(s => (s.SoftwareName, s.SoftwareVersion), ct);


        var offices = new[]
        {
            new Office {OfficeName = "Main", Location = "Sacramento"},
            new Office { OfficeName = "Satellite One", Location = "Lathrop" },
            new Office { OfficeName = "Satellite Two", Location = "Yuba City" }
        };

        foreach (var o in offices) await UpsertOfficeAsync(db, o, ct);
        await db.SaveChangesAsync(ct);

        // ---------------- 5) Demo assignments (old mapping) ----------------
        // Build a map by employee number to look up UserID quickly
        usersByEmp = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.EmployeeNumber!, ct);

        async Task TryAssignHW(string emp, string serial)
        {
            if (!usersByEmp.TryGetValue(emp, out var user))
            {
                logger?.LogInformation("[Seeder] Skip HW: user emp={Emp} not found.", emp);
                return;
            }
            if (!hardwareBySerial.TryGetValue(serial, out var hw))
            {
                logger?.LogInformation("[Seeder] Skip HW: serial={Serial} not found.", serial);
                return;
            }

            // NEW: close any open assignment for this hardware that is not the intended user
            await CloseOpenHardwareAssignmentsExceptAsync(db, hw.HardwareID, user.UserID, ct);

            await EnsureAssignmentAsync(db, user.UserID, AssetKind.Hardware, hw.HardwareID, null,
                DateTime.UtcNow.AddDays(-3), ct);
        }

        async Task TryAssignSW(string emp, string name, string ver)
        {
            if (!usersByEmp.TryGetValue(emp, out var user))
            {
                logger?.LogInformation("[Seeder] Skip SW: user emp={Emp} not found.", emp);
                return;
            }
            if (!softwareByKey.TryGetValue((name, ver), out var sw))
            {
                logger?.LogInformation("[Seeder] Skip SW: {Name} {Ver} not found.", name, ver);
                return;
            }

            // NEW: close any open software assignment for this software that is not the intended user
            await CloseOpenSoftwareAssignmentsExceptAsync(db, sw.SoftwareID, user.UserID, ct);

            await EnsureAssignmentAsync(db, user.UserID, AssetKind.Software, null, sw.SoftwareID,
                DateTime.UtcNow.AddDays(-2), ct);
        }

        await TryAssignHW("28809", "LT-0020"); // John <- Lenovo ThinkPad E16
        await TryAssignHW("69444", "MN-0001"); // Jane <- Dell S2421NX
        await TryAssignHW("58344", "DT-0011"); // Randy <- Lenovo IdeaCentre 3
        await TryAssignSW("10971", "Microsoft 365 Business", "1.0"); // Robin <- M365
        await TryAssignHW("62241", "MN-0023"); // Sarah <- HP 527SH
        // DT-0075 remains Unassigned
        await TryAssignHW("90334", "LT-0005"); // Caitlin <- Galaxy Book4
        await TryAssignHW("27094", "HS-0015"); // Brian <- Zone Vibe 100
        // CC-0088 remains Unassigned
        await TryAssignHW("20983", "DT-0100"); // Maximillian <- Inspiron 3030
        await TryAssignHW("47283", "HS-0001"); // Emily <- Poly Voyager 4320
        await TryAssignHW("34532", "CC-0019"); // Bruce <- j5create 100W Super Charger
        await TryAssignHW("93232", "LT-0115"); // Kate <- Dell Inspiron 15

        await db.SaveChangesAsync(ct);

        logger?.LogInformation("[DBSeeder] Done. Roles:{Roles}, Users:{Users}, HW:{HW}, SW:{SW}, Offices:{OF}",
            await db.Roles.CountAsync(ct),
            await db.Users.CountAsync(ct),
            await db.HardwareAssets.CountAsync(ct),
            await db.SoftwareAssets.CountAsync(ct),
            await db.Offices.CountAsync(ct)
            );
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
        if (existing is null) await db.Roles.AddAsync(incoming, ct);
        else existing.Description = incoming.Description;
    }

    private static async Task UpsertUserAsync(AimsDbContext db, User incoming, CancellationToken ct)
    {
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
            if (existing.ExternalId == Guid.Empty)
                existing.ExternalId = incoming.ExternalId;

            existing.FullName = incoming.FullName;
            existing.EmployeeNumber = incoming.EmployeeNumber;
            existing.IsActive = incoming.IsActive;
            existing.RoleID = incoming.RoleID;
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
        if (existing is null) await db.HardwareAssets.AddAsync(incoming, ct);
        else
        {
            existing.AssetName = incoming.AssetName;
            existing.AssetType = incoming.AssetType;
            existing.Status = incoming.Status;
            existing.Manufacturer = incoming.Manufacturer;
            existing.Model = incoming.Model;
            existing.PurchaseDate = incoming.PurchaseDate;
            existing.WarrantyExpiration = incoming.WarrantyExpiration;
        }
    }
    private static async Task UpsertOfficeAsync(AimsDbContext db, Office incoming, CancellationToken ct)
    {
        var existing = await db.Offices.FirstOrDefaultAsync(o => o.OfficeID == incoming.OfficeID, ct);
        if (existing is null) await db.Offices.AddAsync(incoming, ct);
        else
        {
            existing.OfficeName = incoming.OfficeName;
            existing.Location = incoming.Location;
        }
    }

    private static async Task UpsertSoftwareAsync(AimsDbContext db, Software incoming, CancellationToken ct)
    {
        var existing = await db.SoftwareAssets
            .FirstOrDefaultAsync(s => s.SoftwareName == incoming.SoftwareName &&
                                      s.SoftwareVersion == incoming.SoftwareVersion, ct);

        if (existing is null) await db.SoftwareAssets.AddAsync(incoming, ct);
        else
        {
            existing.SoftwareType = incoming.SoftwareType;
            existing.SoftwareLicenseKey = incoming.SoftwareLicenseKey;
            existing.SoftwareLicenseExpiration = incoming.SoftwareLicenseExpiration;
            existing.SoftwareUsageData = incoming.SoftwareUsageData;
            existing.SoftwareCost = incoming.SoftwareCost;
        }
    }

    // close all open HW assignments except for a specific intended user
    private static async Task CloseOpenHardwareAssignmentsExceptAsync(
        AimsDbContext db, int hardwareId, int keepUserId, CancellationToken ct)
    {
        var open = await db.Assignments
            .Where(a => a.AssetKind == AssetKind.Hardware
                     && a.HardwareID == hardwareId
                     && a.UnassignedAtUtc == null
                     && a.UserID != keepUserId)
            .ToListAsync(ct);

        foreach (var a in open)
            a.UnassignedAtUtc = DateTime.UtcNow;
    }

    // close all open SW assignments except for a specific intended user
    private static async Task CloseOpenSoftwareAssignmentsExceptAsync(
        AimsDbContext db, int softwareId, int keepUserId, CancellationToken ct)
    {
        var open = await db.Assignments
            .Where(a => a.AssetKind == AssetKind.Software
                     && a.SoftwareID == softwareId
                     && a.UnassignedAtUtc == null
                     && a.UserID != keepUserId)
            .ToListAsync(ct);

        foreach (var a in open)
            a.UnassignedAtUtc = DateTime.UtcNow;
    }

    private static async Task EnsureAssignmentAsync(
        AimsDbContext db,
        int userId,
        AssetKind assetKind,    // Hardware or Software
        int? hardwareId,        // when Hardware
        int? softwareId,        // when Software
        DateTime assignedAtUtc,
        CancellationToken ct)
    {
        // Normalize: enforce exactly one side is set
        int? hardwareIdToAssign;
        int? softwareIdToAssign;

        if (assetKind == AssetKind.Hardware)
        {
            hardwareIdToAssign = hardwareId ?? throw new ArgumentNullException(nameof(hardwareId));
            softwareIdToAssign = null;
        }
        else if (assetKind == AssetKind.Software)
        {
            hardwareIdToAssign = null;
            softwareIdToAssign = softwareId ?? throw new ArgumentNullException(nameof(softwareId));
        }
        else
        {
            throw new InvalidOperationException("Unknown AssetKind for assignment seeding.");
        }

        // Avoid creating a second OPEN assignment for the same asset
        var assetHasOpen = await db.Assignments.AnyAsync(a =>
            a.AssetKind == assetKind &&
            a.UnassignedAtUtc == null &&
            ((assetKind == AssetKind.Hardware && a.HardwareID == hardwareIdToAssign) ||
             (assetKind == AssetKind.Software && a.SoftwareID == softwareIdToAssign)), ct);
        if (assetHasOpen)
        {
            // If the open assignment is to the intended user, we are done; if not, the callers
            // (TryAssignHW/TryAssignSW) closed the non-intended ones before calling us.
            var intendedOpen = await db.Assignments.AnyAsync(a =>
                a.AssetKind == assetKind &&
                a.UnassignedAtUtc == null &&
                a.UserID == userId &&
                ((assetKind == AssetKind.Hardware && a.HardwareID == hardwareIdToAssign) ||
                 (assetKind == AssetKind.Software && a.SoftwareID == softwareIdToAssign)), ct);
            if (intendedOpen) return;
            // Else fall through to idempotency check to avoid duplicate historical rows.
        }

        // Idempotency: same user + same concrete asset + same assigned date
        var exists = await db.Assignments.AnyAsync(a =>
            a.UserID == userId &&
            a.AssetKind == assetKind &&
            a.HardwareID == hardwareIdToAssign &&
            a.SoftwareID == softwareIdToAssign &&
            a.AssignedAtUtc.Date == assignedAtUtc.Date, ct);

        if (!exists)
        {
            db.Assignments.Add(new Assignment
            {
                UserID = userId,
                AssetKind = assetKind,
                HardwareID = hardwareIdToAssign,
                SoftwareID = softwareIdToAssign,
                AssignedAtUtc = assignedAtUtc,
                UnassignedAtUtc = null
            });
        }
    }
}
