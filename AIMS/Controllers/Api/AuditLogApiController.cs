using AIMS.Dtos.Audit;
using Microsoft.AspNetCore.Mvc;

namespace AIMS.Controllers.Api;

[ApiController]
[Route("api/audit")]
public class AuditLogApiController : ControllerBase
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
}
