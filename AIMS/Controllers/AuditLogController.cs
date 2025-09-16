using System.Linq;
using AIMS.Data;
using AIMS.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
namespace AIMS.Controllers;



[ApiController]
[Route("api/audit")]
public class AuditLogController : Controller
{
    private readonly AimsDbContext _db;
    private readonly AuditLogQuery _auditQuery;
    public AuditLogController(AimsDbContext db, AuditLogQuery auditQuery)
    {
        _db = db;
        _auditQuery = auditQuery;

    }

    [HttpPost("create")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateRecord([FromQuery] CreateAuditRecordDto req)
    {

        try
        {

            int newRecordID = await _auditQuery.createAuditRecordAsync(req);
            return CreatedAtAction(nameof(GetAuditRecord), new { AuditRecordID = newRecordID }, req);
        }
        catch (Exception e)
        {
            return BadRequest(e.Message);
        }
    }

    [HttpGet("get")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAuditRecord([FromQuery] int AuditRecordID)
    {
        var record = await _auditQuery.GetAuditRecordAsync(AuditRecordID);
        if (record is null) return NotFound();
        return Ok(record);
    }

    [HttpGet("list")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllRecords()
    {
        return Ok(await _auditQuery.GetAllAuditRecordsAsync());
    }
}

