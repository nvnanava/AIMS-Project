// AIMS/Models/Feedback.cs
// Legacy scaffold — kept for historical reference and to preserve migrations.
// NOTE: Feature removed from UI. Do not inject/use in new code.

using System;

namespace AIMS.Models;

public class Feedback
{
    // PK
    public int FeedbackID { get; set; }

    // Columns
    public DateTime SubmissionDate { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";

    // FK — submitted by a user
    public int SubmittedByUserID { get; set; }
    public User SubmittedByUser { get; set; } = null!;
}
