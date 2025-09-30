using AIMS.Data;
using AIMS.Models;
using Microsoft.EntityFrameworkCore;

public class FeedbackQuery
{
    private readonly AimsDbContext _db;
    public FeedbackQuery(AimsDbContext db) => _db = db;

    public async Task<List<GetFeedbackDto>> GetAllFeedbackAsync()
    {
        return await _db.FeedbackEntries
            .Include(f => f.SubmittedByUser) // join user
            .Select(f => new GetFeedbackDto
            {
                FeedbackID = f.FeedbackID,
                SubmissionDate = f.SubmissionDate,
                Category = f.Category,
                Description = f.Description,
                Status = f.Status,
                SubmittedByUserID = f.SubmittedByUserID,
                SubmittedByUserName = f.SubmittedByUser.FullName // assumes User has UserName
            })
            .ToListAsync();
    }
}

public class GetFeedbackDto
{
    public int FeedbackID { get; set; }
    public DateTime SubmissionDate { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";

    public int SubmittedByUserID { get; set; }
    public string SubmittedByUserName { get; set; } = string.Empty; // optional, helpful
}

public class CreateFeedbackDto
{
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SubmittedByUserID { get; set; }
}
