#if DEBUG
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIMS.Data;
using AIMS.Models;
using AIMS.Queries;
using AIMS.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Controllers;

[ApiController]
[Route("api/diag")]
public class DiagnosticsController : ControllerBase
{
    private readonly AimsDbContext _db;
    private readonly UserQuery _userQuery;
    private readonly AssignmentsQuery _assignQuery;
    private readonly AssetQuery _assetQuery;

    public DiagnosticsController(
        AimsDbContext db,
        UserQuery userQuery,
        AssignmentsQuery assignQuery,
        AssetQuery assetQuery)
    {
        _db = db;
        _userQuery = userQuery;
        _assignQuery = assignQuery;
        _assetQuery = assetQuery;
    }

    // ---------- 1) Quick sanity: table counts ----------
    [HttpGet("db-summary")]
    public async Task<IActionResult> GetDbSummary()
    {
        var summary = new
        {
            Users = await _db.Users.CountAsync(),
            Roles = await _db.Roles.CountAsync(),
            Hardware = await _db.HardwareAssets.CountAsync(),
            Software = await _db.SoftwareAssets.CountAsync(),
            Assignments = await _db.Assignments.CountAsync(),
            AuditLogs = await _db.AuditLogs.CountAsync(),
            LatestAssignment = await _db.Assignments
                .AsNoTracking()
                .OrderByDescending(a => a.AssignedAtUtc)
                .Select(a => new
                {
                    a.AssignmentID,
                    a.UserID,
                    User = a.User != null ? a.User.FullName : null,
                    a.AssetKind,
                    a.HardwareID,          // <— was AssetTag
                    a.SoftwareID,
                    a.AssignedAtUtc,
                    a.UnassignedAtUtc
                })
                .FirstOrDefaultAsync()
        };
        return Ok(summary);
    }

    // ---------- 2) Show active assignments with who/what ----------
    [HttpGet("active-assignments")]
    public async Task<IActionResult> GetActiveAssignments()
    {
        var rows = await _assignQuery.GetAllAssignmentsAsync();
        return Ok(rows);
    }

    // ---------- 3) Inspect users (incl. role + supervisor id) ----------
    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var users = await _userQuery.GetAllUsersAsync();
        return Ok(users);
    }

    // ---------- 4) Full consolidated asset table (Hardware + Software) ----------
    [HttpGet("asset-table")]
    public async Task<IActionResult> GetAssetTable()
    {
        const int HardwareKind = 1;
        const int SoftwareKind = 2;

        var active = _db.Assignments.AsNoTracking()
            .Where(a => a.UnassignedAtUtc == null)
            .Select(a => new { AssetKind = (int)a.AssetKind, a.HardwareID, a.SoftwareID, a.UserID }); // <— was AssetTag

        var hardware =
            from h in _db.HardwareAssets.AsNoTracking()
            join aa in active.Where(x => x.AssetKind == HardwareKind)
                on h.HardwareID equals aa.HardwareID into ha             // <— was aa.AssetTag
            from aa in ha.DefaultIfEmpty()
            select new
            {
                h.AssetName,
                TypeRaw = h.AssetType,
                Tag = h.SerialNumber,
                AssignedUserId = (int?)aa.UserID,
                StatusRaw = h.Status
            };

        var software =
            from s in _db.SoftwareAssets.AsNoTracking()
            join aa in active.Where(x => x.AssetKind == SoftwareKind)
                on s.SoftwareID equals aa.SoftwareID into sa
            from aa in sa.DefaultIfEmpty()
            select new
            {
                AssetName = s.SoftwareName,
                TypeRaw = s.SoftwareType,
                Tag = s.SoftwareLicenseKey,
                AssignedUserId = (int?)aa.UserID,
                StatusRaw = ""
            };

        var raw = await hardware.Concat(software)
            .OrderBy(x => x.TypeRaw)
            .ThenBy(x => x.AssetName)
            .ToListAsync();

        return Ok(raw.Select(x => new
        {
            x.AssetName,
            Type = string.IsNullOrWhiteSpace(x.TypeRaw) ? "Software" : x.TypeRaw!,
            x.Tag,
            Assigned = x.AssignedUserId.HasValue,
            Status = !string.IsNullOrWhiteSpace(x.StatusRaw)
                ? x.StatusRaw
                : (x.AssignedUserId.HasValue ? "Assigned" : "Available")
        }));
    }

    // ---------- 5) Search available assets ----------
    // GET /api/diag/assets?q=foo
    [HttpGet("assets")]
    [Produces("application/json")]
    public async Task<IActionResult> GetAvailableAssets(
       [FromQuery(Name = "q")] string? searchString,
       [FromQuery] int take = 50,
       CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 100);

        // Available hardware = hardware with NO open assignment
        var availableHardware = _db.HardwareAssets.AsNoTracking()
            .Where(h => !_db.Assignments.Any(a =>
                a.UnassignedAtUtc == null &&
                a.AssetKind == AssetKind.Hardware &&
                a.HardwareID == h.HardwareID))               // <— was a.AssetTag
            .Select(h => new AssetLookupItem
            {
                AssetID = h.HardwareID,
                AssetName = h.AssetName,
                AssetKind = (int)AssetKind.Hardware
            });

        // Available software = software with NO open assignment
        var availableSoftware = _db.SoftwareAssets.AsNoTracking()
            .Where(s => !_db.Assignments.Any(a =>
                a.UnassignedAtUtc == null &&
                a.AssetKind == AssetKind.Software &&
                a.SoftwareID == s.SoftwareID))
            .Select(s => new AssetLookupItem
            {
                AssetID = s.SoftwareID,
                AssetName = s.SoftwareName,
                AssetKind = (int)AssetKind.Software
            });

        // Union + optional search
        var query = availableHardware.Concat(availableSoftware);

        if (!string.IsNullOrWhiteSpace(searchString))
        {
            var term = searchString.Trim().ToLower();
            query = query.Where(x => x.AssetName.ToLower().Contains(term));
        }

        var results = await query
            .OrderBy(x => x.AssetName)
            .Take(take)
            .ToListAsync(ct);

        return Ok(results);
    }
}
#endif
