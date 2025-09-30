using System.Linq;
using AIMS.Data;
using AIMS.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AIMS.Utilities;

namespace AIMS.Controllers;

// Commented out for now, enable when we have EntraID
// [Authorize(Roles = "Admin")]
// With EntraID wired, we gate via policy configured in Program.cs:

//[Authorize(Policy = "mbcAdmin")] enable in Sprint 6 for role based authorization
[ApiController]
[Route("api/hardware")]
public class HardwareController : ControllerBase
{
    private readonly AimsDbContext _db;
    private readonly HardwareQuery _hardwareQuery;
    public HardwareController(AimsDbContext db, HardwareQuery hardwareQuery)
    {
        _db = db;
        _hardwareQuery = hardwareQuery;
    }

    [HttpGet("get-all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAllHardware(CancellationToken ct = default)
    {
        var rows = await _hardwareQuery.GetAllHardwareAsync(ct);
        return Ok(rows);
    }

    [HttpPost("add")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddHardware([FromBody] CreateHardwareDto dto, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        // check for unique SerialNumber
        if (await _db.HardwareAssets.AnyAsync(h => h.SerialNumber == dto.SerialNumber, ct))
        {
            ModelState.AddModelError("SerialNumber", "A hardware asset with this serial number already exists.");
            return BadRequest(ModelState);
        }
        // check for unique AssetTag
        if (await _db.HardwareAssets.AnyAsync(h => h.AssetTag == dto.AssetTag, ct))
        {
            ModelState.AddModelError("AssetTag", "A hardware asset with this asset tag already exists.");
            return BadRequest(ModelState);
        }
        // validate dates
        if (dto.PurchaseDate > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            ModelState.AddModelError("PurchaseDate", "Purchase date cannot be in the future.");
            return BadRequest(ModelState);
        }
        if (dto.WarrantyExpiration < dto.PurchaseDate)
        {
            ModelState.AddModelError("WarrantyExpiration", "Warranty expiration cannot be before purchase date.");
            return BadRequest(ModelState);
        }

        // Map DTO → Entity
        var hardware = new Hardware
        {
            AssetTag = dto.AssetTag,
            AssetName = dto.AssetName,
            AssetType = dto.AssetType,
            Status = dto.Status,
            Manufacturer = dto.Manufacturer,
            Model = dto.Model,
            SerialNumber = dto.SerialNumber,
            WarrantyExpiration = dto.WarrantyExpiration,
            PurchaseDate = dto.PurchaseDate,
            Comment = dto.Comment
        };

        _db.HardwareAssets.Add(hardware);
        await _db.SaveChangesAsync(ct);

        CacheStamp.BumpAssets(); // signal clients to refresh cache

        return CreatedAtAction(
            nameof(GetAllHardware),   // could also make a GetById and reference it here
            new { id = hardware.HardwareID },
            hardware
        );
    }

    [HttpPost("add-bulk")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddHardwareBulk([FromBody] List<CreateHardwareDto> dtos, CancellationToken ct = default)
    {
        if (dtos == null)
        {
            ModelState.AddModelError("Dtos", "Input list cannot be empty.");
            return BadRequest(ModelState);
        }

        if (!ModelState.IsValid)
            return BadRequest(ModelState);


        //collecting error messages to send back to client
        var errors = new Dictionary<int, List<string>>();

        for (int i = 0; i < dtos.Count; i++)
        {
            var dto = dtos[i];
            var itemErrors = new List<string>();
            //should be handled by front end but adding overlapping validation here
            if (string.IsNullOrWhiteSpace(dto.AssetTag) ||
                string.IsNullOrWhiteSpace(dto.Manufacturer) ||
                string.IsNullOrWhiteSpace(dto.Model) ||
                string.IsNullOrWhiteSpace(dto.SerialNumber) ||
                string.IsNullOrWhiteSpace(dto.AssetType) ||
                string.IsNullOrWhiteSpace(dto.Status))
            {
                itemErrors.Add("All fields are required for each hardware asset.");
            }
            if (await _db.HardwareAssets.AnyAsync(h => h.SerialNumber.ToLower() == dto.SerialNumber.ToLower(), ct))
            {
                itemErrors.Add($"Duplicate serial number: {dto.SerialNumber}");
            }
            if (await _db.HardwareAssets.AnyAsync(h => h.AssetTag.ToLower() == dto.AssetTag.ToLower(), ct))
            {
                itemErrors.Add($"Duplicate asset tag: {dto.AssetTag}");
            }
            if (dto.PurchaseDate > DateOnly.FromDateTime(DateTime.UtcNow))
            {
                itemErrors.Add("Purchase date cannot be in the future.");
            }
            if (dto.WarrantyExpiration < dto.PurchaseDate)
            {
                itemErrors.Add("Warranty expiration cannot be before purchase date.");
            }

            if (itemErrors.Count > 0)
                errors[i] = itemErrors;
        }
        //if no errors then proceed to add
        if (errors.Count > 0)
        {
            return BadRequest(new { errors });
        }

        var hardwareList = new List<Hardware>();

        foreach (var dto in dtos)
        {
            // dto is valid, map to entity
            var hardware = new Hardware
            {
                AssetTag = dto.AssetTag.Trim(),
                AssetName = $"{dto.Manufacturer} {dto.Model}".Trim(), //concat the make and model for names
                AssetType = dto.AssetType.Trim(),
                Status = dto.Status.Trim(),
                Manufacturer = dto.Manufacturer.Trim(),
                Model = dto.Model.Trim(),
                SerialNumber = dto.SerialNumber.Trim(),
                WarrantyExpiration = dto.WarrantyExpiration,
                PurchaseDate = dto.PurchaseDate
            };

            hardwareList.Add(hardware);
        }

        _db.HardwareAssets.AddRange(hardwareList);
        await _db.SaveChangesAsync(ct);

        CacheStamp.BumpAssets(); // signal client to refresh cache

        return CreatedAtAction(
            nameof(GetAllHardware),   // could also make a GetById and reference it here
            null,
            hardwareList
        );
    }

    [HttpPut("edit/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EditHardware(int id, [FromBody] UpdateHardwareDto dto, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // LINQ-forward (avoid FindAsync for consistent style)
        var hardware = await _db.HardwareAssets
            .Where(h => h.HardwareID == id)
            .SingleOrDefaultAsync(ct);

        if (hardware == null)
            return NotFound();

        // check for duplicate Tag:
        var existsTag = await _db.HardwareAssets
        .Where(h => h.HardwareID != id && h.AssetTag == dto.AssetTag)
        .AnyAsync();

        if (existsTag)
        {
            ModelState.AddModelError("AssetTag", "A hardware asset with this asset tag already exists.");
            return BadRequest(ModelState);
        }

        // Update fields with coalescence to avoid nulls


        // avoid rewriting values
        if (dto.AssetTag is not null)
        {

            hardware.AssetTag = dto.AssetTag;
        }
        if (dto.AssetName is not null)
        {
            hardware.AssetName = dto.AssetName;
        }
        if (dto.AssetType is not null)
        {

            hardware.AssetType = dto.AssetType;
        }
        if (dto.Status is not null)
        {

            hardware.Status = dto.Status;
        }
        if (dto.Comment is not null)
        {

            hardware.Comment = dto.Comment;
        }


        if (string.IsNullOrEmpty(hardware.AssetName))
        {
            ModelState.AddModelError("AssetName", "AssetName cannot be empty");
            return BadRequest(ModelState);
        }


        await _db.SaveChangesAsync(ct);

        CacheStamp.BumpAssets(); // signal clients to refresh cache

        return Ok(hardware);
    }
}
