using System.Linq;
using AIMS.Data;
using AIMS.Models;
using AIMS.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace AIMS.Controllers;

// Commented out for now, enable when we have EntraID
// [Authorize(Roles = "Admin")]
// With EntraID wired, we gate via policy configured in Program.cs:

//[Authorize(Policy = "mbcAdmin")] Enable/uncomment in Sprint 6 for role based authorization

[ApiController]
[Route("api/software")]
public class SoftwareController : ControllerBase
{
    private readonly AimsDbContext _db;
    private readonly SoftwareQuery _softwareQuery;
    public SoftwareController(AimsDbContext db, SoftwareQuery softwareQuery)
    {
        _db = db;
        _softwareQuery = softwareQuery;
    }

    [HttpGet("get-all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAllSoftware(CancellationToken ct = default)
    {
        var rows = await _softwareQuery.GetAllSoftwareAsync(ct);
        return Ok(rows);
    }

    [HttpPost("add")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddSoftware([FromBody] CreateSoftwareDto dto, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        //validate unique SoftwareLicenseKey
        if (await _db.SoftwareAssets.AnyAsync(s => s.SoftwareLicenseKey == dto.SoftwareLicenseKey, ct))
        {
            ModelState.AddModelError("SoftwareLicenseKey", "A software asset with this license key already exists.");
            return BadRequest(ModelState);
        }

        //validate SoftwareCost is non-negative
        if (dto.SoftwareCost < 0)
        {
            ModelState.AddModelError("SoftwareCost", "Software cost cannot be negative.");
            return BadRequest(ModelState);
        }

        //validate license expiration is not in the past
        if (dto.SoftwareLicenseExpiration.HasValue && dto.SoftwareLicenseExpiration < DateOnly.FromDateTime(DateTime.UtcNow))
        {
            ModelState.AddModelError("SoftwareLicenseExpiration", "License expiration cannot be in the past.");
            return BadRequest(ModelState);
        }

        // Map DTO → Entity
        var software = new Software
        {
            SoftwareName = dto.SoftwareName,
            SoftwareType = dto.SoftwareType,
            SoftwareVersion = dto.SoftwareVersion,
            SoftwareLicenseKey = dto.SoftwareLicenseKey,
            SoftwareLicenseExpiration = dto.SoftwareLicenseExpiration,
            SoftwareUsageData = dto.SoftwareUsageData,
            SoftwareCost = dto.SoftwareCost,
            Comment = dto.Comment
        };

        _db.SoftwareAssets.Add(software);
        await _db.SaveChangesAsync(ct);
        CacheStamp.BumpAssets(); // signal clients to refresh cache

        return CreatedAtAction(
            nameof(GetAllSoftware),   // could also make a GetById and reference it here
            new { id = software.SoftwareID },
            software
        );
    }

    [HttpPut("edit/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EditSoftware(int id, [FromBody] UpdateSoftwareDto dto, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // LINQ-forward (avoid FindAsync for consistent style)
        var software = await _db.SoftwareAssets
            .Where(s => s.SoftwareID == id)
            .SingleOrDefaultAsync(ct);

        if (software == null)
            return NotFound();

        // check for duplicate License Key:
        var existsKey = await _db.SoftwareAssets
        .Where(s => s.SoftwareID != id && s.SoftwareLicenseKey == dto.SoftwareLicenseKey)
        .AnyAsync();

        if (existsKey)
        {
            ModelState.AddModelError("SoftwareLicenseKey", "A software asset with this license key already exists.");
            return BadRequest(ModelState);
        }
        // Update field        if (dto.AssetTag is not null)
        if (dto.SoftwareName is not null)
        {

            software.SoftwareName = dto.SoftwareName;
        }
        if (dto.SoftwareType is not null)
        {
            software.SoftwareType = dto.SoftwareType;
        }
        if (dto.SoftwareVersion is not null)
        {

            software.SoftwareVersion = dto.SoftwareVersion;
        }
        if (dto.SoftwareLicenseKey is not null)
        {

            software.SoftwareLicenseKey = dto.SoftwareLicenseKey;
        }
        if (dto.SoftwareLicenseExpiration is not null)
        {

            software.SoftwareLicenseExpiration = dto.SoftwareLicenseExpiration;
        }
        if (dto.Comment is not null)
        {

            software.Comment = dto.Comment;
        }
        software.SoftwareUsageData = dto.SoftwareUsageData;
        software.SoftwareCost = dto.SoftwareCost;


        await _db.SaveChangesAsync(ct);

        return Ok(software);
    }
}
