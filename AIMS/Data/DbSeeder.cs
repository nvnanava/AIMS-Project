using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIMS.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIMS.Data;

public static class DbSeeder
{
    // Seeds Roles, Users (with supervisor chain), Hardware, Software, and assignments.
    // Idempotent & deterministic (ExternalId from EmployeeNumber). Skips in Prod unless allowed.
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

        // 1) Roles (natural key = RoleName)
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
        string EmailFromName(string name) => $"{Slug(name)}@aims.local";

        // --- Dummy users (Supervisor tree + Admin/IT) ---
        var dummyUsers = new List<Dictionary<string, string>> {
            new() {{"Name","John Smith"},{"Role","Supervisor"},{"ID","28809"}},
            new() {{"Name","Jane Doe"},{"Role","IT Help Desk"},{"ID","69444"},{"Supervisor","28809"}},
            new() {{"Name","Randy Orton"},{"Role","IT Help Desk"},{"ID","58344"},{"Supervisor","28809"}},
            new() {{"Name","Robin Williams"},{"Role","IT Help Desk"},{"ID","10971"},{"Supervisor","28809"}},
            new() {{"Name","Sarah Johnson"},{"Role","Supervisor"},{"ID","62241"}},
            new() {{"Name","Caitlin Clark"},{"Role","Supervisor"},{"ID","90334"}},
            new() {{"Name","Brian Regan"},{"Role","Supervisor"},{"ID","27094"}},
            new() {{"Name","Maximillian Brandt"},{"Role","Admin"},{"ID","20983"}},
            new() {{"Name","Kate Rosenberg"},{"Role","Admin"},{"ID","93232"}},
            new() {{"Name","Emily Carter"},{"Role","IT Help Desk"},{"ID","47283"},{"Supervisor","28809"}},
            new() {{"Name","Bruce Wayne"},{"Role","IT Help Desk"},{"ID","34532"},{"Supervisor","28809"}},
        };

        foreach (var u in dummyUsers)
        {
            var name  = u["Name"];
            var emp   = u["ID"];
            var role  = u["Role"];
            var email = EmailFromName(name);

            await UpsertUserAsync(db, new User
            {
                FullName       = name,
                Email          = email,
                EmployeeNumber = emp,
                IsActive       = true,
                RoleID         = roleByName[role].RoleID,
                ExternalId     = UId(emp, email)
            }, ct);
        }
        await db.SaveChangesAsync(ct);

        // supervisors
        var usersByEmp = await db.Users.ToDictionaryAsync(x => x.EmployeeNumber, ct);
        foreach (var u in dummyUsers.Where(x => x.ContainsKey("Supervisor")))
        {
            var emp = u["ID"];
            var sup = u["Supervisor"];
            if (usersByEmp.TryGetValue(emp, out var child) && usersByEmp.TryGetValue(sup, out var boss))
            {
                await EnsureSupervisorAsync(db, child, boss.UserID, ct);
            }
        }
        await db.SaveChangesAsync(ct);

        // --- Dummy assets table (hardware/software mix) ---
        var tableData = new List<Dictionary<string, string>> {
            new() { {"Asset Name","Lenovo ThinkPad E16"},{"Type","Laptop"},{"Tag #","LT-0020"},{"Assigned To","John Smith (28809)"},{"Status","Assigned"} },
            new() { {"Asset Name","Dell S2421NX"},{"Type","Monitor"},{"Tag #","MN-0001"},{"Assigned To","Jane Doe (69444)"},{"Status","Assigned"} },
            new() { {"Asset Name","Logitech Zone 300"},{"Type","Headset"},{"Tag #","HS-0080"},{"Assigned To","Unassigned"},{"Status","Available"} },
            new() { {"Asset Name","Lenovo IdeaCentre 3"},{"Type","Desktop"},{"Tag #","DT-0011"},{"Assigned To","Randy Orton (58344)"},{"Status","Damaged"} },
            new() { {"Asset Name","Microsoft 365 Business"},{"Type","Software"},{"Tag #","SW-0100"},{"Assigned To","Robin Williams (10971)"},{"Status","Assigned"} },
            new() { {"Asset Name","HP 527SH"},{"Type","Monitor"},{"Tag #","MN-0023"},{"Assigned To","Sarah Johnson (62241)"},{"Status","In Repair"} },
            new() { {"Asset Name","HP Pavillion TP01-2234"},{"Type","Desktop"},{"Tag #","DT-0075"},{"Assigned To","Unassigned"},{"Status","Available"} },
            new() { {"Asset Name","Samsung Galaxy Book4"},{"Type","Laptop"},{"Tag #","LT-0005"},{"Assigned To","Caitlin Clark (90334)"},{"Status","Damaged"} },
            new() { {"Asset Name","Logitech Zone Vibe 100"},{"Type","Headset"},{"Tag #","HS-0015"},{"Assigned To","Brian Regan (27094)"},{"Status","In Repair"} },
            new() { {"Asset Name","Belkin BoostCharge 3.3ft USB-C"},{"Type","Charging Cable"},{"Tag #","CC-0088"},{"Assigned To","Unassigned"},{"Status","Available"} },
            new() { {"Asset Name","Dell Inspiron 3030"},{"Type","Desktop"},{"Tag #","DT-0100"},{"Assigned To","Maximillian Brandt (20983)"},{"Status","Assigned"} },
            new() { {"Asset Name","Poly Voyager 4320"},{"Type","Headset"},{"Tag #","HS-0001"},{"Assigned To","Emily Carter (47283)"},{"Status","In Repair"} },
            new() { {"Asset Name","j5create 100W Super Charger"},{"Type","Charging Cable"},{"Tag #","CC-0019"},{"Assigned To","Bruce Wayne (34532)"},{"Status","Damaged"} },
            new() { {"Asset Name","Dell Inspiron 15"},{"Type","Laptop"},{"Tag #","LT-0115"},{"Assigned To","Kate Rosenberg (93232)"},{"Status","Assigned"} },
        };

        var hardwareTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var softwareTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        DateOnly defPurchase = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-3));
        DateOnly defWarranty = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1));

        foreach (var row in tableData)
        {
            var name   = row["Asset Name"];
            var type   = row["Type"];
            var tag    = row["Tag #"];
            var status = row["Status"];

            if (string.Equals(type, "Software", StringComparison.OrdinalIgnoreCase))
            {
                // Treat Tag# as license key; default version "1.0"
                var sw = new Software
                {
                    SoftwareName              = name,
                    SoftwareType              = type,
                    SoftwareVersion           = "1.0",
                    SoftwareLicenseKey        = tag,
                    SoftwareLicenseExpiration = null,
                    SoftwareUsageData         = 0,
                    SoftwareCost              = 0m
                };
                await UpsertSoftwareAsync(db, sw, ct);
                softwareTags.Add(tag);
            }
            else
            {
                // Map hardware: use Tag# as SerialNumber (unique by our index)
                var hw = new Hardware
                {
                    AssetName          = name,
                    AssetType          = type,
                    Status             = status,
                    Manufacturer       = "",
                    Model              = "",
                    SerialNumber       = tag,
                    PurchaseDate       = defPurchase,
                    WarrantyExpiration = defWarranty
                };
                await UpsertHardwareAsync(db, hw, ct);
                hardwareTags.Add(tag);
            }
        }
        // Persist upserts so we can safely query for IDs
        await db.SaveChangesAsync(ct);

        // Build lookup maps AFTER save (so First/Single queries actually find rows)
        var hardwareByTag = await db.HardwareAssets
            .Where(h => hardwareTags.Contains(h.SerialNumber))
            .ToDictionaryAsync(h => h.SerialNumber, ct);

        var softwareByTag = await db.SoftwareAssets
            .Where(s => softwareTags.Contains(s.SoftwareLicenseKey))
            .ToDictionaryAsync(s => s.SoftwareLicenseKey, ct);

        // --- Assignments (active + historical) ---
        var today = DateTime.UtcNow.Date;
        foreach (var row in tableData)
        {
            var type = row["Type"];
            var tag = row["Tag #"];
            var assignedTo = row["Assigned To"];

            var (_, empId) = ParseAssignee(assignedTo);
            if (empId == null) continue;
            if (!usersByEmp.TryGetValue(empId, out var user)) continue;

            if (string.Equals(type, "Software", StringComparison.OrdinalIgnoreCase))
            {
                if (!softwareByTag.TryGetValue(tag, out var sw)) continue;

                // active software assignment (7 days ago)
                await EnsureAssignmentAsync(
                    db,
                    userId: user.UserID,
                    assetKind: AssetKind.Software,
                    hardwareId: null,
                    softwareId: sw.SoftwareID,
                    assignedAtUtc: today.AddDays(-7),
                    ct: ct
                );

                // one software historical (30–15 days ago)
                var start = today.AddDays(-30);
                var end   = today.AddDays(-15);
                var hasHist = await db.Assignments.AnyAsync(a =>
                    a.UserID == user.UserID &&
                    a.AssetKind == AssetKind.Software &&
                    a.SoftwareID == sw.SoftwareID &&
                    a.AssignedAtUtc.Date == start, ct);

                if (!hasHist)
                {
                    db.Assignments.Add(new Assignment
                    {
                        UserID = user.UserID,
                        AssetKind = AssetKind.Software,
                        SoftwareID = sw.SoftwareID,
                        AssetTag = null,
                        AssignedAtUtc = start,
                        UnassignedAtUtc = end
                    });
                }
            }
            else
            {
                if (!hardwareByTag.TryGetValue(tag, out var hw)) continue;

                // active hardware assignment (10 days ago)
                var activeStart = today.AddDays(-10);
                await EnsureAssignmentAsync(
                    db,
                    userId: user.UserID,
                    assetKind: AssetKind.Hardware,
                    hardwareId: hw.HardwareID,
                    softwareId: null,
                    assignedAtUtc: activeStart,
                    ct: ct
                );

                // one hardware historical (40–20 days ago) to supervisor if exists
                if (user.SupervisorID is int supId)
                {
                    var histStart = today.AddDays(-40);
                    var histEnd   = today.AddDays(-20);
                    var hasHist = await db.Assignments.AnyAsync(a =>
                        a.UserID == supId &&
                        a.AssetKind == AssetKind.Hardware &&
                        a.AssetTag == hw.HardwareID &&
                        a.AssignedAtUtc.Date == histStart, ct);

                    if (!hasHist)
                    {
                        db.Assignments.Add(new Assignment
                        {
                            UserID = supId,
                            AssetKind = AssetKind.Hardware,
                            AssetTag = hw.HardwareID,
                            SoftwareID = null,
                            AssignedAtUtc = histStart,
                            UnassignedAtUtc = histEnd
                        });
                    }
                }
            }
        }
        await db.SaveChangesAsync(ct);
        logger?.LogInformation("[DBSeeder] 4.5 done. Roles:{Roles}, Users:{Users}, HW:{HW}, SW:{SW}, Assignments:{Asg}",
            await db.Roles.CountAsync(ct),
            await db.Users.CountAsync(ct),
            await db.HardwareAssets.CountAsync(ct),
            await db.SoftwareAssets.CountAsync(ct),
            await db.Assignments.CountAsync(ct));
    }

    // ========================
    // Deterministic GUID helper
    // ========================
    private static Guid FromString(string input)
    {
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        var hash = sha1.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        return new Guid(bytes);
    }

    // ========================
    // Upsert helpers
    // ========================
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
        AssetKind assetKind,
        int? hardwareId,
        int? softwareId,
        DateTime assignedAtUtc,
        CancellationToken ct)
    {
        int? assetTag = null;
        int? softId   = null;

        if (assetKind == AssetKind.Hardware)
        {
            assetTag = hardwareId ?? throw new ArgumentNullException(nameof(hardwareId));
            softId   = null;
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

    // ========================
    // Small utilities
    // ========================
    private static (string name, string? empId) ParseAssignee(string s)
    {
        s = s?.Trim() ?? "";
        if (string.Equals(s, "Unassigned", StringComparison.OrdinalIgnoreCase)) return (s, null);
        var open = s.LastIndexOf('(');
        var close = s.LastIndexOf(')');
        if (open > 0 && close > open)
        {
            var name = s.Substring(0, open).Trim();
            var id   = s.Substring(open + 1, close - open - 1).Trim();
            return (name, id);
        }
        return (s, null);
    }

    private static string Slug(string s) =>
        new string((s ?? "").ToLowerInvariant().Where(ch => char.IsLetterOrDigit(ch) || ch == '-' ).ToArray());
}