using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AIMS.Data;

namespace AIMS.Controllers;

[ApiController]
[Route("api/diag")]
public class DiagnosticsController : ControllerBase
{
    private readonly AimsDbContext _db;
    private readonly UserQuery _userQuery;
    private readonly AssignmentsQuery _assignQuery;
    public DiagnosticsController(AimsDbContext db, UserQuery userQuery, AssignmentsQuery assignQuery)
    {
        _db = db;
        _userQuery = userQuery;
        _assignQuery = assignQuery;
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
            Feedback = await _db.FeedbackEntries.CountAsync(),
            AuditLogs = await _db.AuditLogs.CountAsync(),
            LatestAssignment = await _db.Assignments
                .AsNoTracking()
                .OrderByDescending(a => a.AssignedAtUtc)
                .Select(a => new
                {
                    a.AssignmentID,
                    a.UserID,
                    User = a.User.FullName,
                    a.AssetKind,
                    a.AssetTag,
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

    // ---------- 3) Inspect supervisors/direct reports ----------
    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var users = await _userQuery.GetAllUsersAsync();
        return Ok(users);
    }

    // ---------- 4) Full consolidated asset table (Hardware + Software) ----------
    // GET api/diag/asset-table
    [HttpGet("asset-table")]
    public async Task<IActionResult> GetAssetTable()
    {
        const int HardwareKind = 1;
        const int SoftwareKind = 2;

        var activeAssignments = _db.Assignments
            .AsNoTracking()
            .Where(a => a.UnassignedAtUtc == null)
            .Select(a => new
            {
                AssetKind = (int)a.AssetKind, // normalize enum -> int
                a.AssetTag,
                a.SoftwareID,
                a.UserID
            });

        // Hardware side — identical anonymous shape as software
        var hardwareBase =
            from h in _db.HardwareAssets.AsNoTracking()
            join aa in activeAssignments.Where(x => x.AssetKind == HardwareKind)
                on h.HardwareID equals aa.AssetTag into ha
            from aa in ha.DefaultIfEmpty()
            select new
            {
                AssetName = h.AssetName,
                TypeRaw = h.AssetType, // may be null/empty; format later
                Tag = h.SerialNumber,
                AssignedUserId = (int?)aa.UserID,
                StatusRaw = h.Status // keep DB value; format later if needed
            };

        // Software side — identical anonymous shape
        var softwareBase =
            from s in _db.SoftwareAssets.AsNoTracking()
            join aa in activeAssignments.Where(x => x.AssetKind == SoftwareKind)
                on s.SoftwareID equals aa.SoftwareID into sa
            from aa in sa.DefaultIfEmpty()
            select new
            {
                AssetName = s.SoftwareName,
                TypeRaw = s.SoftwareType,     // don't coalesce here
                Tag = s.SoftwareLicenseKey,
                AssignedUserId = (int?)aa.UserID,
                StatusRaw = (string)null      // compute later (Assigned/Available)
            };

        // Set operation BEFORE any client formatting
        var unionedOrdered = hardwareBase
            .Concat(softwareBase)
            .OrderBy(x => x.TypeRaw)
            .ThenBy(x => x.AssetName);

        // Materialize now
        var raw = await unionedOrdered.ToListAsync();

        // Resolve names in a separate query (post-materialization)
        var userIds = raw.Where(r => r.AssignedUserId.HasValue)
                         .Select(r => r.AssignedUserId!.Value)
                         .Distinct()
                         .ToList();

        var userMap = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.UserID))
            .Select(u => new { u.UserID, u.FullName })
            .ToDictionaryAsync(u => u.UserID, u => u.FullName);

        // Client-side formatting only now
        var rows = raw.Select(x => new
        {
            AssetName = x.AssetName,
            Type = string.IsNullOrWhiteSpace(x.TypeRaw) ? "Software" : x.TypeRaw,
            Tag = x.Tag,
            AssignedTo = x.AssignedUserId.HasValue
                ? $"{(userMap.TryGetValue(x.AssignedUserId.Value, out var name) ? name : "Unknown")} ({x.AssignedUserId.Value})"
                : "Unassigned",
            Status = x.StatusRaw ?? (x.AssignedUserId.HasValue ? "Assigned" : "Available")
        }).ToList();

        return Ok(new
        {
            Headers = new[] { "Asset Name", "Type", "Tag #", "Assigned To", "Status" },
            Rows = rows
        });
    }
}