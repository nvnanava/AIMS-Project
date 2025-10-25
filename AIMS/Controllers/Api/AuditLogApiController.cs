using System.Security.Cryptography;
using System.Text;
using AIMS.Contracts;
using AIMS.Dtos.Audit;
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

        // Parse 'since' (default: 24h ago)
        if (!DateTimeOffset.TryParse(since, out var sinceDto))
            sinceDto = DateTimeOffset.UtcNow.AddDays(-1);

        // Batch size (default 50)
        var max = Math.Clamp(
            HttpContext.Request.Query.TryGetValue("take", out var takeV) && int.TryParse(takeV, out var t) ? t : 50,
            1, 200);

        var events = await _auditQuery.GetAllAuditRecordsAsync(ct);

        // Project → event DTOs we’ll dedupe on Id (ExternalId if present else AuditLogID)
        var projected = events
            .Where(e => e.TimestampUtc > sinceDto.UtcDateTime)
            .Select(e => new AuditEventDto
            {
                Id = (e.ExternalId != Guid.Empty ? e.ExternalId.ToString() : e.AuditLogID.ToString()),
                OccurredAtUtc = e.TimestampUtc,
                Type = e.Action,
                User = $"{e.UserName} ({e.UserID})",
                Target = e.AssetKind == AIMS.Models.AssetKind.Hardware
                    ? (e.HardwareID.HasValue ? $"Hardware#{e.HardwareID}" : "Hardware")
                    : (e.SoftwareID.HasValue ? $"Software#{e.SoftwareID}" : "Software"),
                Details = e.Description ?? "",
                Hash = ComputeHash(e)
            });

        // Deduplicate by Id – keep newest per Id
        var deduped = projected
            .GroupBy(p => p.Id, StringComparer.Ordinal)                            // group by logical id
            .Select(g => g.OrderByDescending(x => x.OccurredAtUtc).First())        // pick newest per id
            .OrderByDescending(x => x.OccurredAtUtc)                                // overall newest first
            .Take(max)                                                              // page
            .ToList();

        var latest = deduped.FirstOrDefault();
        var nextSince = latest?.OccurredAtUtc.ToString("O") ?? sinceDto.UtcDateTime.ToString("O");

        // ETag based on *deduped* page, so 304 works with the collapsed view
        var etagRaw = $"{latest?.Id}|{deduped.Count}";
        var etag = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(etagRaw)));
        HttpContext.Response.Headers.ETag = $"W/\"{etag}\"";

        // If client sent same ETag, signal a cache hit
        if (Request.Headers.TryGetValue("If-None-Match", out var inm) &&
            inm.ToString().Contains(etag, StringComparison.OrdinalIgnoreCase))
        {
            Telemetry.AuditPollEtagHits.Add(1);
            return StatusCode(StatusCodes.Status304NotModified);
        }

        return Ok(new
        {
            items = deduped,
            nextSince
        });

        static string ComputeHash(AIMS.Dtos.Audit.GetAuditRecordDto e)
        {
            var raw =
                $"{e.AuditLogID}|{e.ExternalId}|{e.TimestampUtc:o}|{e.Action}|{e.UserID}|{e.AssetKind}|{e.HardwareID}|{e.SoftwareID}|{e.Description}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        }
    }
}
