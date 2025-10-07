using AIMS.Data;
using AIMS.Models;
using AIMS.Queries;
using AIMS.Utilities;
using AIMS.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Controllers;

//[Authorize(Policy = "mbcAdmin")]
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
    public async Task<IActionResult> GetAllSoftware(CancellationToken ct = default)
    {
        var rows = await _softwareQuery.GetAllSoftwareAsync(ct);
        return Ok(rows);
    }

    [HttpPost("add")]
    [Authorize(Policy = "mbcAdmin")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddSoftware([FromBody] CreateSoftwareDto dto, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // unique license key
        if (await _db.SoftwareAssets.AnyAsync(s => s.SoftwareLicenseKey == dto.SoftwareLicenseKey, ct))
        {
            ModelState.AddModelError(nameof(dto.SoftwareLicenseKey), "A software asset with this license key already exists.");
            return BadRequest(ModelState);
        }

        // non-negative checks
        if (dto.SoftwareCost < 0)
        {
            ModelState.AddModelError(nameof(dto.SoftwareCost), "Software cost cannot be negative.");
            return BadRequest(ModelState);
        }
        if (dto.SoftwareUsageData < 0)
        {
            ModelState.AddModelError(nameof(dto.SoftwareUsageData), "Usage cannot be negative.");
            return BadRequest(ModelState);
        }
        if (dto.LicenseSeatsUsed < 0 || dto.LicenseTotalSeats < 0 || dto.LicenseSeatsUsed > dto.LicenseTotalSeats)
        {
            ModelState.AddModelError(nameof(dto.LicenseSeatsUsed), "License seats used must be between 0 and total seats.");
            return BadRequest(ModelState);
        }

        // expiry cannot be in past (if provided)
        if (dto.SoftwareLicenseExpiration.HasValue &&
            dto.SoftwareLicenseExpiration < DateOnly.FromDateTime(DateTime.UtcNow))
        {
            ModelState.AddModelError(nameof(dto.SoftwareLicenseExpiration), "License expiration cannot be in the past.");
            return BadRequest(ModelState);
        }

        var software = new Software
        {
            SoftwareName = dto.SoftwareName.Trim(),
            SoftwareType = dto.SoftwareType.Trim(),
            SoftwareVersion = (dto.SoftwareVersion ?? string.Empty).Trim(),
            SoftwareLicenseKey = dto.SoftwareLicenseKey.Trim(),
            SoftwareLicenseExpiration = dto.SoftwareLicenseExpiration,
            SoftwareUsageData = dto.SoftwareUsageData,
            SoftwareCost = dto.SoftwareCost,
            LicenseTotalSeats = dto.LicenseTotalSeats,
            LicenseSeatsUsed = dto.LicenseSeatsUsed,
            Comment = (dto.Comment ?? string.Empty).Trim()
        };

        _db.SoftwareAssets.Add(software);
        await _db.SaveChangesAsync(ct);
        CacheStamp.BumpAssets();

        return CreatedAtAction(nameof(GetAllSoftware), new { id = software.SoftwareID }, software);
    }

    [HttpPut("edit/{id}")]
    [Authorize(Policy = "mbcAdmin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EditSoftware(int id, [FromBody] UpdateSoftwareDto dto, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var software = await _db.SoftwareAssets
            .Where(s => s.SoftwareID == id)
            .SingleOrDefaultAsync(ct);

        if (software == null)
            return NotFound();

        // if license key provided, ensure unique
        if (dto.SoftwareLicenseKey is not null)
        {
            var existsKey = await _db.SoftwareAssets
                .AnyAsync(s => s.SoftwareID != id && s.SoftwareLicenseKey == dto.SoftwareLicenseKey, ct);
            if (existsKey)
            {
                ModelState.AddModelError(nameof(dto.SoftwareLicenseKey), "A software asset with this license key already exists.");
                return BadRequest(ModelState);
            }
            software.SoftwareLicenseKey = dto.SoftwareLicenseKey;
        }

        // assign only if provided (partial updates)
        if (dto.SoftwareName is not null) software.SoftwareName = dto.SoftwareName;
        if (dto.SoftwareType is not null) software.SoftwareType = dto.SoftwareType;
        if (dto.SoftwareVersion is not null) software.SoftwareVersion = dto.SoftwareVersion;
        if (dto.SoftwareLicenseExpiration is not null)
        {
            if (dto.SoftwareLicenseExpiration.Value < DateOnly.FromDateTime(DateTime.UtcNow))
            {
                ModelState.AddModelError(nameof(dto.SoftwareLicenseExpiration), "License expiration cannot be in the past.");
                return BadRequest(ModelState);
            }
            software.SoftwareLicenseExpiration = dto.SoftwareLicenseExpiration;
        }
        if (dto.SoftwareUsageData is not null)
        {
            if (dto.SoftwareUsageData.Value < 0)
            {
                ModelState.AddModelError(nameof(dto.SoftwareUsageData), "Usage cannot be negative.");
                return BadRequest(ModelState);
            }
            software.SoftwareUsageData = dto.SoftwareUsageData.Value;
        }
        if (dto.SoftwareCost is not null)
        {
            if (dto.SoftwareCost.Value < 0)
            {
                ModelState.AddModelError(nameof(dto.SoftwareCost), "Software cost cannot be negative.");
                return BadRequest(ModelState);
            }
            software.SoftwareCost = dto.SoftwareCost.Value;
        }
        if (dto.LicenseTotalSeats is not null)
        {
            if (dto.LicenseTotalSeats.Value < 0)
            {
                ModelState.AddModelError(nameof(dto.LicenseTotalSeats), "Total seats cannot be negative.");
                return BadRequest(ModelState);
            }
            software.LicenseTotalSeats = dto.LicenseTotalSeats.Value;
        }
        if (dto.LicenseSeatsUsed is not null)
        {
            if (dto.LicenseSeatsUsed.Value < 0)
            {
                ModelState.AddModelError(nameof(dto.LicenseSeatsUsed), "Seats used cannot be negative.");
                return BadRequest(ModelState);
            }
            software.LicenseSeatsUsed = dto.LicenseSeatsUsed.Value;
        }
        // cross-field seat check if either changed
        if (dto.LicenseSeatsUsed is not null || dto.LicenseTotalSeats is not null)
        {
            if (software.LicenseSeatsUsed > software.LicenseTotalSeats)
            {
                ModelState.AddModelError(nameof(dto.LicenseSeatsUsed), "Seats used cannot exceed total seats.");
                return BadRequest(ModelState);
            }
        }

        if (dto.Comment is not null) software.Comment = dto.Comment;

        await _db.SaveChangesAsync(ct);
        CacheStamp.BumpAssets();

        return Ok(software);
    }
}
