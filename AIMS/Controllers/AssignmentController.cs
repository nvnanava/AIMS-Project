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
[Route("api/")]
public class AssignmentController : ControllerBase
{
    private readonly AimsDbContext _db;
    public AssignmentController(AimsDbContext db) => _db = db;

    // Quick sanity: table counts
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Assignment([FromBody] CreateAssignmentDto req)
    {

        // short-circuit: reject if user not authenticated

        // make sure we get one of the indentifiers for an asset
        if (req.AssetTag == null && req.SoftwareID == null)
        {
            // for correct methods corresponding to request messages, see the Methods section under https://learn.microsoft.com/en-us/dotnet/api/system.web.http.apicontroller?view=aspnetcore-2.2
            return BadRequest("You must specify either AssetTag (HardwareID) or SoftwareID.");
        } 

        // validate that AssetTag exists if specified
        var assetTagExists = await _db.HardwareAssets.AnyAsync(hw => hw.HardwareID == req.AssetTag);
        if (req.AssetTag != null && !assetTagExists)
        {
            return BadRequest("Please specify a valid AssetTag");
        }

        // validate that SoftwareID exists if specified
        var softwareIDExists = await _db.SoftwareAssets.AnyAsync(sw => sw.SoftwareID == req.SoftwareID);
        if (req.SoftwareID != null && !softwareIDExists)
        {
            return BadRequest("Please specify a valid SoftwareID");
        }
        // it does not make sense to specify both a Hardware and Software ID in one assignment request
        if (assetTagExists && softwareIDExists) {
            return BadRequest("Please specify only one of either AssetTag(HardwareID) or SoftwareID");
        }

        // make sure that an assignnment does not already exist (no double assign)
        var assignmentExists = await _db.Assignments.AnyAsync(a => a.SoftwareID == req.SoftwareID || a.AssetTag == req.AssetTag);

        if (assignmentExists)
        {
            // hardware error message (409)
            if (assetTagExists)
            {
                return Conflict("An assignment for this hardware device with ID {assetTag}");

            }
            // software error message (409)
            else if (softwareIDExists)
            {
                return Conflict("An assignment for this software with ID {softwareID}");

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

        return CreatedAtAction(nameof(GetAssignment), new {AssignmentID = newAssignment.AssignmentID}, req);
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]

    public async Task<IActionResult> GetAssignment(int AssignmentID)
    {
        var assignment = await _db.Assignments.Where(a => a.AssignmentID == AssignmentID).FirstOrDefaultAsync();
        if (assignment == null)
        {
            return NotFound("Please specify a valid AssignmentID");
        }

        return Ok(assignment);
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
