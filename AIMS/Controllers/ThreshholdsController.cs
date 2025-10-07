using AIMS.Data;
using AIMS.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Controllers;

[ApiController]
[Route("api/thresholds")]
public sealed class ThresholdsController : ControllerBase
{
    private readonly AimsDbContext _db;
    public ThresholdsController(AimsDbContext db) => _db = db;

    // GET: api/thresholds
    [HttpGet]
    [Authorize(Policy = "mbcAdmin")]
    public async Task<ActionResult<IEnumerable<ThresholdVm>>> GetAll(CancellationToken ct)
    {
        var rows = await _db.Thresholds.AsNoTracking()
            .OrderBy(t => t.AssetType)
            .Select(t => new ThresholdVm { AssetType = t.AssetType, ThresholdValue = t.ThresholdValue })
            .ToListAsync(ct);
        return Ok(rows);
    }

    // PUT: api/thresholds/{assetType} (upsert)
    [HttpPut("{assetType}")]
    [Authorize(Policy = "mbcAdmin")]
    public async Task<IActionResult> Upsert(string assetType, [FromBody] UpsertThresholdDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var key = (assetType ?? string.Empty).Trim();
        if (!string.Equals(key, dto.AssetType?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(dto.AssetType), "Route assetType must match body AssetType.");
            return BadRequest(ModelState);
        }

        var row = await _db.Thresholds.SingleOrDefaultAsync(t => t.AssetType == key, ct);
        if (row is null)
            _db.Thresholds.Add(new Models.Threshold { AssetType = key, ThresholdValue = dto.ThresholdValue });
        else
            row.ThresholdValue = dto.ThresholdValue;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
