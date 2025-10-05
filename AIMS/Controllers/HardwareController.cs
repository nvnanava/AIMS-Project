using System.Linq;
using AIMS.Data;
using AIMS.Models;
using AIMS.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
    [Authorize(Policy = "mbcAdmin")]
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
    [Authorize(Policy = "mbcAdmin")]
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

        // collecting error messages to send back to client
        var errors = new Dictionary<int, List<string>>();

        for (int i = 0; i < dtos.Count; i++)
        {
            var dto = dtos[i];
            var itemErrors = new List<string>();

            // normalize values with null-safe trims
            var assetTag = dto.AssetTag?.Trim();
            var manufacturer = dto.Manufacturer?.Trim();
            var model = dto.Model?.Trim();
            var serialNumber = dto.SerialNumber?.Trim();
            var assetType = dto.AssetType?.Trim();
            var status = dto.Status?.Trim();

            // should be handled by front end but adding overlapping validation here
            if (string.IsNullOrWhiteSpace(assetTag) ||
                string.IsNullOrWhiteSpace(manufacturer) ||
                string.IsNullOrWhiteSpace(model) ||
                string.IsNullOrWhiteSpace(serialNumber) ||
                string.IsNullOrWhiteSpace(assetType) ||
                string.IsNullOrWhiteSpace(status))
            {
                itemErrors.Add("All fields are required for each hardware asset.");
            }
            else
            {
                // duplicate checks only if values are present
                if (await _db.HardwareAssets.AnyAsync(h => h.SerialNumber.ToLower() == serialNumber.ToLower(), ct))
                {
                    itemErrors.Add($"Duplicate serial number: {serialNumber}");
                }
                if (await _db.HardwareAssets.AnyAsync(h => h.AssetTag.ToLower() == assetTag.ToLower(), ct))
                {
                    itemErrors.Add($"Duplicate asset tag: {assetTag}");
                }
            }

            // validate dates
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

            // re-assign normalized values back to dto so mapping is consistent
            dto.AssetTag = assetTag;
            dto.Manufacturer = manufacturer;
            dto.Model = model;
            dto.SerialNumber = serialNumber;
            dto.AssetType = assetType;
            dto.Status = status;
        }
        //if no errors then proceed to add
        if (errors.Count > 0)
        {
            return BadRequest(new { errors });
        }

        var hardwareList = new List<Hardware>();

        foreach (var dto in dtos)
        {
            // dto is valid, map to entity with already normalized values
            var hardware = new Hardware
            {
                AssetTag = dto.AssetTag,
                AssetName = $"{dto.Manufacturer} {dto.Model}".Trim(), // concat the make and model for names
                AssetType = dto.AssetType,
                Status = dto.Status,
                Manufacturer = dto.Manufacturer,
                Model = dto.Model,
                SerialNumber = dto.SerialNumber,
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
    [Authorize(Policy = "mbcAdmin")]
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
