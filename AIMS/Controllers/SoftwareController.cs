using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using AIMS.Data;
using AIMS.Models;

namespace AIMS.Controllers;

// Commented out for now, enable when we have entraID
// [Authorize(Roles = "Admin")]
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
    public async Task<IActionResult> GetAllSoftware()
    {
        var users = await _softwareQuery.GetAllSoftwareAsync();
        return Ok(users);
    }

    [HttpPost("add")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddSoftware([FromBody] CreateSoftwareDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // Map DTO → Entity
        var software = new Software
        {
            SoftwareName = dto.SoftwareName,
            SoftwareType = dto.SoftwareType,
            SoftwareVersion = dto.SoftwareVersion,
            SoftwareLicenseKey = dto.SoftwareLicenseKey,
            SoftwareLicenseExpiration = dto.SoftwareLicenseExpiration,
            SoftwareUsageData = dto.SoftwareUsageData,
            SoftwareCost = dto.SoftwareCost
        };

        _db.SoftwareAssets.Add(software);
        await _db.SaveChangesAsync();

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
    public async Task<IActionResult> EditSoftware(int id, [FromBody] UpdateSoftwareDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var software = await _db.SoftwareAssets.FindAsync(id);
        if (software == null)
            return NotFound();

        // Update fields
        software.SoftwareName = dto.SoftwareName;
        software.SoftwareType = dto.SoftwareType;
        software.SoftwareVersion = dto.SoftwareVersion;
        software.SoftwareLicenseKey = dto.SoftwareLicenseKey;
        software.SoftwareLicenseExpiration = dto.SoftwareLicenseExpiration;
        software.SoftwareUsageData = dto.SoftwareUsageData;
        software.SoftwareCost = dto.SoftwareCost;

        await _db.SaveChangesAsync();

        return Ok(software);
    }

}
