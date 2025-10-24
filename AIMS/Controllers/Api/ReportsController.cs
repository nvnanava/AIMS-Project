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
        // Assignee values
        public string Assignee { get; set; } = string.Empty;
        public string AssigneeOffice { get; set; } = string.Empty;


        // Asset Values
        public AssetKind AssetType { get; set; }
        public string AssetName { get; set; } = string.Empty;
        public string AssetComment { get; set; } = string.Empty;
        public DateOnly? AssetExpiration { get; set; }
        public string AssetLicenseOrTag { get; set; } = string.Empty; // unique
        public string AssetStatus { get; set; } = string.Empty;


        // AuditLog Actions
        public AuditLogAction Action { get; set; }    // e.g. "Create", "Edit", "Assign", "Archive"
        public string Description { get; set; } = string.Empty;   // human summary
        public DateTime ActionTimestampUtc { get; set; } = DateTime.UtcNow;


        // Record-keeping

        public int AuditLogID { get; set; }
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

        var activeItemsFirst = await getOrderedIntermediateListAsync(start, (DateOnly)end, reportType, ct, customOptions);


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


    private async Task<List<CSVIntermediate>> getOrderedIntermediateListAsync(DateOnly start, DateOnly end, ReportType type, CancellationToken ct, CustomReportOptionsDto opts, int? OfficeID = null)
    {
        var startUtc = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); // UTC day of 00:00:00 (inclusive bound)
        var endUtcExclusive = end.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); // UTC next day 0:00:00 (exclusive)

        // base query: always apply date filtering
        IQueryable<AuditLog> query = _db.AuditLogs
            .AsNoTracking()
            .Where(a =>
                a.TimestampUtc >= startUtc && a.TimestampUtc < endUtcExclusive)
            .OrderBy(a => a.TimestampUtc)
            .Include(a => a.HardwareAsset)
            .Include(a => a.SoftwareAsset)
            .Include(a => a.User)
           .ThenInclude(u => u.Office);

        if (type == ReportType.Assignment)
        {
            query = query.Where(a => a.Action == AuditLogAction.Assign || a.Action == AuditLogAction.Unassign);
        }
        else if (type == ReportType.Office)
        {
            query = query.Where(a => a.User.OfficeID == OfficeID);
        }

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
                (a.HardwareAsset!.Status == "In Repair" || a.HardwareAsset.Status == "Marked for Survey"));
        }

        return await query
            .Select(a => new CSVIntermediate
            {
                AuditLogID = a.AuditLogID,

                // Assignee info
                Assignee = a.User!.FullName,
                AssigneeOffice = (a.User == null || a.User.Office == null || a.User.Office.OfficeName == null) ? "N/A" : a.User.Office.OfficeName,

                // Asset Info
                AssetType = a.AssetKind,
                AssetName = a.AssetKind == AssetKind.Hardware
                                ? a.HardwareAsset!.AssetName
                                : a.SoftwareAsset!.SoftwareName,
                AssetComment = a.AssetKind == AssetKind.Hardware
                                ? a.HardwareAsset!.Comment
                                : a.SoftwareAsset!.Comment,
                AssetExpiration = a.AssetKind == AssetKind.Hardware
                                ? a.HardwareAsset!.WarrantyExpiration
                                : a.SoftwareAsset!.SoftwareLicenseExpiration,

                AssetLicenseOrTag = a.AssetKind == AssetKind.Hardware
                                    ? a.HardwareAsset!.SerialNumber
                                    : a.SoftwareAsset!.SoftwareLicenseKey,

                AssetStatus = a.AssetKind == AssetKind.Hardware
                                    ? a.HardwareAsset!.Status
                                    : $"{a.SoftwareAsset!.LicenseTotalSeats - a.SoftwareAsset!.LicenseSeatsUsed} Seats Remaining",

                // AuditLogInformation
                Action = a.Action,
                Description = a.Description,
                ActionTimestampUtc = a.TimestampUtc

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
                    csvWriter.WriteField("AuditLogID");

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
                    csvWriter.WriteField("Asset License or Serial");

                    csvWriter.WriteField("Action");
                    csvWriter.WriteField("Description");
                    csvWriter.WriteField("Timestamp");

                    csvWriter.WriteField("Asset Comment");
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
                    foreach (CSVIntermediate auditLogEntry in list)
                    {
                        csvWriter.WriteField(auditLogEntry.AuditLogID);

                        if (opts.seeUsers)
                        {
                            csvWriter.WriteField(auditLogEntry.Assignee);
                        }

                        if (opts.seeOffice)
                        {
                            csvWriter.WriteField(auditLogEntry.AssigneeOffice);
                        }
                        csvWriter.WriteField(auditLogEntry.AssetName);
                        csvWriter.WriteField(auditLogEntry.AssetType);
                        csvWriter.WriteField(auditLogEntry.AssetLicenseOrTag);

                        csvWriter.WriteField(auditLogEntry.Action);
                        csvWriter.WriteField(auditLogEntry.Description);
                        csvWriter.WriteField(auditLogEntry.ActionTimestampUtc);

                        csvWriter.WriteField(auditLogEntry.AssetComment);
                        if (opts.seeExpiration)
                        {
                            csvWriter.WriteField(auditLogEntry.AssetExpiration);
                        }
                        if (opts.filterByMaintenance)
                        {
                            csvWriter.WriteField(auditLogEntry.AssetStatus);
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
