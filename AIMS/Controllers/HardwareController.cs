using AIMS.Data;
using AIMS.Models;
using AIMS.Queries;
using AIMS.Utilities;
using AIMS.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Controllers;

// [Authorize(Policy = "mbcAdmin")] // enable when ready globally
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

        var dtos = (req.Dtos ?? new())
            .Select(d => new CreateHardwareDto
            {
                AssetTag = (d.AssetTag ?? "").Trim(),
                AssetName = (d.AssetName ?? "").Trim(),
                AssetType = (d.AssetType ?? "").Trim(),
                Status = (d.Status ?? "").Trim(),
                Manufacturer = (d.Manufacturer ?? "").Trim(),
                Model = (d.Model ?? "").Trim(),
                SerialNumber = (d.SerialNumber ?? "").Trim(),
                WarrantyExpiration = d.WarrantyExpiration,
                PurchaseDate = d.PurchaseDate,
                Comment = (d.Comment ?? "").Trim()
            })
            .Where(d =>
                !(string.IsNullOrWhiteSpace(d.AssetTag) &&
                  string.IsNullOrWhiteSpace(d.Manufacturer) &&
                  string.IsNullOrWhiteSpace(d.Model) &&
                  string.IsNullOrWhiteSpace(d.SerialNumber) &&
                  string.IsNullOrWhiteSpace(d.AssetType) &&
                  string.IsNullOrWhiteSpace(d.Status) &&
                  d.PurchaseDate == default &&
                  d.WarrantyExpiration == default))
            .ToList();

        if (dtos.Count == 0)
        {
            ModelState.AddModelError(nameof(BulkHardwareRequest.Dtos), "Input list cannot be empty.");
            return BadRequest(ModelState);
        }

        // per-row validation
        var itemErrors = new Dictionary<string, string[]>();
        for (int i = 0; i < dtos.Count; i++)
        {
            var d = dtos[i];
            var errs = new List<string>();

            if (string.IsNullOrWhiteSpace(d.AssetTag) ||
                string.IsNullOrWhiteSpace(d.Manufacturer) ||
                string.IsNullOrWhiteSpace(d.Model) ||
                string.IsNullOrWhiteSpace(d.SerialNumber) ||
                string.IsNullOrWhiteSpace(d.AssetType) ||
                string.IsNullOrWhiteSpace(d.Status))
                errs.Add("All fields are required for each hardware asset.");

            if (d.AssetTag.Length > 16) errs.Add("Asset tag must be 16 characters or fewer.");

            if (d.PurchaseDate > DateOnly.FromDateTime(DateTime.UtcNow))
                errs.Add("Purchase date cannot be in the future.");
            if (d.WarrantyExpiration < d.PurchaseDate)
                errs.Add("Warranty expiration cannot be before purchase date.");

            if (errs.Count > 0) itemErrors[$"Dtos[{i}]"] = errs.ToArray();
        }
        if (itemErrors.Count > 0)
            return ValidationProblem(new ValidationProblemDetails(itemErrors));

        // batch-internal duplicate checks
        var seenSerials = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // case-insensitive for serials
        var seenTags = new HashSet<string>(StringComparer.Ordinal);              // keep tags case-sensitive (change to OrdinalIgnoreCase if desired)

        for (int i = 0; i < dtos.Count; i++)
        {
            var d = dtos[i];
            var errs = new List<string>();

            if (!seenSerials.Add(d.SerialNumber))
                errs.Add($"Duplicate serial number within batch: {d.SerialNumber}");
            if (!seenTags.Add(d.AssetTag))
                errs.Add($"Duplicate asset tag within batch: {d.AssetTag}");

            if (errs.Count > 0) itemErrors[$"Dtos[{i}]"] = errs.ToArray();
        }
        if (itemErrors.Count > 0)
            return ValidationProblem(new ValidationProblemDetails(itemErrors));

        // duplicate checks vs DB (case-insensitive for serials)
        var existingSerialsList = await _db.HardwareAssets
            .Where(h => h.SerialNumber != null)
            .Select(h => h.SerialNumber!)
            .ToListAsync(ct);

        var existingTagsList = await _db.HardwareAssets
            .Where(h => h.AssetTag != null)
            .Select(h => h.AssetTag!)
            .ToListAsync(ct);

        var existingSerials = new HashSet<string>(existingSerialsList, StringComparer.OrdinalIgnoreCase);
        var existingTags = new HashSet<string>(existingTagsList, StringComparer.Ordinal);

        for (int i = 0; i < dtos.Count; i++)
        {
            var d = dtos[i];
            var errs = new List<string>();

            if (existingSerials.Contains(d.SerialNumber))
                errs.Add($"Duplicate serial number: {d.SerialNumber}");

            if (existingTags.Contains(d.AssetTag))
                errs.Add($"Duplicate asset tag: {d.AssetTag}");

            if (errs.Count > 0) itemErrors[$"Dtos[{i}]"] = errs.ToArray();
        }
        if (itemErrors.Count > 0)
            return ValidationProblem(new ValidationProblemDetails(itemErrors));

        // map → entities
        var entities = dtos.Select(d => new Hardware
        {
            AssetTag = d.AssetTag,
            AssetName = string.IsNullOrWhiteSpace(d.AssetName)
                ? $"{d.Manufacturer} {d.Model}".Trim()
                : d.AssetName,
            AssetType = d.AssetType,
            Status = d.Status,
            Manufacturer = d.Manufacturer,
            Model = d.Model,
            SerialNumber = d.SerialNumber,
            WarrantyExpiration = d.WarrantyExpiration,
            PurchaseDate = d.PurchaseDate,
            Comment = d.Comment
        }).ToList();

        _db.HardwareAssets.AddRange(entities);
        await _db.SaveChangesAsync(ct);

        CacheStamp.BumpAssets();

        return CreatedAtAction(nameof(GetAllHardware), null, entities);
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

        if (dto.AssetTag is not null)
        {
            var existsTag = await _db.HardwareAssets
                .Where(h => h.HardwareID != id && h.AssetTag == dto.AssetTag)
                .AnyAsync(ct);

            if (existsTag)
            {
                ModelState.AddModelError(nameof(dto.AssetTag), "A hardware asset with this asset tag already exists.");
                return BadRequest(ModelState);
            }

            hardware.AssetTag = dto.AssetTag;
        }

        if (dto.AssetName is not null) hardware.AssetName = dto.AssetName;
        if (dto.AssetType is not null) hardware.AssetType = dto.AssetType;
        if (dto.Status is not null) hardware.Status = dto.Status;
        if (dto.Manufacturer is not null) hardware.Manufacturer = dto.Manufacturer;
        if (dto.Model is not null) hardware.Model = dto.Model;
        if (dto.Comment is not null) hardware.Comment = dto.Comment;

        if (string.IsNullOrWhiteSpace(hardware.AssetName))
        {
            ModelState.AddModelError(nameof(dto.AssetName), "AssetName cannot be empty");
            return BadRequest(ModelState);
        }

        await _db.SaveChangesAsync(ct);
        CacheStamp.BumpAssets();

        return Ok(hardware);
    }
}
