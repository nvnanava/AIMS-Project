# AIMS Database Seeding & Docker Guide

This document explains how the **AIMS seeding process** works across environments (`dev`, `prod`, `test`) and how to control it via Docker scripts, environment variables, and CSV inputs.  
It includes **sample commands**, **CSV formats**, and **post-seed recomputation** details (e.g., software license seat recalculation).

---

## ⚙️ 1. Overview

AIMS supports three database seed modes controlled via environment variables or CLI flags:

| Mode | Description |
|------|--------------|
| `basic` | Seeds from built-in default sample data only. |
| `csv` | Loads all records from CSVs inside the seed directory (`/src/seed`). |
| `merge` | Loads CSVs first, then tops up any missing data using the default sample data. |

This flexibility allows you to seed real-world data for demos, testing, or migrations while still maintaining internal references for development.

---

## 🧱 2. Environment Variables

| Variable | Default | Description |
|-----------|----------|-------------|
| `AIMS_SEED_MODE` | `basic` | Controls seed mode (`basic`, `csv`, `merge`). |
| `AIMS_SEED_DIR` | `/src/seed` | Directory containing seed CSV files. |
| `AIMS_ALLOW_PROD_SEED` | `false` | Prevents accidental seeding in production unless explicitly enabled. |
| `AIMS_SEED_LOG` | _(unset)_ | Enables verbose debug logging during seeding. Set to `debug` for detailed output. |

**Example:**
```bash
export AIMS_SEED_MODE=csv
export AIMS_SEED_DIR=./seed
export AIMS_SEED_LOG=debug
```

---

## 🐳 3. Docker Build Scripts

AIMS provides two key scripts for managing database setup and seeding in Docker environments.

### 3.1. `build_containers.sh`

Used during **first-time setup** or complete environment rebuild.

```bash
# Default sample data
./scripts/build_containers.sh dev

# Load data from CSVs in ./seed/
./scripts/build_containers.sh dev --seed-mode csv --seed-dir ./seed

# Merge CSV data + sample
./scripts/build_containers.sh dev --seed-mode merge

# Allow production seeding (use with caution!)
./scripts/build_containers.sh prod --allow-prod-seed
```

This script:
- Creates containers for SQL Server and the web API.
- Waits for the DB to be healthy.
- Runs EF Core migrations automatically.
- Calls the seeding logic based on our specified `AIMS_SEED_MODE`.

---

### 3.2. `db_ready.sh`

Use this when you want to reseed or rebuild the database without destroying containers.

```bash
# Ensure DB exists (no reseed)
./scripts/db_ready.sh dev ensure

# Drop & recreate DB, then seed
./scripts/db_ready.sh dev reseed

# Hard reseed (also clears container volume)
./scripts/db_ready.sh dev reseed --hard

# Reseed from CSVs
./scripts/db_ready.sh dev reseed --seed-mode csv --seed-dir ./seed

# Allow production reseeding (very dangerous)
./scripts/db_ready.sh prod reseed --allow-prod-seed
```

---

## 🧩 4. How Seeding Works Internally

The **DbSeeder** class automatically runs migrations and seeds based on the detected mode:

1. **Runs migrations** via:
   ```csharp
   await db.Database.MigrateAsync();
   ```

2. **Checks mode** using environment variables or CLI flags.

3. Executes one of:
   - `SeedSampleAsync()` → Built-in sample users, hardware, and software.
   - `SeedFromCsvAsync()` → Reads all CSVs and inserts data idempotently.
   - `SeedMergeAsync()` → Combines both (CSV + sample top-up).

4. **Final recomputation step**:
   After loading all Assignments, it automatically calls:
   ```csharp
   await RecomputeSoftwareSeatsAsync();
   ```
   This recalculates `LicenseSeatsUsed` for all software based on open (active) assignments.

---

## 🧾 5. CSV Format Reference

All CSVs must live under the folder set by `AIMS_SEED_DIR` (default `/src/seed`).

### 5.1. `roles.csv`

| Column | Example | Notes |
|---------|----------|-------|
| RoleName | Admin | Must match existing role names used in AIMS. |

---

### 5.2. `users.csv`

| Column | Example | Notes |
|---------|----------|-------|
| FullName | John Smith | Required |
| Email | john.smith@agency.ca.gov | Must be unique |
| RoleName | Admin | Must reference an existing role |
| OfficeName | Sacramento HQ | Optional |
| SupervisorEmail | jane.doe@agency.ca.gov | Optional — matched to `Users.Email` |

---

### 5.3. `hardware.csv`

| Column | Example | Notes |
|---------|----------|-------|
| AssetName | Laptop 123 | Required |
| AssetType | Laptop | Used in thresholds |
| SerialNumber | ABC12345 | Unique |
| Manufacturer | Dell | Optional |
| Model | Latitude 7420 | Optional |
| Status | Available | Optional |
| IsArchived | false | Optional |

---

### 5.4. `software.csv`

| Column | Example | Notes |
|---------|----------|-------|
| SoftwareName | Microsoft 365 | Required |
| SoftwareType | Productivity | Required |
| SoftwareVersion | 2024 | Optional |
| SoftwareLicenseKey | ABCD-EFGH-IJKL-MNOP | Unique license key |
| SoftwareLicenseExpiration | 2026-12-31 | ISO date |
| SoftwareUsageData | 0 | Optional |
| SoftwareCost | 99.99 | Decimal |
| LicenseTotalSeats | 50 | Required |
| LicenseSeatsUsed | 0 | Auto-updated via recompute |
| Comment | N/A | Optional |
| IsArchived | false | Optional |

---

### 5.5. `assignments.csv`

| Column | Example | Notes |
|---------|----------|-------|
| UserEmail | john.smith@agency.ca.gov | Must match a user in `users.csv` |
| AssetKind | Hardware | Either `Hardware` or `Software` |
| AssetTag | ABC12345 | SerialNumber (Hardware) or LicenseKey (Software) |
| AssignedAtUtc | 2025-01-02T00:00:00Z | ISO timestamp |
| Comment | Issued for field use | Optional |

After importing, **RecomputeSoftwareSeatsAsync()** ensures:
```csharp
LicenseSeatsUsed = COUNT(*) FROM Assignments WHERE AssetKind=Software AND UnassignedAtUtc IS NULL;
```

---

## 🧮 6. Recomputing Software Seats

The recomputation occurs **after** assignment seeding inside `SeedFromCsvAsync()`:

```csharp
await db.Assignments.AddRangeAsync(assignments);
await db.SaveChangesAsync();

// Recalculate seats for all software
await RecomputeSoftwareSeatsAsync();
```

### Implementation

```csharp
private async Task RecomputeSoftwareSeatsAsync()
{
    var softwareList = await _db.SoftwareAssets.ToListAsync();

    foreach (var s in softwareList)
    {
        s.LicenseSeatsUsed = await _db.Assignments
            .CountAsync(a => a.AssetKind == AssetKind.Software &&
                             a.SoftwareID == s.SoftwareID &&
                             a.UnassignedAtUtc == null);
    }

    await _db.SaveChangesAsync();
}
```

You can also trigger this manually in the Diagnostics Controller:
```csharp
await _db.Database.ExecuteSqlRawAsync("EXEC dbo.RecomputeSoftwareSeats");
```

---

## 🧰 7. Debugging Tips

- To enable row-level seeding logs:
  ```bash
  export AIMS_SEED_LOG=debug
  ```
  This will print every applied/ignored row during seeding.

- To inspect table counts after seeding:
  ```bash
  docker exec -it sqlserver-dev /opt/mssql-tools18/bin/sqlcmd     -S localhost -U sa -P 'StrongP@ssword!' -No -C -Q "SELECT COUNT(*) FROM AIMS.dbo.Users"
  ```

- To verify assignments:
  ```bash
  SELECT COUNT(*) FROM AIMS.dbo.Assignments WHERE UnassignedAtUtc IS NULL;
  ```

---

## 🚀 8. Quick Start Summary

| Task | Command |
|------|----------|
| Build dev containers with sample data | `./scripts/build_containers.sh dev` |
| Build dev containers with CSV seed | `./scripts/build_containers.sh dev --seed-mode csv --seed-dir ./seed` |
| Merge CSV + sample | `./scripts/build_containers.sh dev --seed-mode merge` |
| Reseed DB (drop & recreate) | `./scripts/db_ready.sh dev reseed --hard` |
| Enable seeding in prod | `./scripts/build_containers.sh prod --allow-prod-seed` |
| Enable debug logging | `export AIMS_SEED_LOG=debug` |

---

## 🧹 9. Forcing a Reseed

To completely reseed the database manually:
```bash
docker exec -it web-dev dotnet ef database drop --force --context AimsDbContext
docker exec -it web-dev dotnet ef database update
docker exec -it web-dev dotnet run -- --seed-mode csv
```

Alternatively, delete the seed history table:
```sql
DELETE FROM AIMS.dbo.AIMS_SeedHistory;
```

This resets the seed lock so the seeder runs fresh next time.

---

## ✅ 10. Summary

After these changes:
- Assignments correctly seed based on natural keys (`SerialNumber`, `LicenseKey`).
- The system **auto-updates software seat counts** after seeding.
- The entire seeding process is configurable via Docker or environment variables.
- You can switch between sample, CSV, or merge modes without schema edits.
