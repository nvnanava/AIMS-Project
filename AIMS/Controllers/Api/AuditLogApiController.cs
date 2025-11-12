/* ======================================================================
   AIMS API: AuditLogApiController (GetEventsSince refactor + latest)
   ----------------------------------------------------------------------
   Purpose
   - Audit Log CRUD + search endpoints.
   - Lightweight polling endpoint (/api/audit/events) with ETag and
     deduplication to reduce payload churn.
   - NEW: /api/audit/events/latest for first-paint seeding (ignore 'since').
   ====================================================================== */

using System.Security.Cryptography;
using System.Text;
using AIMS.Contracts;
using AIMS.Dtos.Audit;
using AIMS.Dtos.Common;
using AIMS.Models;
using AIMS.Queries;
using AIMS.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AIMS.Controllers.Api;

[ApiController]
[Route("api/audit")]
public partial class AuditLogApiController : ControllerBase
{
    private readonly AuditLogQuery _auditQuery;
    public AuditLogApiController(AuditLogQuery auditQuery) => _auditQuery = auditQuery;

    [HttpPost("create")]
    [ProducesResponseType(typeof(void), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateRecord([FromBody] CreateAuditRecordDto req, CancellationToken ct)
    {
        try
        {
            var id = await _auditQuery.CreateAuditRecordAsync(req, ct);
            return CreatedAtAction(nameof(GetAuditRecord), new { auditLogId = id }, null);
        }
        catch (Exception e)
        {
            return BadRequest(new { error = e.Message });
        }
    }

    [HttpGet("get")]
    [ProducesResponseType(typeof(GetAuditRecordDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAuditRecord([FromQuery] int auditLogId, CancellationToken ct)
    {
        var record = await _auditQuery.GetAuditRecordAsync(auditLogId, ct);
        if (record is null) return NotFound();
        return Ok(record);
    }

    [HttpGet("list")]
    [ProducesResponseType(typeof(List<GetAuditRecordDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllRecords(CancellationToken ct)
    {
        var rows = await _auditQuery.GetAllAuditRecordsAsync(ct);
        return Ok(rows);
    }

    // New endpoint: fetch recent logs by asset type/id (for modal previews)
    [HttpGet("recent")]
    [ProducesResponseType(typeof(List<GetAuditRecordDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecentLogs(
        [FromQuery] string assetKind,
        [FromQuery] int assetId,
        [FromQuery] int take = 5,
        CancellationToken ct = default)
    {
        var rows = await _auditQuery.GetRecentAuditRecordsAsync(assetKind, assetId, take, ct);
        return Ok(rows);
    }

    // ---------------------------------------------------------------------
    // Paged Audit Log search (Task 1B backend)
    // Absolute route so it lives at /api/auditlog to match spec
    // ---------------------------------------------------------------------
    [HttpGet("~/api/auditlog")]
    [ProducesResponseType(typeof(PagedResult<AuditLogRowDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? q = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? actor = null,
        [FromQuery] string? action = null,
        [FromQuery] AssetKind? kind = null,
        [FromQuery] int? hardwareId = null,
        [FromQuery] int? softwareId = null,
        [FromQuery] bool includeArchived = false,
        CancellationToken ct = default)
    {
        var result = await _auditQuery.SearchAsync(
            page, pageSize, q, from, to, actor, action, kind, hardwareId, softwareId, ct);
        return Ok(result);
    }
}

public partial class AuditLogApiController
{
    // Polling fallback: newest events since timestamp
    [HttpGet("events")]
    [EnableRateLimiting("audit-poll")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEventsSince([FromQuery] string since, CancellationToken ct)
    {
        Telemetry.AuditPollRequests.Add(1);

        var sinceUtc = ParseSinceOrDefault(since);
        var max = ReadAndClampTake(HttpContext.Request.Query);

        var all = await _auditQuery.GetEventsWindowAsync(sinceUtc, max, ct);
        var page = ProjectDedupAndPage(all, sinceUtc, max);

        var etag = BuildEtag(page);
        HttpContext.Response.Headers.ETag = $"W/\"{etag}\"";

        if (IsNotModified(Request.Headers, etag))
        {
            Telemetry.AuditPollEtagHits.Add(1);
            return StatusCode(StatusCodes.Status304NotModified);
        }

        var latest = page.FirstOrDefault();
        var nextSince = (latest?.OccurredAtUtc ?? sinceUtc).ToString("O");

        return Ok(new
        {
            items = page,
            nextSince
        });
    }

    // First-paint seed — ignore 'since', just return latest N
    [HttpGet("events/latest")]
    [EnableRateLimiting("audit-poll-soft")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLatest(CancellationToken ct)
    {
        var max = ReadAndClampTake(HttpContext.Request.Query);
        var all = await _auditQuery.GetEventsWindowAsync(DateTime.MinValue, max, ct);

        // Reuse projection/dedup; pass a 'since' far in the past so we only limit by Take(max)
        var page = ProjectDedupAndPage(all, DateTime.MinValue, max);

        var latest = page.FirstOrDefault();
        var nextSince = (latest?.OccurredAtUtc ?? DateTimeOffset.UtcNow.UtcDateTime).ToString("O");

        var etag = BuildEtag(page);
        HttpContext.Response.Headers.ETag = $"W/\"{etag}\"";

        return Ok(new
        {
            items = page,
            nextSince
        });
    }

    // ---------------------- tiny helpers (keep action thin) ------------------

    private static DateTime ParseSinceOrDefault(string since)
        => DateTimeOffset.TryParse(since, out var dto)
            ? dto.UtcDateTime
            : DateTimeOffset.UtcNow.AddDays(-30).UtcDateTime; // ← 30d to match JS

    private static int ReadAndClampTake(IQueryCollection q)
    {
        var take = (q.TryGetValue("take", out var v) && int.TryParse(v, out var n)) ? n : 50;
        return Math.Clamp(take, 1, 200);
    }

    private static List<AuditEventDto> ProjectDedupAndPage(
        List<GetAuditRecordDto> events,
        DateTime sinceUtc,
        int max)
    {
        var projected = events
            .Where(e => e.TimestampUtc > sinceUtc)
            .Select(e =>
            {
                var last = e.Changes?.Count > 0 ? e.Changes[^1] : null; // last change
                return new AuditEventDto
                {
                    Id = (e.ExternalId != Guid.Empty ? e.ExternalId.ToString() : e.AuditLogID.ToString()),
                    OccurredAtUtc = e.TimestampUtc,
                    Type = e.Action,
                    User = $"{e.UserName} ({e.UserID})",
                    Target = e.AssetKind == AssetKind.Hardware
                        ? (e.HardwareID.HasValue ? $"Hardware#{e.HardwareID}" : "Hardware")
                        : (e.SoftwareID.HasValue ? $"Software#{e.SoftwareID}" : "Software"),
                    Details = e.Description ?? "",
                    ChangeField = last?.Field,
                    PrevValue = last?.OldValue,
                    NewValue = last?.NewValue,
                    Hash = ComputeHash(e)
                };
            });

        // Deduplicate by Id, newest first
        var deduped = projected
            .GroupBy(p => p.Id, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(x => x.OccurredAtUtc).First())
            .OrderByDescending(x => x.OccurredAtUtc)
            .Take(max)
            .ToList();

        return deduped;
    }

    private static string BuildEtag(List<AuditEventDto> page)
    {
        const string SchemaV = "v2"; // bump when payload shape changes
        var latestId = page.FirstOrDefault()?.Id ?? "";
        var raw = $"{latestId}|{page.Count}|{SchemaV}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    private static bool IsNotModified(IHeaderDictionary headers, string etag)
        => headers.TryGetValue("If-None-Match", out var inm)
           && inm.ToString().Contains(etag, StringComparison.OrdinalIgnoreCase);

    private static string ComputeHash(GetAuditRecordDto e)
    {
        var raw =
            $"{e.AuditLogID}|{e.ExternalId}|{e.TimestampUtc:o}|{e.Action}|{e.UserID}|{e.AssetKind}|{e.HardwareID}|{e.SoftwareID}|{e.Description}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }
}
