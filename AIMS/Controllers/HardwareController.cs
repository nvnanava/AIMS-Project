using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using AIMS.Data;
using AIMS.Models;

namespace AIMS.Controllers;

// Commented out for now, enable when we have EntraID
// [Authorize(Roles = "Admin")]
// With EntraID wired, we gate via policy configured in Program.cs:
[Authorize(Policy = "mbcAdmin")]
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

        // Map DTO → Entity
        var hardware = new Hardware
        {
            AssetName = dto.AssetName,
            AssetType = dto.AssetType,
            Status = dto.Status,
            Manufacturer = dto.Manufacturer,
            Model = dto.Model,
            SerialNumber = dto.SerialNumber,
            WarrantyExpiration = dto.WarrantyExpiration,
            PurchaseDate = dto.PurchaseDate
        };

        _db.HardwareAssets.Add(hardware);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(
            nameof(GetAllHardware),   // could also make a GetById and reference it here
            new { id = hardware.HardwareID },
            hardware
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

        // Update fields
        hardware.AssetName = dto.AssetName;
        hardware.AssetType = dto.AssetType;
        hardware.Status = dto.Status;
        hardware.Manufacturer = dto.Manufacturer;
        hardware.Model = dto.Model;
        hardware.SerialNumber = dto.SerialNumber;
        hardware.WarrantyExpiration = dto.WarrantyExpiration;
        hardware.PurchaseDate = dto.PurchaseDate;

        await _db.SaveChangesAsync(ct);

        return Ok(hardware);
    }
}