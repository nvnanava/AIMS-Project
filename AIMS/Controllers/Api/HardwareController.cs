using AIMS.Data;
using AIMS.Dtos.Assets;
using AIMS.Dtos.Hardware;
using AIMS.Models;
using AIMS.Queries;
using AIMS.Utilities;
using AIMS.ViewModels;
using AIMS.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace AIMS.Controllers.Api;

// [Authorize(Policy = "mbcAdmin")] // enable when ready globally
[ApiController]
[Route("api/hardware")]
public class HardwareController : ControllerBase
{
    private readonly AimsDbContext _db;
    private readonly HardwareQuery _hardwareQuery;
    private readonly HardwareAssetService _hardwareService;



    public HardwareController(AimsDbContext db, HardwareQuery hardwareQuery, HardwareAssetService hardwareService)
    {
        _db = db;
        _hardwareQuery = hardwareQuery;
        _hardwareService = hardwareService;
    }

    [HttpGet("get-all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllHardware(CancellationToken ct = default)
    {
        var rows = await _hardwareQuery.GetAllHardwareAsync(ct);
        return Ok(rows);
    }

    [HttpPost("add")]
    [Authorize(Policy = "mbcAdmin")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddHardware([FromBody] CreateHardwareDto dto, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // unique checks
        if (await _db.HardwareAssets.AnyAsync(h => h.SerialNumber == dto.SerialNumber, ct))
        {
            ModelState.AddModelError(nameof(dto.SerialNumber), "A hardware asset with this serial number already exists.");
            return BadRequest(ModelState);
        }
        if (await _db.HardwareAssets.AnyAsync(h => h.AssetTag == dto.AssetTag, ct))
        {
            ModelState.AddModelError(nameof(dto.AssetTag), "A hardware asset with this asset tag already exists.");
            return BadRequest(ModelState);
        }

        // dates
        if (dto.PurchaseDate > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            ModelState.AddModelError(nameof(dto.PurchaseDate), "Purchase date cannot be in the future.");
            return BadRequest(ModelState);
        }
        if (dto.WarrantyExpiration < dto.PurchaseDate)
        {
            ModelState.AddModelError(nameof(dto.WarrantyExpiration), "Warranty expiration cannot be before purchase date.");
            return BadRequest(ModelState);
        }

        var hardware = new Hardware
        {
            AssetTag = dto.AssetTag.Trim(),
            AssetName = string.IsNullOrWhiteSpace(dto.AssetName)
                ? $"{dto.Manufacturer} {dto.Model}".Trim()
                : dto.AssetName.Trim(),
            AssetType = dto.AssetType.Trim(),
            Status = dto.Status.Trim(),
            Manufacturer = dto.Manufacturer.Trim(),
            Model = dto.Model.Trim(),
            SerialNumber = dto.SerialNumber.Trim(),
            WarrantyExpiration = dto.WarrantyExpiration,
            PurchaseDate = dto.PurchaseDate,
            Comment = (dto.Comment ?? string.Empty).Trim()
        };

        _db.HardwareAssets.Add(hardware);
        await _db.SaveChangesAsync(ct);

        CacheStamp.BumpAssets();

        return CreatedAtAction(nameof(GetAllHardware), new { id = hardware.HardwareID }, hardware);
    }

    [HttpPost("add-bulk")]
    [Authorize(Policy = "mbcAdmin")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddHardwareBulk([FromBody] BulkHardwareRequest req, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var created = await _hardwareService.AddHardwareBulkAsync(req, ct);
            return CreatedAtAction(nameof(GetAllHardware), null, created);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return BadRequest(ModelState);
        }
        catch (Exception ex)
        {
            return ValidationProblem(ex.Message);
        }
    }

    [HttpPut("edit/{id}")]
    [Authorize(Policy = "mbcAdmin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EditHardware(int id, [FromBody] UpdateHardwareDto dto, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var hardware = await _db.HardwareAssets
            .Where(h => h.HardwareID == id)
            .SingleOrDefaultAsync(ct);

        if (hardware == null)
            return NotFound();

        var validationResult = await _hardwareService.ValidateEditAsync(hardware, dto, id, ModelState, ct);
        if (validationResult != null)
            return validationResult;

        HardwareAssetService.ApplyEdit(dto, hardware);


        await _db.SaveChangesAsync(ct);
        CacheStamp.BumpAssets();

        return Ok(hardware);
    }

    [HttpPut("archive/{id}")]
    [Authorize(Policy = "mbcAdmin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ArchiveHardware(int id, CancellationToken ct = default)
    {
        var hardware = await _db.HardwareAssets
            .IgnoreQueryFilters()
            .Where(h => h.HardwareID == id)
            .SingleOrDefaultAsync(ct);

        if (hardware == null)
            return NotFound();

        hardware.IsArchived = true;
        hardware.Status = "Archived";

        // unassign if assigned
        var assignment = await _db.Assignments
            .Where(a => a.HardwareID == id && a.UnassignedAtUtc == null)
            .SingleOrDefaultAsync(ct);
        if (assignment != null)
        {
            assignment.UnassignedAtUtc = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
        CacheStamp.BumpAssets();

        var Asset = await _db.HardwareAssets
        .IgnoreQueryFilters()
        .Where(a => a.HardwareID == id)
        .Select(a => new AssetRowDto
        {
            HardwareID = a.HardwareID,
            AssetName = a.AssetName,
            Type = a.AssetType,
            Tag = a.AssetTag,
            Status = "Archived",
            IsArchived = true,
            AssignedUserId = null,
            AssignedTo = "Unassigned"
        })
    .SingleOrDefaultAsync();
        if (Asset == null)
        {
            return NotFound();
        }

        return Ok(Asset);
    }

    [HttpPut("unarchive/{id}")]
    [Authorize(Policy = "mbcAdmin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnarchiveHardware(int id, CancellationToken ct = default)
    {
        var hardware = await _db.HardwareAssets
            .IgnoreQueryFilters()
            .Where(h => h.HardwareID == id)
            .SingleOrDefaultAsync(ct);

        if (hardware == null)
            return NotFound();

        hardware.IsArchived = false;
        hardware.Status = "Available";

        await _db.SaveChangesAsync(ct);
        CacheStamp.BumpAssets();

        var Asset = await _db.HardwareAssets
        .Where(a => a.HardwareID == id)
        .Select(a => new AssetRowDto
        {
            HardwareID = a.HardwareID,
            AssetName = a.AssetName,
            Type = a.AssetType,
            Tag = a.AssetTag,
            Status = "Available",
            IsArchived = false,
            AssignedUserId = null,
            AssignedTo = "Unassigned"
        })
    .SingleOrDefaultAsync();

        if (Asset == null)
            return NotFound();

        return Ok(Asset);

    }

    [HttpPost("check-duplicates")]
    [Authorize(Policy = "mbcAdmin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckDuplicates([FromBody] BulkHardwareRequest req, CancellationToken ct = default)
    {
        var serials = req.Dtos?
            .Select(d => (d.SerialNumber ?? "").Trim())
            .Where(s => s != "")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new();

        var tags = req.Dtos?
            .Select(d => (d.AssetTag ?? "").Trim())
            .Where(t => t != "")
            .Distinct() // case-sensitive; switch to OrdinalIgnoreCase if desired
            .ToList() ?? new();

        var existingSerials = await _db.HardwareAssets
            .Where(h => h.SerialNumber != null && serials.Contains(h.SerialNumber!))
            .Select(h => h.SerialNumber!)
            .ToListAsync(ct);

        var existingTags = await _db.HardwareAssets
            .Where(h => h.AssetTag != null && tags.Contains(h.AssetTag!))
            .Select(h => h.AssetTag!)
            .ToListAsync(ct);

        return Ok(new { existingSerials, existingTags });
    }
}
