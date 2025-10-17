using AIMS.Data;
using AIMS.Dtos.Assets;
using AIMS.Dtos.Software;
using AIMS.Models;
using AIMS.Queries;
using AIMS.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Controllers.Api;

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

    //add bulk software licenses 
    [HttpPost("add-bulk")]
    [Authorize(Policy = "mbcAdmin")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddBulkSoftware([FromBody] List<CreateSoftwareDto> dtos, CancellationToken ct = default)
    {
        //check empty lists
        if (dtos == null || dtos.Count == 0)
        {
            ModelState.AddModelError("Dtos", "Input list cannot be empty.");
            return BadRequest(ModelState);
        }

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        //collecting error messages to send back to client
        var errors = new Dictionary<int, List<string>>();

        // Validate each DTO in the list
        for (int i = 0; i < dtos.Count; i++)
        {
            var dto = dtos[i];
            var itemErrors = new List<string>();
            //validate unique SoftwareLicenseKey

            // required fields
            if (string.IsNullOrWhiteSpace(dto.SoftwareName) ||
                string.IsNullOrWhiteSpace(dto.SoftwareVersion) ||
                string.IsNullOrWhiteSpace(dto.SoftwareLicenseKey) ||
                string.IsNullOrWhiteSpace(dto.Comment))
            {
                itemErrors.Add("All fields are required for each software asset, except License Expiration.");
            }
            else
            {
                if (await _db.SoftwareAssets.AnyAsync(s => s.SoftwareLicenseKey.ToLower() == dto.SoftwareLicenseKey.ToLower(), ct))
                {
                    itemErrors.Add($"A software asset with this license key '{dto.SoftwareLicenseKey}' already exists.");
                }
            }

            //validate SoftwareCost is non-negative
            if (dto.SoftwareCost < 0)
            {
                itemErrors.Add("Software cost cannot be negative.");
            }

            //validate license expiration is not in the past
            if (dto.SoftwareLicenseExpiration.HasValue && dto.SoftwareLicenseExpiration < DateOnly.FromDateTime(DateTime.UtcNow))
            {
                itemErrors.Add("License expiration cannot be in the past.");
            }

            if (itemErrors.Count > 0)
            {
                errors[i] = itemErrors;
            }
        }

        if (errors.Count > 0)
        {
            return BadRequest(errors);
        }

        var newSoftwareAssets = dtos.Select(dto => new Software
        {
            SoftwareName = dto.SoftwareName,
            SoftwareVersion = dto.SoftwareVersion,
            SoftwareLicenseKey = dto.SoftwareLicenseKey,
            SoftwareLicenseExpiration = dto.SoftwareLicenseExpiration,
            SoftwareUsageData = dto.SoftwareUsageData,
            SoftwareCost = dto.SoftwareCost,
            Comment = dto.Comment
        }).ToList();

        _db.SoftwareAssets.AddRange(newSoftwareAssets);
        await _db.SaveChangesAsync(ct);
        CacheStamp.BumpAssets();

        return CreatedAtAction(nameof(GetAllSoftware), null, newSoftwareAssets);

    }

    [HttpPut("archive/{id}")]
    [Authorize(Policy = "mbcAdmin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ArchiveSoftware(int id, CancellationToken ct = default)
    {
        var software = await _db.SoftwareAssets
            .Where(s => s.SoftwareID == id)
            .SingleOrDefaultAsync(ct);

        if (software == null)
            return NotFound();

        software.IsArchived = true;




        // unassign if assigned
        var assignment = await _db.Assignments
            .Where(s => s.SoftwareID == id && s.UnassignedAtUtc == null)
            .SingleOrDefaultAsync(ct);
        if (assignment != null)
        {
            assignment.UnassignedAtUtc = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
        CacheStamp.BumpAssets();

        var Asset = await _db.SoftwareAssets
        .IgnoreQueryFilters()
        .Where(s => s.SoftwareID == id)
        .Select(s => new AssetRowDto
        {
            SoftwareID = s.SoftwareID,
            AssetName = s.SoftwareName,
            Type = s.SoftwareType,
            Tag = s.SoftwareLicenseKey,
            Status = "Archived",
            IsArchived = true,
            AssignedUserId = null,
            AssignedTo = "Unassigned"
        })
    .FirstOrDefaultAsync();

        return Ok(Asset);
    }

    [HttpPut("unarchive/{id}")]
    [Authorize(Policy = "mbcAdmin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnarchiveSoftware(int id, CancellationToken ct = default)
    {
        var software = await _db.SoftwareAssets
            .IgnoreQueryFilters()
            .Where(s => s.SoftwareID == id)
            .SingleOrDefaultAsync(ct);

        if (software == null)
            return NotFound();

        software.IsArchived = false;


        await _db.SaveChangesAsync(ct);
        CacheStamp.BumpAssets();

        var Asset = await _db.SoftwareAssets
        .Where(s => s.SoftwareID == id)
        .Select(s => new AssetRowDto
        {
            SoftwareID = s.SoftwareID,
            AssetName = s.SoftwareName,
            Type = s.SoftwareType,
            Tag = s.SoftwareLicenseKey,
            Status = "Available",
            IsArchived = false,
            AssignedUserId = null,
            AssignedTo = "Unassigned"
        })
    .FirstOrDefaultAsync();

        return Ok(Asset);

    }
}
