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
[Route("api/assign")]
public class AssignmentController : ControllerBase
{
    private readonly AimsDbContext _db;
    private readonly AssignmentsQuery _assignQuery;
    public AssignmentController(AimsDbContext db, AssignmentsQuery assignQuery)
    {
        _db = db;
        _assignQuery = assignQuery;

    }

    // Quick sanity: table counts
    [HttpPost("create")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Assignment([FromBody] CreateAssignmentDto req)
    {

        // short-circuit: reject if user not authenticated

        // make sure we get one of the indentifiers for an asset
        bool bothNull = req.AssetTag == null && req.SoftwareID == null;
        bool bothValues = req.SoftwareID != null && req.AssetTag != null;
        if (bothNull || bothValues)
        {
            // for correct methods corresponding to request messages, see the Methods section under https://learn.microsoft.com/en-us/dotnet/api/system.web.http.apicontroller?view=aspnetcore-2.2
            return BadRequest("You must specify either AssetTag (HardwareID) or SoftwareID.");
        }

        // validate that AssetTag exists if specified
        var assetTagExists = await _db.HardwareAssets.AnyAsync(hw => hw.HardwareID == req.AssetTag);
        if (req.AssetKind == AssetKind.Hardware && !assetTagExists)
        {
            return BadRequest("Please specify a valid AssetTag");
        }

        // validate that SoftwareID exists if specified
        var softwareIDExists = await _db.SoftwareAssets.AnyAsync(sw => sw.SoftwareID == req.SoftwareID);
        if (req.AssetKind == AssetKind.Software && !softwareIDExists)
        {
            return BadRequest("Please specify a valid SoftwareID");
        }
        // it does not make sense to specify both a Hardware and Software ID in one assignment request
        if (assetTagExists && softwareIDExists)
        {
            return BadRequest("Please specify only one of either AssetTag(HardwareID) or SoftwareID");
        }

        // make sure that an assignnment does not already exist (no double assign)
        var assignmentExists = await _db.Assignments.AnyAsync(a =>
            (req.SoftwareID != null && a.SoftwareID == req.SoftwareID) ||
            (req.AssetTag != null && a.AssetTag == req.AssetTag)
        );


        if (assignmentExists)
        {
            // hardware error message (409)
            if (req.AssetKind == AssetKind.Hardware && assetTagExists)
            {
                return Conflict($"An assignment for hardware device with ID {req.AssetTag} already exists!");

            }
            // software error message (409)
            else if (req.AssetKind == AssetKind.Software && softwareIDExists)
            {
                return Conflict($"An assignment for software with ID {req.SoftwareID} already exists!");

            }
        }

        // See comment above the DTO class; this can be automated using AutoMapper
        Assignment newAssignment = new Assignment
        {
            SoftwareID = req.SoftwareID,
            AssetKind = req.AssetKind,
            AssetTag = req.AssetTag,
            UserID = req.UserID
        };

        // finally, create assignment
        _db.Assignments.Add(newAssignment);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAssignment), new { AssignmentID = newAssignment.AssignmentID }, req);
    }

    [HttpGet("get")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]

    public async Task<IActionResult> GetAssignment([FromQuery] int AssignmentID)
    {
        var assignment = await _assignQuery.GetAssignmentAsync(AssignmentID);

        return Ok(assignment);
    }
    [HttpGet("list")]
    [ProducesResponseType(StatusCodes.Status200OK)]

    public async Task<IActionResult> GetAllAssignments()
    {
        var rows = await _assignQuery.GetAllAssignmentsAsync();
        return Ok(rows);
    }

    [HttpPost("close")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CloseAssignment([FromQuery] int AssignmentID)
    {
        // find assignment by primary key
        var assignment = await _db.Assignments.FindAsync(AssignmentID);

        // if not found, error
        if (assignment == null)
        {
            return NotFound("Please specify a valid AssignmentID");
        }

        // delete the record
        _db.Assignments.Remove(assignment);
        await _db.SaveChangesAsync();
        return Ok();

    }

}

/**

See the following link for the reason for why we use DTOs:
https://blog.devart.com/working-with-data-transfer-objects-in-asp-net-core.html

We might move this to a new file: https://learn.microsoft.com/en-us/aspnet/web-api/overview/data/using-web-api-with-entity-framework/part-5

(TODO): We can use an automapper to generate the bindings from this DTO to the actual model: https://automapper.io
*/
public class CreateAssignmentDto
{
    public int UserID { get; set; }

    // What (one of these must be set, enforced in code/migration)
    public AssetKind AssetKind { get; set; }
    public int? AssetTag { get; set; }      // when Hardware
    public int? SoftwareID { get; set; }    // when Software

}

public class CloseAssignmentDto
{
    public int AssignmentID { get; set; }
}
