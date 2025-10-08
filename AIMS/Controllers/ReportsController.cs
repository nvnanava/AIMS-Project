using AIMS.Data;
using AIMS.Queries;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CsvHelper; // Requires CsvHelper NuGet package
using System.Text;
using AIMS.Models;
using CsvHelper.Configuration;
using System.Globalization;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using AIMS.ViewModels;

namespace AIMS.Controllers;

// // With EntraID wired, we gate via policy configured in Program.cs:
// [Authorize(Policy = "mbcAdmin")]
[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly ReportsQuery _reports;
    private readonly AimsDbContext _db;
    private readonly ILogger<ReportsController> _logger;

    private sealed class CSVIntermediate
    {
        public int AssignmentID { get; set; }
        public string Assignee { get; set; } = string.Empty;
        public string Office { get; set; } = string.Empty;
        public AssetKind AssetType { get; set; }
        public string AssetName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
    }
    public ReportsController(ReportsQuery reports, AimsDbContext db, ILogger<ReportsController> logger)
    {
        _reports = reports;
        _db = db;
        _logger = logger;
    }

    [HttpPost("/")]
    public async Task<IActionResult> Create(
        [FromQuery] string? type,
        [FromQuery] DateOnly start,
        [FromQuery] DateOnly? end,
        [FromQuery] int? OfficeID,
        [FromQuery] string? desc,
        [FromQuery] CustomReportDto? customOptions,
         CancellationToken ct = default
        )
    {

        // perform basic checking
        if (end is null)
        {
            end = DateOnly.FromDateTime(DateTime.Now);
        }

        // date mismatch
        if (end < start)
        {
            ModelState.AddModelError("DateMismatch", "The start date must not be after the end date!");
            return BadRequest(ModelState);
        }

        // try converting type to enum
        ReportType reportType;
        if (!Enum.TryParse(type, true, out reportType))
        {
            // conversion failed
            ModelState.AddModelError("InvalidType", "Please specify a valid report type: Assignment, Office, or Custom!");
            return BadRequest(ModelState);
        }

        var creationDate = DateTime.Now;

        // Assignments Report
        // Data In: Start Date, End Date (optional; today if null), Description (optional)
        // Data Out: AssignmentID, Assignee, Office, Asset Name, Asset Type, Asset Seat Number, Comment
        // Notes: File Name should include generation date,
        // Sort: Assigned Assets first, non-assigned later

        if (reportType == ReportType.Assignment)
        {
            // get assignments, sorted by activity
            var activeItemsFirst = await _db.Assignments
            .AsNoTracking()
            // if null, these will be put at the top of the list (meaning the assigned assets are first)
            .OrderBy(item => item.UnassignedAtUtc)
           .Select(a => new CSVIntermediate
           {
               AssignmentID = a.AssignmentID,
               Assignee = a.User!.FullName,
               Office = a.Office!.OfficeName,
               AssetType = a.AssetKind,
               AssetName = a.AssetKind == AssetKind.Hardware
                                ? a.Hardware!.AssetName
                                : a.Software!.SoftwareName,
               Status = a.AssetKind == AssetKind.Hardware
                                ? a.Hardware!.Status
                                : string.Empty,
               Comment = a.AssetKind == AssetKind.Hardware
                                ? a.Hardware!.Comment
                                : a.Software!.Comment
           })
            .ToListAsync(ct);

            // 2. Generate CSV content using CsvHelper
            // Create the MemoryStream outside of a using block
            var memoryStream = new MemoryStream();
            using (var streamWriter = new StreamWriter(memoryStream, Encoding.UTF8))
            {
                var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = false // Disable automatic header generation
                };

                using (var csvWriter = new CsvWriter(streamWriter, csvConfig))
                {
                    // Manually write the custom header row
                    csvWriter.WriteField("AssignmentID");
                    csvWriter.WriteField("Assignee");
                    csvWriter.WriteField("Assignee Office");
                    csvWriter.WriteField("Asset Name");
                    csvWriter.WriteField("Asset Type");
                    // csvWriter.WriteField("Seat Number"); TODO: Add this when this field is added at the end of sprint 7
                    csvWriter.WriteField("Comment");
                    csvWriter.NextRecord(); // Move to the next line for data

                    // Manually write each record
                    foreach (var assignment in activeItemsFirst)
                    {
                        csvWriter.WriteField(assignment.AssignmentID);
                        csvWriter.WriteField(assignment.Assignee);
                        csvWriter.WriteField(assignment.Office);
                        csvWriter.WriteField(assignment.AssetName);
                        csvWriter.WriteField(assignment.AssetType);
                        csvWriter.WriteField(assignment.Comment);
                        csvWriter.NextRecord(); // Move to the next line
                    }

                    streamWriter.Flush();
                }
            }

            var bytes = memoryStream.ToArray();

            return File(
                bytes,
                contentType: "text/csv",
                fileDownloadName: $"Assignment_Report_{creationDate}.csv"
            );


            // Custom Report
            // Data In: Start Date, End Date (optional), Description (optional)
            // ... See Hardware, See Software, See Users, See Office, See when software and/ or warranty expires, See what requires maintenance or replacements
            // Data Out: Assignee, Office, Asset Name, Asset Type, Seat #, Expiration, MaintenanceStatus, Comment
            // Notes: If MaintenanceStatus opt specified: show only those.
        }
        else if (reportType == ReportType.Custom)
        {

            // get assignments, sorted by activity
            IQueryable<Assignment> activeItemsFirst =
            _db.Assignments
            .AsNoTracking().OrderBy(item => item.UnassignedAtUtc);

            if (customOptions.seeHardware && !customOptions.seeSoftware)
            {
                activeItemsFirst = activeItemsFirst.Where(a => a.AssetKind == AssetKind.Hardware);
            }
            else if (customOptions.seeSoftware && !customOptions.seeHardware)
            {
                activeItemsFirst = activeItemsFirst.Where(a => a.AssetKind == AssetKind.Software);
            }

            List<CSVIntermediate> list = await activeItemsFirst
            .OrderBy(item => item.UnassignedAtUtc)
            .Select(a => new CSVIntermediate
            {
                AssignmentID = a.AssignmentID,
                Assignee = a.User!.FullName,
                Office = a.Office!.OfficeName,
                AssetType = a.AssetKind,
                AssetName = a.AssetKind == AssetKind.Hardware
                                ? a.Hardware!.AssetName
                                : a.Software!.SoftwareName,
                Status = a.AssetKind == AssetKind.Hardware
                                ? a.Hardware!.Status
                                : string.Empty,
                Comment = a.AssetKind == AssetKind.Hardware
                                ? a.Hardware!.Comment
                                : a.Software!.Comment
            })
            .ToListAsync();


            // 2. Generate CSV content using CsvHelper
            // Create the MemoryStream outside of a using block
            var memoryStream = new MemoryStream();
            using (var streamWriter = new StreamWriter(memoryStream, Encoding.UTF8))
            {
                var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = false // Disable automatic header generation
                };

                using (var csvWriter = new CsvWriter(streamWriter, csvConfig))
                {
                    // Manually write the custom header row
                    csvWriter.WriteField("AssignmentID");
                    csvWriter.WriteField("Assignee");
                    csvWriter.WriteField("Assignee Office");
                    csvWriter.WriteField("Asset Name");
                    csvWriter.WriteField("Asset Type");
                    // csvWriter.WriteField("Seat Number"); TODO: Add this when this field is added at the end of sprint 7
                    csvWriter.WriteField("Comment");
                    csvWriter.NextRecord(); // Move to the next line for data

                    // Manually write each record
                    foreach (CSVIntermediate assignment in list)
                    {
                        csvWriter.WriteField(assignment.AssignmentID);
                        csvWriter.WriteField(assignment.Assignee);
                        csvWriter.WriteField(assignment.Office);
                        csvWriter.WriteField(assignment.AssetName);
                        csvWriter.WriteField(assignment.AssetType);
                        csvWriter.WriteField(assignment.Comment);
                        csvWriter.NextRecord(); // Move to the next line
                    }

                    streamWriter.Flush();
                }
            }

            var bytes = memoryStream.ToArray();

            return File(
                bytes,
                contentType: "text/csv",
                fileDownloadName: $"Custom_Report_{creationDate}.csv"
            );


        }
        else if (reportType == ReportType.Office)
        {
            if (OfficeID is null)
            {
                ModelState.AddModelError(nameof(OfficeID), "Please specify a valid OfficeID.");
                return BadRequest(ModelState);
            }


            var office = await _db.Offices.Where(o => o.OfficeID == OfficeID).FirstOrDefaultAsync(ct);

            if (office is null)
            {
                ModelState.AddModelError(nameof(OfficeID), "Please specify a valid OfficeID.");
                return BadRequest(ModelState);

            }
            // Custom Report
            // Data In: Start Date, End Date (optional), Description (optional)
            // ... See Hardware, See Software, See Users, See Office, See when software and/ or warranty expires, See what requires maintenance or replacements
            // Data Out: Assignee, Office, Asset Name, Asset Type, Seat #, Expiration, MaintenanceStatus, Comment
            // Notes: If MaintenanceStatus opt specified: show only those.

            // get assignments, sorted by activity
            var activeItemsFirst = await _db.Assignments
        .AsNoTracking()
        .Where(a => a.OfficeID == OfficeID)
        // if null, these will be put at the top of the list (meaning the assigned assets are first)
        .OrderBy(item => item.UnassignedAtUtc)
                    .Select(a => new CSVIntermediate
                    {
                        AssignmentID = a.AssignmentID,
                        Assignee = a.User!.FullName,
                        Office = a.Office!.OfficeName,
                        AssetType = a.AssetKind,
                        AssetName = a.AssetKind == AssetKind.Hardware
                                ? a.Hardware!.AssetName
                                : a.Software!.SoftwareName,
                        Status = a.AssetKind == AssetKind.Hardware
                                ? a.Hardware!.Status
                                : string.Empty,
                        Comment = a.AssetKind == AssetKind.Hardware
                                ? a.Hardware!.Comment
                                : a.Software!.Comment
                    })

        .ToListAsync(ct);

            // 2. Generate CSV content using CsvHelper
            // Create the MemoryStream outside of a using block
            var memoryStream = new MemoryStream();
            using (var streamWriter = new StreamWriter(memoryStream, Encoding.UTF8))
            {
                var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = false // Disable automatic header generation
                };

                using (var csvWriter = new CsvWriter(streamWriter, csvConfig))
                {
                    // Manually write the custom header row
                    csvWriter.WriteField("AssignmentID");
                    csvWriter.WriteField("Assignee");
                    csvWriter.WriteField("Assignee Office");
                    csvWriter.WriteField("Asset Name");
                    csvWriter.WriteField("Asset Type");
                    // csvWriter.WriteField("Seat Number"); TODO: Add this when this field is added at the end of sprint 7
                    csvWriter.WriteField("Comment");
                    csvWriter.NextRecord(); // Move to the next line for data

                    // Manually write each record
                    foreach (var assignment in activeItemsFirst)
                    {
                        csvWriter.WriteField(assignment.AssignmentID);
                        csvWriter.WriteField(assignment.Assignee);
                        csvWriter.WriteField(assignment.Office);
                        csvWriter.WriteField(assignment.AssetName);
                        csvWriter.WriteField(assignment.AssetType);
                        csvWriter.WriteField(assignment.Comment);
                        csvWriter.NextRecord(); // Move to the next line
                    }

                    streamWriter.Flush();
                }
            }

            var bytes = memoryStream.ToArray();

            return File(
                bytes,
                contentType: "text/csv",
                fileDownloadName: $"{office.OfficeName}_Report_{creationDate}.csv"
            );

        }
        return Ok();
    }


    [HttpGet("/{id?}/download")]
    public async Task<IActionResult> Download(int? id)
    {
        return Ok();
    }

    [HttpGet("/list")]
    public async Task<IActionResult> GetAll()
    {
        var res = await _reports.GetAllReportsAsync(ct: HttpContext.RequestAborted);
        return Ok(res);
    }
}
