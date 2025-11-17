using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AIMS.Models;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Data;

public static class DbSeeder
{
    private static bool DebugLogs =>
        string.Equals(Environment.GetEnvironmentVariable("AIMS_SEED_LOG"), "debug", StringComparison.OrdinalIgnoreCase);

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

        if (isProd && !allowProdSeed && !IsProdSeedEnvTrue())
        {
            logger?.LogInformation("[DBSeeder] Skipped seeding in Production (allowed=false).");
            return;
        }

        var seedMode = (Environment.GetEnvironmentVariable("AIMS_SEED_MODE") ?? "basic")
            .Trim().ToLowerInvariant();                 // basic | csv | merge
        var seedDir = (Environment.GetEnvironmentVariable("AIMS_SEED_DIR") ?? "/src/seed")
            .Trim();

        logger?.LogInformation("[DBSeeder] Mode={Mode}, SeedDir={Dir}, ProdAllowed={Allow}",
            seedMode, seedDir, allowProdSeed || IsProdSeedEnvTrue());

        // Ensure DB exists/migrated (usually called by scripts, but harmless to assert)
        await db.Database.MigrateAsync(ct);

        if (seedMode is "csv" or "merge")
        {
            await SeedFromCsvAsync(db, seedDir, logger, ct);
            if (seedMode == "merge")
            {
                logger?.LogInformation("[DBSeeder] Merge mode: topping up with curated sample…");
                await SeedSampleAsync(db, logger, ct);
            }
        }
        else
        {
            await SeedSampleAsync(db, logger, ct);
        }

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

    // ==============================
    // ===== CSV SEEDING (idemp) ====
    // ==============================
    private static async Task SeedFromCsvAsync(
        AimsDbContext db, string dir, ILogger? logger, CancellationToken ct)
    {
        // Make sure the directory exists
        if (!Directory.Exists(dir))
        {
            logger?.LogWarning("[DBSeeder/CSV] Seed directory not found: {Dir}", dir);
            return;
        }

        // 1) Roles (key: RoleName)
        await ApplyCsvAsync(db, Path.Combine(dir, "roles.csv"), async rows =>
        {
            foreach (var r in rows)
            {
                var role = new Role
                {
                    RoleName = Get(r, "rolename"),
                    Description = Get(r, "description")
                };
                await UpsertRoleAsync(db, role, ct);
            }
            await db.SaveChangesAsync(ct);
        }, logger);

        // 2) Offices (key: OfficeName). Columns: OfficeName, Location
        await ApplyCsvAsync(db, Path.Combine(dir, "offices.csv"), async rows =>
        {
            foreach (var r in rows)
            {
                var o = new Office { OfficeName = Get(r, "officename"), Location = Get(r, "location") };
                await UpsertOfficeAsync(db, o, ct);
            }
            await db.SaveChangesAsync(ct);
        }, logger);

        // Preload Roles map for later
        var roleByName = await db.Roles.AsNoTracking().ToDictionaryAsync(r => r.RoleName, ct);

        // 3) Users (key: Email). Columns (case-insensitive):
        // FullName, Email, EmployeeNumber, IsActive, RoleName
        await ApplyCsvAsync(db, Path.Combine(dir, "users.csv"), async rows =>
        {
            foreach (var r in rows)
            {
                var email = Get(r, "email");

                var officeIdRaw = ParseInt(Get(r, "officeid"), 0);
                int? officeId = officeIdRaw == 0 ? null : officeIdRaw;

                var incoming = new User
                {
                    FullName = Get(r, "fullname"),
                    Email = email,
                    EmployeeNumber = Get(r, "employeenumber"),
                    IsArchived = ParseBool(Get(r, "isArchived"), defaultValue: true),
                    RoleID = roleByName.TryGetValue(Get(r, "rolename"), out var rr) ? rr.RoleID : roleByName.GetValueOrDefault("Employee")?.RoleID ?? 0,
                    OfficeID = officeId
                };
                await UpsertUserAsync(db, incoming, ct);
            }
            await db.SaveChangesAsync(ct);
        }, logger);

        // Supervisor links (optional): supervisors.csv (EmployeeNumber, SupervisorEmployeeNumber)
        await ApplyCsvAsync(db, Path.Combine(dir, "supervisors.csv"), async rows =>
        {
            var usersByEmp = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.EmployeeNumber ?? "", ct);
            foreach (var r in rows)
            {
                var emp = Get(r, "employeenumber");
                var sup = Get(r, "supervisoremployeenumber");
                if (usersByEmp.TryGetValue(emp, out var user) && usersByEmp.TryGetValue(sup, out var supervisor))
                    await EnsureSupervisorAsync(db, user, supervisor.UserID, ct);
            }
            await db.SaveChangesAsync(ct);
        }, logger);


        // 4) Thresholds (key: AssetType). Columns: AssetType, ThresholdValue
        await ApplyCsvAsync(db, Path.Combine(dir, "thresholds.csv"), async rows =>
        {
            foreach (var r in rows)
            {
                var t = new Threshold
                {
                    AssetType = Get(r, "assettype"),
                    ThresholdValue = ParseInt(Get(r, "thresholdvalue"), 0)
                };
                await UpsertThresholdAsync(db, t, ct);
            }
            await db.SaveChangesAsync(ct);
        }, logger);

        // 5) Hardware (key: SerialNumber)
        // Columns: SerialNumber, AssetName, AssetType, Status, Manufacturer, Model, PurchaseDate, WarrantyExpiration
        await ApplyCsvAsync(db, Path.Combine(dir, "hardware.csv"), async rows =>
        {
            foreach (var r in rows)
            {
                // choose sensible defaults if CSV is missing dates
                var purchase = ParseDateOnlyOrDefault(Get(r, "purchasedate"), DateOnly.FromDateTime(DateTime.UtcNow));
                var warranty = ParseDateOnlyOrDefault(Get(r, "warrantyexpiration"), purchase.AddYears(1));

                // Coalesce AssetTag from multiple header spellings
                var rawTag = Get(r, "assettag");
                if (string.IsNullOrWhiteSpace(rawTag)) rawTag = Get(r, "asset tag");
                if (string.IsNullOrWhiteSpace(rawTag)) rawTag = Get(r, "asset_tag");
                var assetTag = string.IsNullOrWhiteSpace(rawTag)
                    ? $"AT-{Get(r, "serialnumber")}".Trim()
                    : rawTag.Trim();

                var h = new Hardware
                {
                    AssetTag = assetTag,
                    SerialNumber = Get(r, "serialnumber"),
                    AssetName = Get(r, "assetname"),
                    AssetType = Get(r, "assettype"),
                    Status = Get(r, "status"),
                    Manufacturer = Get(r, "manufacturer"),
                    Model = Get(r, "model"),
                    PurchaseDate = purchase,            // DateOnly (non-nullable) ✓
                    WarrantyExpiration = warranty       // DateOnly (non-nullable) ✓
                };
                await UpsertHardwareAsync(db, h, ct);
            }
            await db.SaveChangesAsync(ct);
        }, logger);

        var hardwareBySerial = await db.HardwareAssets.AsNoTracking()
            .ToDictionaryAsync(h => h.SerialNumber, ct);

        // 6) Software (key: SoftwareName + SoftwareVersion)
        // Columns: SoftwareName, SoftwareVersion, SoftwareType, SoftwareLicenseKey, SoftwareLicenseExpiration,
        //          SoftwareUsageData, SoftwareCost, LicenseTotalSeats, LicenseSeatsUsed
        await ApplyCsvAsync(db, Path.Combine(dir, "software.csv"), async rows =>
        {
            foreach (var r in rows)
            {
                var s = new Software
                {
                    SoftwareName = Get(r, "softwarename"),
                    SoftwareVersion = Get(r, "softwareversion"),
                    SoftwareType = Get(r, "softwaretype"),
                    SoftwareLicenseKey = Get(r, "softwarelicensekey"),
                    SoftwareLicenseExpiration = ParseDateOnly(Get(r, "softwarelicenseexpiration")),
                    SoftwareUsageData = ParseInt(Get(r, "softwareusagedata"), 0),
                    SoftwareCost = ParseDecimal(Get(r, "softwarecost"), 0m),
                    LicenseTotalSeats = ParseInt(Get(r, "licensetotalseats"), 0),
                    LicenseSeatsUsed = ParseInt(Get(r, "licenseseatsused"), 0),
                };
                await UpsertSoftwareAsync(db, s, ct);
            }
            await db.SaveChangesAsync(ct);
        }, logger);

        var softwareByKey = await db.SoftwareAssets.AsNoTracking()
            .ToDictionaryAsync(s => (s.SoftwareName, s.SoftwareVersion), ct);

        // 7) Agreements (key: FileUri). Columns supported:
        // FileUri, AssetKind(Hardware|Software),
        // HardwareID?, SerialNumber?,
        // SoftwareID?, SoftwareName?, SoftwareVersion?,
        // DateAdded
        await ApplyCsvAsync(db, Path.Combine(dir, "agreements.csv"), async rows =>
        {
            foreach (var r in rows)
            {
                var kind = ParseAssetKind(Get(r, "assetkind"));
                int? hardwareId = null;
                int? softwareId = null;

                // ---- Try explicit IDs first ----
                var hwIdText = Get(r, "hardwareid");
                if (int.TryParse(hwIdText, out var hwIdParsed))
                    hardwareId = hwIdParsed;

                var swIdText = Get(r, "softwareid");
                if (int.TryParse(swIdText, out var swIdParsed))
                    softwareId = swIdParsed;

                // ---- Fallbacks: resolve by natural keys if IDs not present ----
                if (kind == AssetKind.Hardware && hardwareId is null)
                {
                    var serial = Get(r, "serialnumber");
                    if (!string.IsNullOrWhiteSpace(serial) && hardwareBySerial.TryGetValue(serial, out var hw))
                        hardwareId = hw.HardwareID;
                }
                else if (kind == AssetKind.Software && softwareId is null)
                {
                    var name = Get(r, "softwarename");
                    var ver = Get(r, "softwareversion");
                    if (!string.IsNullOrWhiteSpace(name) && softwareByKey.TryGetValue((name, ver), out var sw))
                        softwareId = sw.SoftwareID;
                }

                var a = new Agreement
                {
                    FileUri = Get(r, "fileuri"),
                    AssetKind = kind,
                    HardwareID = hardwareId,
                    SoftwareID = softwareId,
                    DateAdded = ParseDateTime(Get(r, "dateadded")) ?? DateTime.UtcNow
                };

                await UpsertAgreementAsync(db, a, ct);
            }
            await db.SaveChangesAsync(ct);
        }, logger);

        // 8) Reports
        // Accepts either IDs or human keys. Preferred: generatedbyemail / generatedforoffice
        await ApplyCsvAsync(db, Path.Combine(dir, "reports.csv"), async rows =>
        {
            var userById = (await db.Users.Where(u => !u.IsArchived).ToListAsync())
                              .ToDictionary(u => u.UserID);

            var userByEmail = (await db.Users.Where(u => !u.IsArchived && u.Email != null).ToListAsync())
                              .GroupBy(u => u.Email!.Trim().ToLowerInvariant())
                              .ToDictionary(g => g.Key, g => g.First().UserID);

            var officeById = (await db.Offices.ToListAsync()).ToDictionary(o => o.OfficeID);

            var officeByName = (await db.Offices.Where(o => o.OfficeName != null).ToListAsync())
                               .GroupBy(o => o.OfficeName!.Trim().ToLowerInvariant())
                               .ToDictionary(g => g.Key, g => g.First().OfficeID);

            foreach (var r in rows)
            {
                int? byUserId = null;
                int? forOfficeId = null;

                // Prefer emails / names (CSV headers are lower-case; Get() is case-insensitive in our helper)
                var byEmail = Get(r, "generatedbyemail")?.Trim().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(byEmail) && userByEmail.TryGetValue(byEmail!, out var uidFromEmail))
                    byUserId = uidFromEmail;

                var officeName = Get(r, "generatedforoffice")?.Trim().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(officeName) && officeByName.TryGetValue(officeName!, out var oidFromName))
                    forOfficeId = oidFromName;

                // Fallbacks: explicit IDs in CSV (if present)
                if (byUserId is null)
                {
                    var uidText = Get(r, "generatedbyuserid");
                    if (int.TryParse(uidText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tmpUid)
                        && userById.ContainsKey(tmpUid))
                        byUserId = tmpUid;
                }

                if (forOfficeId is null)
                {
                    var oidText = Get(r, "generatedforofficeid");
                    if (int.TryParse(oidText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tmpOid)
                        && officeById.ContainsKey(tmpOid))
                        forOfficeId = tmpOid;
                }

                // Hard guard: refuse to insert dangling FKs
                if (byUserId is null)
                    throw new InvalidOperationException($"Reports CSV: could not resolve GeneratedBy user for Name='{Get(r, "name")}'. Provide generatedbyemail or a valid generatedbyuserid.");
                if (forOfficeId is null)
                    throw new InvalidOperationException($"Reports CSV: could not resolve GeneratedFor office for Name='{Get(r, "name")}'. Provide generatedforoffice or a valid generatedforofficeid.");

                var report = new Report
                {
                    Name = Get(r, "name")!,
                    Type = Get(r, "type")!,
                    Description = Get(r, "description"),
                    ExternalId = ParseGuid(Get(r, "externalid")),                 // supply a GUID in CSV
                    DateCreated = ParseDateTime(Get(r, "datecreated")) ?? DateTime.UtcNow,
                    Content = GetBytes(r, "content"),                              // base64 in CSV
                    GeneratedByUserID = byUserId.Value,
                    GeneratedForOfficeID = forOfficeId.Value
                };

                await UpsertReportAsync(db, report, ct);
            }

            await db.SaveChangesAsync(ct);
        }, logger);

        // 9) Assignments (idempotent)
        // Supports either keys OR IDs:
        //  - User: EmployeeNumber OR UserID
        //  - Hardware: SerialNumber OR HardwareID
        //  - Software: (SoftwareName + SoftwareVersion) OR SoftwareID
        await ApplyCsvAsync(db, Path.Combine(dir, "assignments.csv"), async rows =>
        {
            var usersByEmp = await db.Users.AsNoTracking()
                .Where(u => u.EmployeeNumber != null)
                .ToDictionaryAsync(u => u.EmployeeNumber!, ct);

            var usersById = await db.Users.AsNoTracking()
                .ToDictionaryAsync(u => u.UserID, ct);

            var applied = 0;
            var skipped = 0;
            var i = 0;

            foreach (var r in rows)
            {
                i++;

                var kind = ParseAssetKind(Get(r, "assetkind"));
                var assignedAt = ParseDateTime(Get(r, "assignedatutc")) ?? DateTime.UtcNow;
                var unassignedAt = ParseDateTime(Get(r, "unassignedatutc"));

                // ---- Resolve User (EmployeeNumber or UserID) ----
                int? userId = null;
                var emp = Get(r, "employeenumber");
                if (!string.IsNullOrWhiteSpace(emp) && usersByEmp.TryGetValue(emp, out var u))
                    userId = u.UserID;
                else if (int.TryParse(Get(r, "userid"), out var uid) && usersById.TryGetValue(uid, out var u2))
                    userId = u2.UserID;

                if (userId is null)
                {
                    skipped++;
                    if (DebugLogs) logger?.LogWarning("[DBSeeder/Assign CSV] Row {Row}: SKIP — user not resolved (emp='{Emp}', userId='{UidRaw}')", i, emp, Get(r, "userid"));
                    continue;
                }

                if (kind == AssetKind.Hardware)
                {
                    // ---- Resolve Hardware (SerialNumber or HardwareID) ----
                    int? hardwareId = null;
                    var serial = Get(r, "serialnumber");
                    if (!string.IsNullOrWhiteSpace(serial) && hardwareBySerial.TryGetValue(serial, out var hw))
                        hardwareId = hw.HardwareID;
                    else if (int.TryParse(Get(r, "hardwareid"), out var hid))
                        hardwareId = await db.HardwareAssets.AsNoTracking().AnyAsync(h => h.HardwareID == hid, ct) ? hid : null;

                    if (hardwareId is null)
                    {
                        skipped++;
                        if (DebugLogs) logger?.LogWarning("[DBSeeder/Assign CSV] Row {Row}: SKIP — hardware not resolved (serial='{Serial}', hardwareId='{HidRaw}')", i, serial, Get(r, "hardwareid"));
                        continue;
                    }

                    // Make sure only this user has the asset open
                    await CloseOpenHardwareAssignmentsExceptAsync(db, hardwareId.Value, userId.Value, ct);
                    await db.SaveChangesAsync(ct);

                    // Ensure the open assignment row exists (by day)
                    await EnsureAssignmentAsync(db, userId.Value, AssetKind.Hardware, hardwareId, null, assignedAt, ct);

                    // Close it if CSV provides an explicit unassign time
                    if (unassignedAt is not null)
                    {
                        var open = await db.Assignments
                            .Where(a => a.AssetKind == AssetKind.Hardware
                                     && a.HardwareID == hardwareId.Value
                                     && a.UserID == userId.Value
                                     && a.UnassignedAtUtc == null)
                            .OrderByDescending(a => a.AssignedAtUtc)
                            .FirstOrDefaultAsync(ct);

                        if (open is not null && open.AssignedAtUtc <= unassignedAt.Value)
                            open.UnassignedAtUtc = unassignedAt.Value;
                    }

                    applied++;
                }
                else // Software
                {
                    // ---- Resolve Software ((Name+Version) or SoftwareID) ----
                    int? softwareId = null;
                    var name = Get(r, "softwarename");
                    var ver = Get(r, "softwareversion");
                    if (!string.IsNullOrWhiteSpace(name) && softwareByKey.TryGetValue((name, ver), out var sw))
                        softwareId = sw.SoftwareID;
                    else if (int.TryParse(Get(r, "softwareid"), out var sid))
                        softwareId = await db.SoftwareAssets.AsNoTracking().AnyAsync(s => s.SoftwareID == sid, ct) ? sid : null;

                    if (softwareId is null)
                    {
                        skipped++;
                        if (DebugLogs) logger?.LogWarning("[DBSeeder/Assign CSV] Row {Row}: SKIP — software not resolved (name='{Name}', ver='{Ver}', softwareId='{SidRaw}')", i, name, ver, Get(r, "softwareid"));
                        continue;
                    }

                    // Make sure only this user has the seat open
                    await CloseOpenSoftwareAssignmentsExceptAsync(db, softwareId.Value, userId.Value, ct);
                    await db.SaveChangesAsync(ct);

                    // Ensure the open assignment row exists (by day)
                    await EnsureAssignmentAsync(db, userId.Value, AssetKind.Software, null, softwareId, assignedAt, ct);

                    // Close it if CSV provides an explicit unassign time
                    if (unassignedAt is not null)
                    {
                        var open = await db.Assignments
                            .Where(a => a.AssetKind == AssetKind.Software
                                     && a.SoftwareID == softwareId.Value
                                     && a.UserID == userId.Value
                                     && a.UnassignedAtUtc == null)
                            .OrderByDescending(a => a.AssignedAtUtc)
                            .FirstOrDefaultAsync(ct);

                        if (open is not null && open.AssignedAtUtc <= unassignedAt.Value)
                            open.UnassignedAtUtc = unassignedAt.Value;
                    }

                    applied++;
                }

                if ((i % 100) == 0) // small batching to keep EF change tracker sane on large CSVs
                    await db.SaveChangesAsync(ct);
            }

            await db.SaveChangesAsync(ct);
            logger?.LogInformation("[DBSeeder/Assign CSV] Applied={Applied}, Skipped={Skipped}, Total={Total}", applied, skipped, rows.Count);
        }, logger);

        // ----- Recompute software LicenseSeatsUsed based on OPEN assignments -----
        {
            var allSoftware = await db.SoftwareAssets.ToListAsync(ct);
            foreach (var sw in allSoftware)
            {
                var openCount = await db.Assignments.CountAsync(a =>
                    a.AssetKind == AssetKind.Software &&
                    a.SoftwareID == sw.SoftwareID &&
                    a.UnassignedAtUtc == null, ct);

                sw.LicenseSeatsUsed = Math.Min(openCount, sw.LicenseTotalSeats);
            }
            await db.SaveChangesAsync(ct);
        }

        // Key audit-log changes by ExternalId (GUID or AL-0001 style → deterministic GUID)
        var changesByExt = new Dictionary<Guid, List<AuditLogChange>>();

        // --- 10a) load AuditLogChanges first ---
        // CSV must have: ExternalId, Field, OldValue, NewValue
        await ApplyCsvAsync(db, Path.Combine(dir, "auditlogchanges.csv"), async rows =>
        {
            changesByExt.Clear();

            foreach (var r in rows)
            {
                // Accept real GUIDs or stable tokens like "AL-0001" (deterministically mapped)
                var extId = ParseGuid(Get(r, "externalid"));

                if (!changesByExt.TryGetValue(extId, out var list))
                {
                    list = new List<AuditLogChange>();
                    changesByExt[extId] = list;
                }

                list.Add(new AuditLogChange
                {
                    Field = Get(r, "field"),
                    OldValue = Get(r, "oldvalue"),
                    NewValue = Get(r, "newvalue")
                });
            }

            await Task.CompletedTask;
        }, logger);

        // --- 10b) load AuditLogs (main records) ---
        // CSV must have at least: ExternalId, TimestampUtc, UserEmployeeNumber(or UserID),
        // Action, Description, AssetKind, and either HardwareID/SerialNumber (for Hardware)
        // or SoftwareID/SoftwareName+SoftwareVersion (for Software).
        await ApplyCsvAsync(db, Path.Combine(dir, "auditlogs.csv"), async rows =>
        {
            var usersByEmp = await db.Users.AsNoTracking()
                .ToDictionaryAsync(u => u.EmployeeNumber ?? "", ct);
            var usersById = await db.Users.AsNoTracking()
                .ToDictionaryAsync(u => u.UserID, ct);

            foreach (var r in rows)
            {
                // Accept true GUIDs or tokens like "AL-0001" (deterministic)
                var extId = ParseGuid(Get(r, "externalid"));

                var kind = ParseAssetKind(Get(r, "assetkind"));

                // Resolve target ids: prefer explicit numeric IDs; otherwise fall back to natural keys
                int? hardwareId = null, softwareId = null;

                if (kind == AssetKind.Hardware)
                {
                    if (int.TryParse(Get(r, "hardwareid"), out var hwId))
                    {
                        hardwareId = hwId;
                    }
                    else
                    {
                        var serial = Get(r, "serialnumber");
                        if (!string.IsNullOrWhiteSpace(serial) &&
                            hardwareBySerial.TryGetValue(serial, out var hw))
                        {
                            hardwareId = hw.HardwareID;
                        }
                    }
                }
                else // Software
                {
                    if (int.TryParse(Get(r, "softwareid"), out var swId))
                    {
                        softwareId = swId;
                    }
                    else
                    {
                        var name = Get(r, "softwarename");
                        var ver = Get(r, "softwareversion");
                        if (!string.IsNullOrWhiteSpace(name) &&
                            softwareByKey.TryGetValue((name, ver), out var sw))
                        {
                            softwareId = sw.SoftwareID;
                        }
                    }
                }

                // Resolve actor: prefer EmployeeNumber, else explicit UserID
                int userId = 0;
                var emp = Get(r, "useremployeenumber");
                if (!string.IsNullOrWhiteSpace(emp) && usersByEmp.TryGetValue(emp, out var uByEmp))
                    userId = uByEmp.UserID;
                else if (int.TryParse(Get(r, "userid"), out var uid) && usersById.ContainsKey(uid))
                    userId = uid;

                var log = new AuditLog
                {
                    ExternalId = extId,
                    TimestampUtc = ParseDateTime(Get(r, "timestamputc")) ?? DateTime.UtcNow,
                    UserID = userId,
                    Action = Get(r, "action"),
                    Description = Get(r, "description"),
                    AssetKind = kind,
                    HardwareID = hardwareId,
                    SoftwareID = softwareId
                };

                // Attach any changes we loaded in 10a for the same ExternalId
                var changes = changesByExt.TryGetValue(extId, out var list)
                    ? (IList<AuditLogChange>)list
                    : Array.Empty<AuditLogChange>();

                await UpsertAuditWithChangesAsync(db, log, changes, ct);
            }

            await db.SaveChangesAsync(ct);
        }, logger);
    }

    private static async Task ApplyCsvAsync(
        AimsDbContext db, string path, Func<List<Dictionary<string, string>>, Task> apply, ILogger? logger)
    {
        if (!File.Exists(path))
        {
            logger?.LogDebug("[DBSeeder/CSV] Skip (missing): {Path}", path);
            return;
        }

        var rows = ReadCsv(path);
        if (rows.Count == 0)
        {
            logger?.LogWarning("[DBSeeder/CSV] Empty file: {Path}", path);
            return;
        }

        logger?.LogInformation("[DBSeeder/CSV] Applying {Count} rows from {Path}", rows.Count, path);
        await apply(rows);
    }

    // ==============================
    // ===== BASIC SAMPLE SEED ======
    // ==============================
    private static async Task SeedSampleAsync(AimsDbContext db, ILogger? logger, CancellationToken ct)
    {
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
                       IsArchived = false, RoleID = roleByName["Supervisor"].RoleID, ExternalId = UId("28809","john.smith@aims.local") },

            new User { FullName = "Jane Doe", Email = "jane.doe@aims.local", EmployeeNumber = "69444",
                       IsArchived = false, RoleID = roleByName["IT Help Desk"].RoleID, ExternalId = UId("69444","jane.doe@aims.local") },

            new User { FullName = "Randy Orton", Email = "randy.orton@aims.local", EmployeeNumber = "58344",
                       IsArchived = false, RoleID = roleByName["IT Help Desk"].RoleID, ExternalId = UId("58344","randy.orton@aims.local") },

            new User { FullName = "Robin Williams", Email = "robin.williams@aims.local", EmployeeNumber = "10971",
                       IsArchived = false, RoleID = roleByName["IT Help Desk"].RoleID, ExternalId = UId("10971","robin.williams@aims.local") },

            new User { FullName = "Sarah Johnson", Email = "sarah.johnson@aims.local", EmployeeNumber = "62241",
                       IsArchived = false, RoleID = roleByName["Supervisor"].RoleID, ExternalId = UId("62241","sarah.johnson@aims.local") },

            new User { FullName = "Caitlin Clark", Email = "caitlin.clark@aims.local", EmployeeNumber = "90334",
                       IsArchived = false, RoleID = roleByName["Supervisor"].RoleID, ExternalId = UId("90334","caitlin.clark@aims.local") },

            new User { FullName = "Brian Regan", Email = "brian.regan@aims.local", EmployeeNumber = "27094",
                       IsArchived = false, RoleID = roleByName["Supervisor"].RoleID, ExternalId = UId("27094","brian.regan@aims.local") },

            new User { FullName = "Maximillian Brandt", Email = "max.brandt@aims.local", EmployeeNumber = "20983",
                       IsArchived = false, RoleID = roleByName["Admin"].RoleID, ExternalId = UId("20983","max.brandt@aims.local") },

            new User { FullName = "Kate Rosenberg", Email = "kate.rosenberg@aims.local", EmployeeNumber = "93232",
                       IsArchived = false, RoleID = roleByName["Admin"].RoleID, ExternalId = UId("93232","kate.rosenberg@aims.local") },

            new User { FullName = "Emily Carter", Email = "emily.carter@aims.local", EmployeeNumber = "47283",
                       IsArchived = false, RoleID = roleByName["IT Help Desk"].RoleID, ExternalId = UId("47283","emily.carter@aims.local") },

            new User { FullName = "Bruce Wayne", Email = "bruce.wayne@aims.local", EmployeeNumber = "34532",
                       IsArchived = false, RoleID = roleByName["IT Help Desk"].RoleID, ExternalId = UId("34532","bruce.wayne@aims.local") },

            // Tyler
            new User { FullName = "Tyler Burguillos", Email = "tnburg@pacbell.net", EmployeeNumber = "80003",
                       IsArchived = false, RoleID = roleByName["Supervisor"].RoleID, ExternalId = UId("80003","tnburg@pacbell.net") },
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
        string[] reportsOfTyler = { "34532", "62241" };
        foreach (var emp in reportsOfTyler)
        {
            if (usersByEmp.TryGetValue(emp, out var user))
                await EnsureSupervisorAsync(db, user, tylerId, ct);
        }
        await db.SaveChangesAsync(ct);

        // Offices
        var officesWanted = new[]
        {
            new Office { OfficeName = "HQ - Sacramento", Location = "915 L St, Sacramento, CA" },
            new Office { OfficeName = "Warehouse - West", Location = "501 River Way, West Sac, CA" },
            new Office { OfficeName = "Remote", Location = "Distributed" },
        };
        foreach (var o in officesWanted) await UpsertOfficeAsync(db, o, ct);
        await db.SaveChangesAsync(ct);
        var officeByName = await db.Offices.AsNoTracking().ToDictionaryAsync(o => o.OfficeName, ct);

        // Thresholds
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

        // Hardware
        var hardwareWanted = new[]
        {
            new Hardware { AssetName="Lenovo ThinkPad E16", AssetType="Laptop", Status="Assigned", Manufacturer="Lenovo", Model="E16", SerialNumber="LT-0020",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) },
            new Hardware { AssetName="Samsung Galaxy Book4", AssetType="Laptop", Status="Damaged", Manufacturer="Samsung", Model="Book4", SerialNumber="LT-0005",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-8)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(10)) },
            new Hardware { AssetName="Dell Inspiron 15", AssetType="Laptop", Status="Assigned", Manufacturer="Dell", Model="Inspiron 15", SerialNumber="LT-0115",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-10)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(2)) },

            new Hardware { AssetName="Dell S2421NX", AssetType ="Monitor", Status="Assigned", Manufacturer="Dell", Model ="S2421NX", SerialNumber="MN-0001",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) },
            new Hardware { AssetName="HP 527SH", AssetType="Monitor", Status="Assigned", Manufacturer="HP", Model="527SH", SerialNumber="MN-0023",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-4)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(2)) },

            new Hardware { AssetName="Lenovo IdeaCentre 3", AssetType="Desktop", Status="Damaged", Manufacturer="Lenovo", Model="IdeaCentre 3", SerialNumber="DT-0011",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-12)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)) },
            new Hardware { AssetName="HP Pavillion TP01-2234", AssetType="Desktop", Status="Available", Manufacturer="HP", Model="TP01-2234", SerialNumber="DT-0075",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-2)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(3)) },
            new Hardware { AssetName="Dell Inspiron 3030", AssetType="Desktop", Status="Assigned", Manufacturer="Dell", Model="Inspiron 3030", SerialNumber="DT-0100",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-7)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) },

            new Hardware { AssetName="Logitech Zone 300", AssetType="Headset", Status="Available", Manufacturer ="Logitech", Model="Zone 300", SerialNumber="HS-0080",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-5)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) },
            new Hardware { AssetName="Logitech Zone Vibe 100", AssetType ="Headset", Status ="In Repair", Manufacturer ="Logitech", Model="Zone Vibe 100", SerialNumber="HS-0015",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(2)) },
            new Hardware { AssetName="Poly Voyager 4320", AssetType="Headset", Status="In Repair", Manufacturer ="Poly", Model="Voyager 4320", SerialNumber="HS-0001",
                           PurchaseDate=DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-3)), WarrantyExpiration=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(2)) },

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

        // Software
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

        // Agreements
        var agreementsWanted = new List<Agreement>
        {
            new Agreement
            {
                FileUri   = "blob://agreements/hardware/LT-0020-warranty.pdf",
                AssetKind = AssetKind.Hardware,
                HardwareID = hardwareBySerial["LT-0020"].HardwareID,
                SoftwareID = null,
                DateAdded = DateTime.UtcNow.AddDays(-25)
            },
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

        // Reports
        var reportsWanted = new List<Report>
        {
            new Report
            {
                Name = "Weekly Asset Count",
                Type = "Inventory",
                Description = "Hardware & Software totals by type",
                GeneratedByUserID = usersByEmp["28809"].UserID, // John
                GeneratedForOfficeID = officeByName["HQ - Sacramento"].OfficeID,
                DateCreated = DateTime.UtcNow.AddDays(-7)
            },
            new Report
            {
                Name = "License Usage Summary",
                Type = "Software",
                Description = "Seats used vs total by product",
                GeneratedByUserID = usersByEmp["47283"].UserID, // Emily
                GeneratedForOfficeID = officeByName["Remote"].OfficeID,
                DateCreated = DateTime.UtcNow.AddDays(-3)
            }
        };
        foreach (var r in reportsWanted) await UpsertReportAsync(db, r, ct);
        await db.SaveChangesAsync(ct);

        // Assignments
        usersByEmp = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.EmployeeNumber!, ct);

        async Task TryAssignHW(string emp, string serial, DateTime whenUtc)
        {
            if (!usersByEmp.TryGetValue(emp, out var user)) return;
            if (!hardwareBySerial.TryGetValue(serial, out var hw)) return;

            await CloseOpenHardwareAssignmentsExceptAsync(db, hw.HardwareID, user.UserID, ct);
            await EnsureAssignmentAsync(db, user.UserID, AssetKind.Hardware, hw.HardwareID, null, whenUtc, ct);
        }

        async Task TryAssignSW(string emp, string name, string ver, DateTime whenUtc)
        {
            if (!usersByEmp.TryGetValue(emp, out var user)) return;
            if (!softwareByKey.TryGetValue((name, ver), out var sw)) return;

            await CloseOpenSoftwareAssignmentsExceptAsync(db, sw.SoftwareID, user.UserID, ct);
            await EnsureAssignmentAsync(db, user.UserID, AssetKind.Software, null, sw.SoftwareID, whenUtc, ct);
        }

        var now = DateTime.UtcNow;
        await TryAssignHW("28809", "LT-0020", now.AddDays(-12));
        await TryAssignHW("69444", "MN-0001", now.AddDays(-11));
        await TryAssignHW("58344", "DT-0011", now.AddDays(-10));
        await TryAssignSW("10971", "Microsoft 365 Business", "1.0", now.AddDays(-9));
        await TryAssignHW("62241", "MN-0023", now.AddDays(-8));
        await TryAssignHW("90334", "LT-0005", now.AddDays(-7));
        await TryAssignHW("27094", "HS-0015", now.AddDays(-6));
        await TryAssignHW("20983", "DT-0100", now.AddDays(-5));
        await TryAssignHW("47283", "HS-0001", now.AddDays(-4));
        await TryAssignHW("34532", "CC-0019", now.AddDays(-3));
        await TryAssignHW("93232", "LT-0115", now.AddDays(-2));

        await db.SaveChangesAsync(ct);

        // Audit logs (15 examples)
        var auditEvents = new List<(AuditLog log, IList<AuditLogChange> changes)>
        {
            // 1) Hardware assignment (John -> LT-0020)
            (
                new AuditLog {
                    ExternalId   = FromString("audit:assign:LT-0020:28809"),
                    TimestampUtc = now.AddDays(-12).AddMinutes(3),
                    UserID       = usersByEmp["28809"].UserID,
                    Action       = "Assign",
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
                    UserID       = usersByEmp["10971"].UserID,
                    Action       = "Assign",
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
                    UserID       = usersByEmp["27094"].UserID,
                    Action       = "Update",
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
                    UserID       = usersByEmp["47283"].UserID,
                    Action       = "Create",
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
                    UserID       = usersByEmp["69444"].UserID,
                    Action       = "Assign",
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
                    UserID       = usersByEmp["58344"].UserID,
                    Action       = "Assign",
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
                    UserID       = usersByEmp["93232"].UserID,
                    Action       = "Unassign",
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
                    UserID       = usersByEmp["93232"].UserID,
                    Action       = "Update",
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
                    UserID       = usersByEmp["62241"].UserID,
                    Action       = "Update",
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
                    Action       = "Install",
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
                    UserID       = usersByEmp["20983"].UserID,
                    Action       = "License Renewed",
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
                    UserID       = usersByEmp["62241"].UserID,
                    Action       = "License Expired",
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
                    UserID       = usersByEmp["47283"].UserID,
                    Action       = "Unassign",
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
                    UserID       = usersByEmp["28809"].UserID,
                    Action       = "Install",
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
                    UserID       = usersByEmp["47283"].UserID,
                    Action       = "Remove",
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
    }

    // ==============================
    // ===== Helpers / Upserts  =====
    // ==============================
    private static bool IsProdSeedEnvTrue() =>
        string.Equals(Environment.GetEnvironmentVariable("AIMS_ALLOW_PROD_SEED"), "true", StringComparison.OrdinalIgnoreCase);

    private static Guid FromString(string input)
    {
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(input));
        Span<byte> bytes = new byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        return new Guid(bytes);
    }

    // Deterministic 32-hex GraphObjectID generator
    private static string GraphIdFor(User u)
    {
        var key = !string.IsNullOrWhiteSpace(u.EmployeeNumber)
            ? $"emp:{u.EmployeeNumber}"
            : $"email:{u.Email}";
        return FromString("graph:" + key).ToString("N");
    }

    private static DateOnly ParseDateOnlyOrDefault(string v, DateOnly fallback)
    {
        var d = ParseDateOnly(v);
        return d ?? fallback;
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

        if (string.IsNullOrWhiteSpace(incoming.GraphObjectID))
            incoming.GraphObjectID = GraphIdFor(incoming);

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
            existing.IsArchived = incoming.IsArchived;
            existing.RoleID = incoming.RoleID;
            existing.OfficeID = incoming.OfficeID;

            if (string.IsNullOrWhiteSpace(existing.GraphObjectID))
                existing.GraphObjectID = GraphIdFor(existing);
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
        if (existing is null)
        {
            // Ensure a non-empty AssetTag on insert
            if (string.IsNullOrWhiteSpace(incoming.AssetTag))
                incoming.AssetTag = $"AT-{incoming.SerialNumber}".Trim();

            await db.HardwareAssets.AddAsync(incoming, ct);
        }
        else
        {
            // Allow AssetTag to be updated when provided
            if (!string.IsNullOrWhiteSpace(incoming.AssetTag))
                existing.AssetTag = incoming.AssetTag.Trim();

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
        // Ensure incoming seats-used never exceeds incoming total
        incoming.LicenseSeatsUsed = Math.Min(incoming.LicenseSeatsUsed, incoming.LicenseTotalSeats);

        var existing = await db.SoftwareAssets
            .FirstOrDefaultAsync(s => s.SoftwareName == incoming.SoftwareName &&
                                      s.SoftwareVersion == incoming.SoftwareVersion, ct);

        if (existing is null)
        {
            await db.SoftwareAssets.AddAsync(incoming, ct);
        }
        else
        {
            existing.SoftwareType = incoming.SoftwareType;
            existing.SoftwareLicenseKey = incoming.SoftwareLicenseKey;
            existing.SoftwareLicenseExpiration = incoming.SoftwareLicenseExpiration;
            existing.SoftwareUsageData = incoming.SoftwareUsageData;
            existing.SoftwareCost = incoming.SoftwareCost;

            // Update total first…
            existing.LicenseTotalSeats = incoming.LicenseTotalSeats;

            // …then cap used to both: max(existing, incoming) but never above total
            var maxUsed = Math.Max(existing.LicenseSeatsUsed, incoming.LicenseSeatsUsed);
            existing.LicenseSeatsUsed = Math.Min(maxUsed, existing.LicenseTotalSeats);
        }
    }

    private static async Task UpsertAgreementAsync(AimsDbContext db, Agreement incoming, CancellationToken ct)
    {
        var existing = await db.Agreements.FirstOrDefaultAsync(a => a.FileUri == incoming.FileUri, ct);
        if (existing is null)
        {
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
        var existing = await db.Reports.FirstOrDefaultAsync(r => r.Name == incoming.Name && r.Type == incoming.Type, ct);
        if (existing is null) await db.Reports.AddAsync(incoming, ct);
        else
        {
            existing.Description = incoming.Description;
            existing.GeneratedByUserID = incoming.GeneratedByUserID;
            existing.GeneratedForOfficeID = incoming.GeneratedForOfficeID;
            existing.DateCreated = incoming.DateCreated;
        }
    }

    private static async Task UpsertAuditWithChangesAsync(
        AimsDbContext db, AuditLog incoming, IList<AuditLogChange> changes, CancellationToken ct)
    {
        var existing = await db.AuditLogs
            .Include(a => a.Changes)
            .FirstOrDefaultAsync(a => a.ExternalId == incoming.ExternalId, ct);

        if (existing is null)
        {
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
            existing.TimestampUtc = incoming.TimestampUtc;
            existing.UserID = incoming.UserID;
            existing.Action = incoming.Action;
            existing.Description = incoming.Description;

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
    AssetKind assetKind,
    int? hardwareId,
    int? softwareId,
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

        // --- Ensure at most ONE open row per asset by closing any existing open row first ---
        var existingOpen = await db.Assignments
            .Where(a => a.AssetKind == assetKind
                     && a.UnassignedAtUtc == null
                     && ((assetKind == AssetKind.Hardware && a.HardwareID == hardwareIdToAssign)
                      || (assetKind == AssetKind.Software && a.SoftwareID == softwareIdToAssign)))
            .OrderByDescending(a => a.AssignedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (existingOpen is not null)
        {
            // If it's already open for this same user on the same day, nothing to do.
            if (existingOpen.UserID == userId && existingOpen.AssignedAtUtc.Date == assignedAtUtc.Date)
                return;

            // Prevent backwards time: if CSV gives an earlier assignedAt than the existing open,
            // keep the close time at the existing open's assigned time.
            var closeAt = assignedAtUtc >= existingOpen.AssignedAtUtc
                ? assignedAtUtc
                : existingOpen.AssignedAtUtc;

            existingOpen.UnassignedAtUtc = closeAt;

            // Flush the close so the unique index (AssetID, UnassignedAtUtc=NULL) is clear before we insert a new open row.
            await db.SaveChangesAsync(ct);
        }

        // If we already inserted this exact (user, asset, day) row earlier in the run, skip dup.
        var existsSameDay = await db.Assignments.AnyAsync(a =>
            a.UserID == userId
            && a.AssetKind == assetKind
            && a.HardwareID == hardwareIdToAssign
            && a.SoftwareID == softwareIdToAssign
            && a.AssignedAtUtc.Date == assignedAtUtc.Date, ct);

        if (existsSameDay)
            return;

        // Insert new open row
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
    private static AssetKind ParseAssetKind(string v)
    {
        var s = (v ?? "").Trim().ToLowerInvariant();
        if (s.StartsWith("soft") || s == "sw" || s == "s") return AssetKind.Software; // "software", "soft", "sw"
        if (s.StartsWith("hard") || s == "hw" || s == "h") return AssetKind.Hardware; // "hardware", "hard", "hw"
                                                                                      // Default to Hardware if omitted/unknown so hardware rows still flow
        return AssetKind.Hardware;
    }
    // ==============================
    // ===== Minimal CSV Reader =====
    // ==============================
    private static List<Dictionary<string, string>> ReadCsv(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0) return new();

        var headers = ParseCsvLine(lines[0]).Select(h => h.Trim().ToLowerInvariant()).ToArray();
        var rows = new List<Dictionary<string, string>>(Math.Max(0, lines.Length - 1));

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var cols = ParseCsvLine(lines[i]);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int c = 0; c < headers.Length; c++)
            {
                var val = c < cols.Count ? cols[c] : "";
                row[headers[c]] = val;
            }
            rows.Add(row);
        }
        return rows;
    }

    // Handles quoted fields with commas and double-quote escapes ("")
    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    // lookahead for escaped quote
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"'); i++; // skip the escape
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(ch);
                }
            }
            else
            {
                if (ch == '"') inQuotes = true;
                else if (ch == ',')
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(ch);
                }
            }
        }
        result.Add(sb.ToString());
        return result;
    }

    // ==============================
    // ===== Safe field helpers  ====
    // ==============================
    private static string Get(Dictionary<string, string> row, string key)
        => row.TryGetValue(key, out var v) ? v?.Trim() ?? "" : "";

    private static bool ParseBool(string v, bool defaultValue = false)
        => string.IsNullOrWhiteSpace(v) ? defaultValue :
           v.Equals("1") || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("yes", StringComparison.OrdinalIgnoreCase);

    private static int ParseInt(string v, int def) =>
        int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : def;

    private static decimal ParseDecimal(string v, decimal def) =>
        decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : def;

    private static DateOnly? ParseDateOnly(string v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        if (DateOnly.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        if (DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
            return DateOnly.FromDateTime(dt);
        return null;
    }

    private static DateTime? ParseDateTime(string v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        if (DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d))
            return d.ToUniversalTime();
        return null;
    }

    private static Guid ParseGuid(string v)
    {
        if (string.IsNullOrWhiteSpace(v)) return Guid.NewGuid();
        if (Guid.TryParse(v, out var g)) return g;

        // Accept 32-hex without hyphens
        if (Regex.IsMatch(v, "^[0-9a-fA-F]{32}$"))
            return Guid.ParseExact(v, "N");

        // Deterministic fallback so seeding stays idempotent across runs
        return FromString("guid:" + v.Trim());
    }

    private static byte[] GetBytes(Dictionary<string, string> row, string key)
    {
        var s = Get(row, key);
        if (string.IsNullOrWhiteSpace(s)) return Array.Empty<byte>();

        try
        {
            // CSV provides base64; decode when valid
            return Convert.FromBase64String(s);
        }
        catch
        {
            // Fallback: treat as UTF-8 literal
            return Encoding.UTF8.GetBytes(s);
        }
    }
}
