using System.Linq;
using AIMS.Data;
using AIMS.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Controllers;

// Commented out for now, enable when we have entraID
// [Authorize(Roles = "Admin")]
[ApiController]
[Route("api/feedback")]
public class FeedbackController : ControllerBase
{
    private readonly AimsDbContext _db;
    private readonly FeedbackQuery _feedbackQuery;
    public FeedbackController(AimsDbContext db, FeedbackQuery feedbackQuery)
    {
        _db = db;
        _feedbackQuery = feedbackQuery;
    }

    [HttpGet("get-all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAllFeedback()
    {
        var feedback = await _feedbackQuery.GetAllFeedbackAsync();
        return Ok(feedback);
    }


    [HttpPost("create")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateFeedback([FromBody] CreateFeedbackDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var feedback = new Feedback
        {
            SubmissionDate = DateTime.UtcNow,   // auto-set when submitted
            Category = dto.Category,
            Description = dto.Description,
            Status = "Open",                    // default
            SubmittedByUserID = dto.SubmittedByUserID
        };

        _db.FeedbackEntries.Add(feedback);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAllFeedback), new { id = feedback.FeedbackID }, feedback);
    }



}
