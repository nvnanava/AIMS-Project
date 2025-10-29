using AIMS.Models;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Data;

public static class DbSeeder
{
    // Seeds Roles, Users (w/ supervisors), Offices, Thresholds, Hardware, Software,
    // Agreements, Reports, Assignments, and sample Audit events (with changes).
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

            // Tyler
            new User { FullName = "Tyler Burguillos", Email = "tnburg@pacbell.net", EmployeeNumber = "80003",
                       IsActive = true, RoleID = roleByName["Supervisor"].RoleID, ExternalId = UId("80003","tnburg@pacbell.net") },
        };
        foreach (var u in usersWanted) await UpsertUserAsync(db, u, ct);
        await db.SaveChangesAsync(ct);

        // Supervisor chain
        var usersByEmp = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.EmployeeNumber!, ct);
        int johnId = usersByEmp["28809"].UserID;
        string[] reportsOfJohn = { "69444", "58344", "10971", "47283", "34532" };
        foreach (var emp in reportsOfJohn)
        {
            if (usersByEmp.TryGetValue(emp, out var user))
                await EnsureSupervisorAsync(db, user, johnId, ct);
        }
        await db.SaveChangesAsync(ct);

        int tylerId = usersByEmp["80003"].UserID;
        string[] reportsOfTyler = { "34532", "62241" }; // Bruce, Sarah
        foreach (var emp in reportsOfTyler)
        {
            if (usersByEmp.TryGetValue(emp, out var user))
                await EnsureSupervisorAsync(db, user, tylerId, ct);
        }
        await db.SaveChangesAsync(ct);

        // ---------------- 3) Offices ----------------
        var officesWanted = new[]
        {
            new Office { OfficeName = "HQ - Sacramento", Location = "915 L St, Sacramento, CA" },
            new Office { OfficeName = "Warehouse - West", Location = "501 River Way, West Sac, CA" },
            new Office { OfficeName = "Remote", Location = "Distributed" },
        };
        foreach (var o in officesWanted) await UpsertOfficeAsync(db, o, ct);
        await db.SaveChangesAsync(ct);
        var officeByName = await db.Offices.AsNoTracking().ToDictionaryAsync(o => o.OfficeName, ct);

        // ---------------- 4) Thresholds ----------------
        var thresholdsWanted = new[]
        {
            new Threshold { AssetType = "Laptop",         ThresholdValue = 10 },
            new Threshold { AssetType = "Desktop",        ThresholdValue = 5  },
            new Threshold { AssetType = "Monitor",        ThresholdValue = 12 },
            new Threshold { AssetType = "Headset",        ThresholdValue = 15 },
            new Threshold { AssetType = "Charging Cable", ThresholdValue = 25 },
            new Threshold { AssetType = "Software",       ThresholdValue = 100 }
        };
        foreach (var t in thresholdsWanted) await UpsertThresholdAsync(db, t, ct);
        await db.SaveChangesAsync(ct);

        // ---------------- 5) Hardware ----------------
        var hardwareWanted = new[]
        {
            // Laptops
            new Hardware { AssetName="Lenovo ThinkPad E16", AssetType="Laptop", Status="Assigned", Manufacturer="Lenovo", Model="E16", SerialNumber="LT-0020",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) },
            new Hardware { AssetName="Samsung Galaxy Book4", AssetType="Laptop", Status="Damaged", Manufacturer="Samsung", Model="Book4", SerialNumber="LT-0005",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-8)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(10)) },
            new Hardware { AssetName="Dell Inspiron 15", AssetType="Laptop", Status="Assigned", Manufacturer="Dell", Model="Inspiron 15", SerialNumber="LT-0115",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-10)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(2)) },

            // Monitors
            new Hardware { AssetName="Dell S2421NX", AssetType ="Monitor", Status="Assigned", Manufacturer="Dell", Model ="S2421NX", SerialNumber="MN-0001",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) },
            new Hardware { AssetName="HP 527SH", AssetType="Monitor", Status="Assigned", Manufacturer="HP", Model="527SH", SerialNumber="MN-0023",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-4)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(2)) },

            // Desktops
            new Hardware { AssetName="Lenovo IdeaCentre 3", AssetType="Desktop", Status="Damaged", Manufacturer="Lenovo", Model="IdeaCentre 3", SerialNumber="DT-0011",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-12)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)) },
            new Hardware { AssetName="HP Pavillion TP01-2234", AssetType="Desktop", Status="Available", Manufacturer="HP", Model="TP01-2234", SerialNumber="DT-0075",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-2)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(3)) },
            new Hardware { AssetName="Dell Inspiron 3030", AssetType="Desktop", Status="Assigned", Manufacturer="Dell", Model="Inspiron 3030", SerialNumber="DT-0100",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-7)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) },

            // Headsets
            new Hardware { AssetName="Logitech Zone 300", AssetType="Headset", Status="Available", Manufacturer ="Logitech", Model="Zone 300", SerialNumber="HS-0080",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-5)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) },
            new Hardware { AssetName="Logitech Zone Vibe 100", AssetType ="Headset", Status ="In Repair", Manufacturer ="Logitech", Model="Zone Vibe 100", SerialNumber="HS-0015",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(2)) },
            new Hardware { AssetName="Poly Voyager 4320", AssetType="Headset", Status="In Repair", Manufacturer ="Poly", Model="Voyager 4320", SerialNumber="HS-0001",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-3)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(2)) },

            // Cables
            new Hardware { AssetName="Belkin BoostCharge 3.3ft USB-C", AssetType="Charging Cable", Status="Available", Manufacturer="Belkin", Model="BoostCharge", SerialNumber="CC-0088",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) },
            new Hardware { AssetName="j5create 100W Super Charger", AssetType="Charging Cable", Status="Assigned", Manufacturer="j5create", Model ="100W Super", SerialNumber="CC-0019",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-2)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) },
        };
        foreach (var h in hardwareWanted) await UpsertHardwareAsync(db, h, ct);
        await db.SaveChangesAsync(ct);

        var hardwareBySerial = await db.HardwareAssets.AsNoTracking()
            .ToDictionaryAsync(h => h.SerialNumber, ct);

        // Reset HW assignment state to match seeded Status
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

        // ---------------- 6) Software ----------------
        var softwareWanted = new[]
        {
            new Software { SoftwareName = "Microsoft 365 Business", SoftwareType = "Software", SoftwareVersion = "1.0",
                           SoftwareLicenseKey = "SW-0100", SoftwareLicenseExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                           SoftwareUsageData = 0, SoftwareCost = 12.99m, LicenseTotalSeats = 200, LicenseSeatsUsed = 0 },

            new Software { SoftwareName = "Adobe Photoshop", SoftwareType = "Software", SoftwareVersion = "2024",
                           SoftwareLicenseKey = "SW-0200", SoftwareLicenseExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                           SoftwareUsageData = 0, SoftwareCost = 20.99m, LicenseTotalSeats = 50, LicenseSeatsUsed = 0 },

            new Software { SoftwareName = "Slack", SoftwareType = "Software", SoftwareVersion = "5.3",
                           SoftwareLicenseKey = "SW-0300", SoftwareLicenseExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
                           SoftwareUsageData = 0, SoftwareCost = 8.00m, LicenseTotalSeats = 500, LicenseSeatsUsed = 0 },

            new Software { SoftwareName = "Zoom Pro", SoftwareType = "Software", SoftwareVersion = "7.1",
                           SoftwareLicenseKey = "SW-0400", SoftwareLicenseExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
                           SoftwareUsageData = 0, SoftwareCost = 15.99m, LicenseTotalSeats = 120, LicenseSeatsUsed = 0 },

            new Software { SoftwareName = "IntelliJ IDEA", SoftwareType = "Software", SoftwareVersion = "2024.2",
                           SoftwareLicenseKey = "SW-0500", SoftwareLicenseExpiration = null,
                           SoftwareUsageData = 0, SoftwareCost = 499.00m, LicenseTotalSeats = 20, LicenseSeatsUsed = 0 },

            new Software { SoftwareName = "QuickBooks Online", SoftwareType = "Software", SoftwareVersion = "2024",
                           SoftwareLicenseKey = "SW-0600", SoftwareLicenseExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(2)),
                           SoftwareUsageData = 0, SoftwareCost = 39.99m, LicenseTotalSeats = 40, LicenseSeatsUsed = 0 },
        };
        foreach (var s in softwareWanted) await UpsertSoftwareAsync(db, s, ct);
        await db.SaveChangesAsync(ct);

        var softwareByKey = await db.SoftwareAssets.AsNoTracking()
            .ToDictionaryAsync(s => (s.SoftwareName, s.SoftwareVersion), ct);

        // ---------------- 7) Agreements (blob-backed) ----------------
        var agreementsWanted = new List<Agreement>
        {
            // HW agreement (warranty PDF)
            new Agreement
            {
                FileUri   = "blob://agreements/hardware/LT-0020-warranty.pdf",
                AssetKind = AssetKind.Hardware,
                HardwareID = hardwareBySerial["LT-0020"].HardwareID,
                SoftwareID = null,
                DateAdded = DateTime.UtcNow.AddDays(-25)
            },
            // SW agreement (license EULA)
            new Agreement
            {
                FileUri   = "blob://agreements/software/SW-0100-eula.pdf",
                AssetKind = AssetKind.Software,
                SoftwareID = softwareByKey[("Microsoft 365 Business","1.0")].SoftwareID,
                HardwareID = null,
                DateAdded = DateTime.UtcNow.AddDays(-20)
            }
        };
        foreach (var a in agreementsWanted) await UpsertAgreementAsync(db, a, ct);
        await db.SaveChangesAsync(ct);

        // ---------------- 8) Reports (blob-backed) ----------------
        var reportsWanted = new List<Report>
        {
            new Report
            {
                Name = "Weekly Asset Count",
                Type = "Inventory",
                Description = "Hardware & Software totals by type",
               // BlobUri = "blob://reports/weekly-asset-count.csv",
                GeneratedByUserID = usersByEmp["28809"].UserID, // John
                GeneratedForOfficeID = officeByName["HQ - Sacramento"].OfficeID,
                DateCreated = DateTime.UtcNow.AddDays(-7)
            },
            new Report
            {
                Name = "License Usage Summary",
                Type = "Software",
                Description = "Seats used vs total by product",
              //  BlobUri = "blob://reports/license-usage.csv",
                GeneratedByUserID = usersByEmp["47283"].UserID, // Emily
                GeneratedForOfficeID = officeByName["Remote"].OfficeID,
                DateCreated = DateTime.UtcNow.AddDays(-3)
            }
        };
        foreach (var r in reportsWanted) await UpsertReportAsync(db, r, ct);
        await db.SaveChangesAsync(ct);

        // ---------------- 9) Assignments ----------------
        usersByEmp = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.EmployeeNumber!, ct);

        async Task TryAssignHW(string emp, string serial, DateTime whenUtc)
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

            // close any open assignment not for intended user
            await CloseOpenHardwareAssignmentsExceptAsync(db, hw.HardwareID, user.UserID, ct);

            await EnsureAssignmentAsync(db, user.UserID, AssetKind.Hardware, hw.HardwareID, null, whenUtc, ct);
        }

        async Task TryAssignSW(string emp, string name, string ver, DateTime whenUtc)
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

            await CloseOpenSoftwareAssignmentsExceptAsync(db, sw.SoftwareID, user.UserID, ct);

            await EnsureAssignmentAsync(db, user.UserID, AssetKind.Software, null, sw.SoftwareID, whenUtc, ct);

            // bump license seat count (idempotent)
            var tracked = await db.SoftwareAssets.FirstAsync(s => s.SoftwareID == sw.SoftwareID, ct);
            if (tracked.LicenseSeatsUsed < tracked.LicenseTotalSeats)
                tracked.LicenseSeatsUsed = Math.Max(tracked.LicenseSeatsUsed, 1);
        }

        var now = DateTime.UtcNow;
        await TryAssignHW("28809", "LT-0020", now.AddDays(-12)); // John <- Lenovo ThinkPad E16
        await TryAssignHW("69444", "MN-0001", now.AddDays(-11)); // Jane <- Dell S2421NX
        await TryAssignHW("58344", "DT-0011", now.AddDays(-10)); // Randy <- IdeaCentre 3
        await TryAssignSW("10971", "Microsoft 365 Business", "1.0", now.AddDays(-9)); // Robin <- M365
        await TryAssignHW("62241", "MN-0023", now.AddDays(-8)); // Sarah <- HP 527SH
        await TryAssignHW("90334", "LT-0005", now.AddDays(-7)); // Caitlin <- Galaxy Book4
        await TryAssignHW("27094", "HS-0015", now.AddDays(-6)); // Brian <- Zone Vibe 100
        await TryAssignHW("20983", "DT-0100", now.AddDays(-5)); // Maximillian <- Inspiron 3030
        await TryAssignHW("47283", "HS-0001", now.AddDays(-4)); // Emily <- Poly Voyager 4320
        await TryAssignHW("34532", "CC-0019", now.AddDays(-3)); // Bruce <- j5create 100W
        await TryAssignHW("93232", "LT-0115", now.AddDays(-2)); // Kate <- Inspiron 15

        await db.SaveChangesAsync(ct);

        // ---------------- 10) Audit Logs (+ Changes) ----------------
        // Create 15 example events (Assign / Update / Create / Unassign / Archive / Install / Renew / Expire / Remove)
        // with proper XOR targeting. Use deterministic ExternalId keys for idempotency.
        var auditEvents = new List<(AuditLog log, IList<AuditLogChange> changes)>
        {
            // 1) Hardware assignment (John -> LT-0020)
            (
                new AuditLog {
                    ExternalId   = FromString("audit:assign:LT-0020:28809"),
                    TimestampUtc = now.AddDays(-12).AddMinutes(3),
                    UserID       = usersByEmp["28809"].UserID, // John did it
                    Action       = AuditLogAction.Assign,
                    Description  = "Assigned Lenovo ThinkPad E16 to John Smith",
                    AssetKind    = AssetKind.Hardware,
                    HardwareID   = hardwareBySerial["LT-0020"].HardwareID,
                    SoftwareID   = null
                },
                new List<AuditLogChange> {
                    new AuditLogChange { Field = "Status", OldValue = "Available", NewValue = "Assigned" },
                    new AuditLogChange { Field = "AssignedTo", OldValue = null, NewValue = "John Smith (28809)" }
                }
            ),

            // 2) Software seat allocation (Robin -> M365 1.0)
            (
                new AuditLog {
                    ExternalId   = FromString("audit:assign:SW-0100:10971"),
                    TimestampUtc = now.AddDays(-9).AddMinutes(10),
                    UserID       = usersByEmp["10971"].UserID, // Robin
                    Action       = AuditLogAction.Assign,
                    Description  = "Assigned Microsoft 365 Business seat to Robin Williams",
                    AssetKind    = AssetKind.Software,
                    SoftwareID   = softwareByKey[("Microsoft 365 Business","1.0")].SoftwareID,
                    HardwareID   = null
                },
                new List<AuditLogChange> {
                    new AuditLogChange { Field = "LicenseSeatsUsed", OldValue = "0", NewValue = "1" }
                }
            ),

            // 3) Hardware status update (repair HS-0015)
            (
                new AuditLog {
                    ExternalId   = FromString("audit:update:HS-0015:repair"),
                    TimestampUtc = now.AddDays(-5).AddMinutes(20),
                    UserID       = usersByEmp["27094"].UserID, // Brian
                    Action       = AuditLogAction.Update,
                    Description  = "Set Logitech Zone Vibe 100 status to In Repair",
                    AssetKind    = AssetKind.Hardware,
                    HardwareID   = hardwareBySerial["HS-0015"].HardwareID,
                    SoftwareID   = null
                },
                new List<AuditLogChange> {
                    new AuditLogChange { Field = "Status", OldValue = "Assigned", NewValue = "In Repair" }
                }
            ),

            // 4) Report generated (anchor to M365)
            (
                new AuditLog {
                    ExternalId   = FromString("audit:create:report:license-usage"),
                    TimestampUtc = now.AddDays(-3).AddMinutes(5),
                    UserID       = usersByEmp["47283"].UserID, // Emily
                    Action       = AuditLogAction.Create,
                    Description  = "Generated License Usage Summary report",
                    AssetKind    = AssetKind.Software,
                    SoftwareID   = softwareByKey[("Microsoft 365 Business","1.0")].SoftwareID,
                    HardwareID   = null
                },
                new List<AuditLogChange>()
            ),

            // 5) Assign monitor MN-0001 to Jane
            (
                new AuditLog {
                    ExternalId   = FromString("audit:assign:MN-0001:69444"),
                    TimestampUtc = now.AddDays(-11).AddMinutes(7),
                    UserID       = usersByEmp["69444"].UserID, // Jane
                    Action       = AuditLogAction.Assign,
                    Description  = "Assigned Dell S2421NX to Jane Doe",
                    AssetKind    = AssetKind.Hardware,
                    HardwareID   = hardwareBySerial["MN-0001"].HardwareID,
                    SoftwareID   = null
                },
                new List<AuditLogChange> {
                    new AuditLogChange { Field = "AssignedTo", OldValue = "Unassigned", NewValue = "Jane Doe (69444)" },
                    new AuditLogChange { Field = "Status", OldValue = "Available", NewValue = "Assigned" }
                }
            ),

            // 6) Assign desktop DT-0011 to Randy
            (
                new AuditLog {
                    ExternalId   = FromString("audit:assign:DT-0011:58344"),
                    TimestampUtc = now.AddDays(-10).AddMinutes(12),
                    UserID       = usersByEmp["58344"].UserID, // Randy
                    Action       = AuditLogAction.Assign,
                    Description  = "Assigned Lenovo IdeaCentre 3 to Randy Orton",
                    AssetKind    = AssetKind.Hardware,
                    HardwareID   = hardwareBySerial["DT-0011"].HardwareID,
                    SoftwareID   = null
                },
                new List<AuditLogChange> {
                    new AuditLogChange { Field = "AssignedTo", OldValue = "Unassigned", NewValue = "Randy Orton (58344)" },
                    new AuditLogChange { Field = "Status", OldValue = "Available", NewValue = "Assigned" }
                }
            ),

            // 7) Unassign charger CC-0019 from Bruce
            (
                new AuditLog {
                    ExternalId   = FromString("audit:unassign:CC-0019:34532"),
                    TimestampUtc = now.AddDays(-3).AddMinutes(30),
                    UserID       = usersByEmp["93232"].UserID, // Kate performed unassign
                    Action       = AuditLogAction.Unassign,
                    Description  = "Unassigned j5create 100W Super Charger from Bruce Wayne",
                    AssetKind    = AssetKind.Hardware,
                    HardwareID   = hardwareBySerial["CC-0019"].HardwareID,
                    SoftwareID   = null
                },
                new List<AuditLogChange> {
                    new AuditLogChange { Field = "AssignedTo", OldValue = "Bruce Wayne (34532)", NewValue = "Unassigned" },
                    new AuditLogChange { Field = "Status", OldValue = "Assigned", NewValue = "Available" }
                }
            ),

            // 8) Extend warranty LT-0115
            (
                new AuditLog {
                    ExternalId   = FromString("audit:update:LT-0115:warranty:extended"),
                    TimestampUtc = now.AddDays(-2).AddMinutes(15),
                    UserID       = usersByEmp["93232"].UserID, // Kate
                    Action       = AuditLogAction.Update,
                    Description  = "Extended warranty for Dell Inspiron 15 by 1 year",
                    AssetKind    = AssetKind.Hardware,
                    HardwareID   = hardwareBySerial["LT-0115"].HardwareID,
                    SoftwareID   = null
                },
                new List<AuditLogChange> {
                    new AuditLogChange { Field = "WarrantyExpiration", OldValue = "Auto", NewValue = "Auto+1y" }
                }
            ),

            // 9) Update MN-0023 notes
            (
                new AuditLog {
                    ExternalId   = FromString("audit:update:MN-0023:notes"),
                    TimestampUtc = now.AddDays(-8).AddMinutes(40),
                    UserID       = usersByEmp["62241"].UserID, // Sarah
                    Action       = AuditLogAction.Update,
                    Description  = "Updated ergonomic notes for HP 527SH monitor",
                    AssetKind    = AssetKind.Hardware,
                    HardwareID   = hardwareBySerial["MN-0023"].HardwareID,
                    SoftwareID   = null
                },
                new List<AuditLogChange> {
                    new AuditLogChange { Field = "Comment", OldValue = "", NewValue = "Assigned with dual-arm mount." }
                }
            ),

            // 10) Install IntelliJ 2024.2 for Kate
            (
                new AuditLog {
                    ExternalId   = FromString("audit:install:IntelliJ:2024.2:93232"),
                    TimestampUtc = now.AddDays(-4).AddMinutes(5),
                    UserID       = usersByEmp["93232"].UserID,
                    Action       = AuditLogAction.Install,
                    Description  = "Installed IntelliJ IDEA (2024.2) for Kate Rosenberg",
                    AssetKind    = AssetKind.Software,
                    SoftwareID   = softwareByKey[("IntelliJ IDEA","2024.2")].SoftwareID,
                    HardwareID   = null
                },
                new List<AuditLogChange> {
                    new AuditLogChange { Field = "SeatsUsed", OldValue = "0", NewValue = "1" }
                }
            ),

            // 11) Renew Zoom Pro 7.1
            (
                new AuditLog {
                    ExternalId   = FromString("audit:renew:ZoomPro:7.1"),
                    TimestampUtc = now.AddDays(-3).AddMinutes(50),
                    UserID       = usersByEmp["20983"].UserID, // Max
                    Action       = AuditLogAction.Renew,
                    Description  = "Renewed Zoom Pro (7.1) license for 12 months",
                    AssetKind    = AssetKind.Software,
                    SoftwareID   = softwareByKey[("Zoom Pro","7.1")].SoftwareID,
                    HardwareID   = null
                },
                new List<AuditLogChange> {
                    new AuditLogChange { Field = "LicenseExpiration", OldValue = "Auto", NewValue = "Auto+12m" }
                }
            ),

            // 12) Expire Slack 5.3
            (
                new AuditLog {
                    ExternalId   = FromString("audit:expire:Slack:5.3"),
                    TimestampUtc = now.AddDays(-2).AddMinutes(42),
                    UserID       = usersByEmp["62241"].UserID, // Sarah
                    Action       = AuditLogAction.Expired,
                    Description  = "Slack (5.3) license reached end of term",
                    AssetKind    = AssetKind.Software,
                    SoftwareID   = softwareByKey[("Slack","5.3")].SoftwareID,
                    HardwareID   = null
                },
                new List<AuditLogChange> {
                    new AuditLogChange { Field = "LicenseExpiration", OldValue = "Auto+6m", NewValue = "Expired" }
                }
            ),

            // 13) Unassign M365 from Robin
            (
                new AuditLog {
                    ExternalId   = FromString("audit:unassign:M365:1.0:10971"),
                    TimestampUtc = now.AddDays(-2).AddMinutes(55),
                    UserID       = usersByEmp["47283"].UserID, // Emily
                    Action       = AuditLogAction.Unassign,
                    Description  = "Unassigned Microsoft 365 Business (1.0) from Robin Williams",
                    AssetKind    = AssetKind.Software,
                    SoftwareID   = softwareByKey[("Microsoft 365 Business","1.0")].SoftwareID,
                    HardwareID   = null
                },
                new List<AuditLogChange> {
                    new AuditLogChange { Field = "SeatsUsed", OldValue = "1", NewValue = "0" }
                }
            ),

            // 14) Install QuickBooks 2024 for John
            (
                new AuditLog {
                    ExternalId   = FromString("audit:install:QuickBooks:2024:28809"),
                    TimestampUtc = now.AddDays(-1).AddMinutes(22),
                    UserID       = usersByEmp["28809"].UserID, // John
                    Action       = AuditLogAction.Install,
                    Description  = "Installed QuickBooks Online (2024) for John Smith",
                    AssetKind    = AssetKind.Software,
                    SoftwareID   = softwareByKey[("QuickBooks Online","2024")].SoftwareID,
                    HardwareID   = null
                },
                new List<AuditLogChange> {
                    new AuditLogChange { Field = "SeatsUsed", OldValue = "0", NewValue = "1" }
                }
            ),

            // 15) Remove Photoshop 2024 from Emily
            (
                new AuditLog {
                    ExternalId   = FromString("audit:remove:Photoshop:2024:47283"),
                    TimestampUtc = now.AddDays(-1).AddMinutes(40),
                    UserID       = usersByEmp["47283"].UserID, // Emily
                    Action       = AuditLogAction.Unassign,
                    Description  = "Removed Adobe Photoshop (2024) license from Emily Carter",
                    AssetKind    = AssetKind.Software,
                    SoftwareID   = softwareByKey[("Adobe Photoshop","2024")].SoftwareID,
                    HardwareID   = null
                },
                new List<AuditLogChange> {
                    new AuditLogChange { Field = "SeatsUsed", OldValue = "1", NewValue = "0" }
                }
            )
        };

        foreach (var (log, changes) in auditEvents)
            await UpsertAuditWithChangesAsync(db, log, changes, ct);

        await db.SaveChangesAsync(ct);

        logger?.LogInformation(
            "[DBSeeder] Done. Roles:{Roles}, Users:{Users}, Offices:{Offices}, Thresholds:{Th}, HW:{HW}, SW:{SW}, Agreements:{Ag}, Reports:{Rp}, Assignments:{Asg}, Audit:{Au}, Changes:{Ch}",
            await db.Roles.CountAsync(ct),
            await db.Users.CountAsync(ct),
            await db.Offices.CountAsync(ct),
            await db.Thresholds.CountAsync(ct),
            await db.HardwareAssets.CountAsync(ct),
            await db.SoftwareAssets.CountAsync(ct),
            await db.Agreements.CountAsync(ct),
            await db.Reports.CountAsync(ct),
            await db.Assignments.CountAsync(ct),
            await db.AuditLogs.CountAsync(ct),
            await db.AuditLogChanges.CountAsync(ct));
    }

    // ---------- helpers ----------
    private static Guid FromString(string input)
    {
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        var hash = sha1.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        return new Guid(bytes);
    }

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

    private static async Task UpsertOfficeAsync(AimsDbContext db, Office incoming, CancellationToken ct)
    {
        var existing = await db.Offices.FirstOrDefaultAsync(o => o.OfficeName == incoming.OfficeName, ct);
        if (existing is null) await db.Offices.AddAsync(incoming, ct);
        else existing.Location = incoming.Location;
    }

    private static async Task UpsertThresholdAsync(AimsDbContext db, Threshold incoming, CancellationToken ct)
    {
        var existing = await db.Thresholds.FirstOrDefaultAsync(t => t.AssetType == incoming.AssetType, ct);
        if (existing is null) await db.Thresholds.AddAsync(incoming, ct);
        else existing.ThresholdValue = incoming.ThresholdValue;
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
            existing.LicenseTotalSeats = incoming.LicenseTotalSeats;
            existing.LicenseSeatsUsed = Math.Max(existing.LicenseSeatsUsed, incoming.LicenseSeatsUsed);
        }
    }

    private static async Task UpsertAgreementAsync(AimsDbContext db, Agreement incoming, CancellationToken ct)
    {
        // Idempotency by (FileUri)
        var existing = await db.Agreements.FirstOrDefaultAsync(a => a.FileUri == incoming.FileUri, ct);
        if (existing is null)
        {
            // Enforce XOR and AssetKind correctness
            if (incoming.AssetKind == AssetKind.Hardware)
            {
                incoming.SoftwareID = null;
                if (incoming.HardwareID is null) throw new InvalidOperationException("Agreement requires HardwareID for AssetKind.Hardware.");
            }
            else
            {
                incoming.HardwareID = null;
                if (incoming.SoftwareID is null) throw new InvalidOperationException("Agreement requires SoftwareID for AssetKind.Software.");
            }
            await db.Agreements.AddAsync(incoming, ct);
        }
        else
        {
            existing.DateAdded = incoming.DateAdded;
            existing.AssetKind = incoming.AssetKind;
            existing.HardwareID = incoming.AssetKind == AssetKind.Hardware ? incoming.HardwareID : null;
            existing.SoftwareID = incoming.AssetKind == AssetKind.Software ? incoming.SoftwareID : null;
        }
    }

    private static async Task UpsertReportAsync(AimsDbContext db, Report incoming, CancellationToken ct)
    {
        // Idempotency by Name + Type
        var existing = await db.Reports.FirstOrDefaultAsync(r => r.Name == incoming.Name && r.Type == incoming.Type, ct);
        if (existing is null) await db.Reports.AddAsync(incoming, ct);
        else
        {
            existing.Description = incoming.Description;
           // existing.BlobUri = incoming.BlobUri;
            existing.GeneratedByUserID = incoming.GeneratedByUserID;
            existing.GeneratedForOfficeID = incoming.GeneratedForOfficeID;
            existing.DateCreated = incoming.DateCreated;
        }
    }

    private static async Task UpsertAuditWithChangesAsync(
        AimsDbContext db, AuditLog incoming, IList<AuditLogChange> changes, CancellationToken ct)
    {
        // Idempotency by ExternalId (deterministic in this seeder)
        var existing = await db.AuditLogs
            .Include(a => a.Changes)
            .FirstOrDefaultAsync(a => a.ExternalId == incoming.ExternalId, ct);

        if (existing is null)
        {
            // Enforce XOR and AssetKind correctness
            if (incoming.AssetKind == AssetKind.Hardware)
            {
                incoming.SoftwareID = null;
                if (incoming.HardwareID is null) throw new InvalidOperationException("AuditLog requires HardwareID for AssetKind.Hardware.");
            }
            else
            {
                incoming.HardwareID = null;
                if (incoming.SoftwareID is null) throw new InvalidOperationException("AuditLog requires SoftwareID for AssetKind.Software.");
            }

            incoming.Changes = new List<AuditLogChange>(changes ?? Array.Empty<AuditLogChange>());
            await db.AuditLogs.AddAsync(incoming, ct);
        }
        else
        {
            // Update summary fields; keep FK target + kind
            existing.TimestampUtc = incoming.TimestampUtc;
            existing.UserID = incoming.UserID;
            existing.Action = incoming.Action;
            existing.Description = incoming.Description;

            // Reconcile changes by Field (simple upsert)
            var byField = existing.Changes.ToDictionary(c => c.Field, StringComparer.OrdinalIgnoreCase);
            foreach (var c in (changes ?? Array.Empty<AuditLogChange>()))
            {
                if (byField.TryGetValue(c.Field, out var ex))
                {
                    ex.OldValue = c.OldValue;
                    ex.NewValue = c.NewValue;
                }
                else
                {
                    existing.Changes.Add(new AuditLogChange { Field = c.Field, OldValue = c.OldValue, NewValue = c.NewValue });
                }
            }
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
            var intendedOpen = await db.Assignments.AnyAsync(a =>
                a.AssetKind == assetKind &&
                a.UnassignedAtUtc == null &&
                a.UserID == userId &&
                ((assetKind == AssetKind.Hardware && a.HardwareID == hardwareIdToAssign) ||
                 (assetKind == AssetKind.Software && a.SoftwareID == softwareIdToAssign)), ct);
            if (intendedOpen) return;
        }

        // Idempotency: same user + same asset + same assigned date
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
