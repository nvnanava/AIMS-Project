using System.Globalization;
using System.Text;
using System.Text.Json;
using AIMS.Data;
using AIMS.Dtos.Reports;
using AIMS.Models;
using AIMS.Queries;
using CsvHelper; // Requires CsvHelper NuGet package
using CsvHelper.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIMS.Controllers.Api;

// With EntraID wired, we gate via policy configured in Program.cs:
// [Authorize(Policy = "mbcAdmin")]
[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly ReportsQuery _reports;
    private readonly AimsDbContext _db;



    private sealed class CSVIntermediate
    {
        public int AssignmentID { get; set; }
        public string Assignee { get; set; } = string.Empty;
        public string Office { get; set; } = string.Empty;
        public AssetKind AssetType { get; set; }
        public string AssetName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
        public DateOnly? Expiration { get; set; }
    }

    public ReportsController(
        AimsDbContext db,
        ReportsQuery reports
        )
    {
        _reports = reports;
        _db = db;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromQuery] DateOnly start,
        [FromQuery] string reportName,
        [FromQuery] int CreatorUserID,
        [FromQuery] string type,
        [FromQuery] DateOnly? end = null,
        [FromQuery] int? OfficeID = null,
        [FromQuery] string? desc = null,
        [FromBody] CustomReportOptionsDto? customOptions = null,
         CancellationToken ct = default
        )
    {
        // perform basic checking
        if (end is null)
        {
            end = DateOnly.FromDateTime(DateTime.Now);
        }
        if (customOptions is null)
        {
            customOptions = new CustomReportOptionsDto { };
        }
        if (OfficeID is not null)
        {
            var office = await _db.Offices.Where(o => o.OfficeID == OfficeID).FirstOrDefaultAsync(ct);

            if (office is null)
            {
                ModelState.AddModelError(nameof(OfficeID), "Please specify a valid OfficeID.");
                return BadRequest(ModelState);
            }
        }

        // check user
        var user = await _db.Users.Where(u => u.UserID == CreatorUserID).FirstOrDefaultAsync(ct);
        if (user is null)
        {
            ModelState.AddModelError(nameof(CreatorUserID), "Please specify a valid CreatorUserID.");
            return BadRequest(ModelState);
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

        byte[] reportBytes = Array.Empty<byte>();

        // Assignments Report
        // Data In: Start Date, End Date (optional; today if null), Description (optional)
        // Data Out: AssignmentID, Assignee, Office, Asset Name, Asset Type, Asset Seat Number, Comment
        // Notes: File Name should include generation date,
        // Sort: Assigned Assets first, non-assigned later


        // Custom Report
        // Data In: Start Date, End Date (optional), Description (optional)
        // ... See Hardware, See Software, See Users, See Office, See when software and/ or warranty expires, See what requires maintenance or replacements
        // Data Out: Assignee, Office, Asset Name, Asset Type, Seat #, Expiration, MaintenanceStatus, Comment
        // Notes: If MaintenanceStatus opt specified: show only those.
        if (reportType == ReportType.Office && OfficeID is null)
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

        var activeItemsFirst = await getOrderedIntermediateListAsync(start, (DateOnly)end, ct, customOptions);


        reportBytes = await WriteToBinaryCSV(activeItemsFirst, customOptions);

        // create report entry
        var id = await _reports.CreateReport(new CreateReportDto
        {
            Name = reportName,

            Type = type,

            Description = desc,

            // Who/Where generated
            GeneratedByUserID = CreatorUserID,

            GeneratedForOfficeID = OfficeID,
            Content = reportBytes
        }, ct);

        return Ok(new CreateReportResponseDto { ReportID = id, ContentLength = reportBytes.Length });
    }


    private async Task<List<CSVIntermediate>> getOrderedIntermediateListAsync(DateOnly start, DateOnly end, CancellationToken ct, CustomReportOptionsDto opts)
    {
        var startUtc = start.ToDateTime(TimeOnly.MinValue).ToUniversalTime(); // UTC day of 00:00:00 (inclusive bound)
        var endUtcExclusive = end.AddDays(1).ToDateTime(TimeOnly.MinValue).ToUniversalTime(); // UTC next day 0:00:00 (exclusive)

        // base query: always apply date filtering
        IQueryable<Assignment> query = _db.Assignments
            .AsNoTracking()
            .Where(a =>
                (a.AssignedAtUtc >= startUtc && a.AssignedAtUtc < endUtcExclusive)
                ||
                (a.UnassignedAtUtc.HasValue &&
                 a.UnassignedAtUtc.Value >= startUtc &&
                 a.UnassignedAtUtc.Value < endUtcExclusive)
            )
            .OrderBy(a => a.UnassignedAtUtc)
            .Include(a => a.Hardware)
            .Include(a => a.Software);

        if (opts.seeHardware && !opts.seeSoftware)
        {
            query = query.Where(a => a.AssetKind == AssetKind.Hardware);
        }
        else if (opts.seeSoftware && !opts.seeHardware)
        {
            query = query.Where(a => a.AssetKind == AssetKind.Software);
        }

        if (opts.filterByMaintenance)
        {
            query = query.Where(a =>
                a.HardwareID != null &&
                (a.Hardware!.Status == "In Repair" || a.Hardware.Status == "Marked for Survey"));
        }

        return await query
            .Select(a => new CSVIntermediate
            {
                AssignmentID = a.AssignmentID,
                Assignee = a.User!.FullName,
                Office = a.User!.Office!.OfficeName,
                AssetType = a.AssetKind,
                AssetName = a.AssetKind == AssetKind.Hardware
                                ? a.Hardware!.AssetName
                                : a.Software!.SoftwareName,
                Status = a.AssetKind == AssetKind.Hardware
                                ? a.Hardware!.Status
                                : string.Empty,
                Comment = a.AssetKind == AssetKind.Hardware
                                ? a.Hardware!.Comment
                                : a.Software!.Comment,
                Expiration = a.AssetKind == AssetKind.Hardware
                                ? a.Hardware!.WarrantyExpiration
                                : a.Software!.SoftwareLicenseExpiration
            })
            .ToListAsync(ct);
    }
    private async Task<byte[]> WriteToBinaryCSV(List<CSVIntermediate> list, CustomReportOptionsDto opts)
    {
        var memoryStream = new MemoryStream();

        var utf8WithoutBom = new UTF8Encoding(false);
        {
            using (var streamWriter = new StreamWriter(memoryStream, utf8WithoutBom, leaveOpen: true))
            {
                var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = false // Disable automatic header generation
                };

                using (var csvWriter = new CsvWriter(streamWriter, csvConfig))
                {
                    // Manually write the custom header row
                    csvWriter.WriteField("AssignmentID");

                    if (opts.seeUsers)
                    {
                        csvWriter.WriteField("Assignee");
                    }

                    if (opts.seeOffice)
                    {
                        csvWriter.WriteField("Assignee Office");
                    }
                    csvWriter.WriteField("Asset Name");
                    csvWriter.WriteField("Asset Type");
                    // csvWriter.WriteField("Seat Number"); TODO: Add this when this field is added at the end of sprint 7
                    csvWriter.WriteField("Comment");
                    if (opts.seeExpiration)
                    {
                        csvWriter.WriteField("Expiration");
                    }
                    if (opts.filterByMaintenance)
                    {
                        csvWriter.WriteField("Status");
                    }
                    csvWriter.NextRecord(); // Move to the next line for data

                    // Manually write each record
                    foreach (CSVIntermediate assignment in list)
                    {
                        csvWriter.WriteField(assignment.AssignmentID);
                        if (opts.seeUsers)
                        {
                            csvWriter.WriteField(assignment.Assignee);
                        }
                        if (opts.seeOffice)
                        {

                            csvWriter.WriteField(assignment.Office);
                        }
                        csvWriter.WriteField(assignment.AssetName);
                        csvWriter.WriteField(assignment.AssetType);
                        csvWriter.WriteField(assignment.Comment);
                        if (opts.seeExpiration)
                        {
                            csvWriter.WriteField(assignment.Expiration);
                        }
                        if (opts.filterByMaintenance)
                        {
                            csvWriter.WriteField(assignment.Status);
                        }
                        csvWriter.NextRecord(); // Move to the next line
                    }

                    await streamWriter.FlushAsync();
                }
            }
        }

        // Reset the MemoryStream's position to the beginning
        memoryStream.Position = 0;

        // Convert the MemoryStream to a byte array
        return memoryStream.ToArray();
    }

    [HttpGet("download/{id?}")]
    // for swagger: octet-stream means that the file is a) binary and b) should be offered for download.
    [Produces("text/csv")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Download(int? id, CancellationToken ct = default)
    {
        // make sure that id is not null
        if (id is null)
        {
            ModelState.AddModelError(nameof(id), "Please specify a valid ReportID.");
            return BadRequest(ModelState);
        }


        var report = await _reports.GetReportForDownload((int)id, ct);

        if (report is null)
        {
            ModelState.AddModelError(nameof(id), "Please specify a valid ReportID.");
            return BadRequest(ModelState);
        }


        // For browsers/clients: the actual file header says text/csv
        return File(report.Content, "text/csv", $"{report.Name}_{report.Type}_Report_{report.DateCreated:yyyy_MM_dd_HH_mm_ss}.csv");
    }

    [HttpGet("list")]
    public async Task<IActionResult> GetAll()
    {
        var res = await _reports.GetAllReportsAsync(ct: HttpContext.RequestAborted);
        return Ok(res);
    }
}
