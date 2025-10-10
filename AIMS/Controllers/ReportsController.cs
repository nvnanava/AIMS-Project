using AIMS.Data;
using AIMS.Queries;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CsvHelper; // Requires CsvHelper NuGet package
using System.Text;
using AIMS.Models;
using CsvHelper.Configuration;
using System.Globalization;
using AIMS.ViewModels;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

namespace AIMS.Controllers;

// // With EntraID wired, we gate via policy configured in Program.cs:
// [Authorize(Policy = "mbcAdmin")]
[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly ReportsQuery _reports;
    private readonly AimsDbContext _db;

    // TODO: Using for wwwroot saving. Remove when Blob storage is added
    private readonly IWebHostEnvironment _web;

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
        public DateOnly? Expiration { get; set; }
    }

    public ReportsController(
        ReportsQuery reports,
        AimsDbContext db,
        IWebHostEnvironment web,
        ILogger<ReportsController> logger)
    {
        _reports = reports;
        _db = db;
        _web = web;
        _logger = logger;
    }

    [Produces("text/csv")]
    [HttpPost("/")]
    public async Task<IActionResult> Create(
        [FromQuery] DateOnly start,
        [FromQuery] string reportName,
        [FromQuery] int CreatorUserID,
        [FromQuery] string? type,
        [FromQuery] DateOnly? end,
        [FromQuery] int? OfficeID,
        [FromQuery] string? desc,
        [FromQuery] CustomReportOptionsDto? customOptions,
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

        var dateString = creationDate.ToString("yyyy-MM-dd_HH-mm-ss");

        var fileName = "";
        var spacelessReportName = reportName.Replace(" ", "_");

        var memoryStream = new MemoryStream();
        string? filePath = null;

        // Assignments Report
        // Data In: Start Date, End Date (optional; today if null), Description (optional)
        // Data Out: AssignmentID, Assignee, Office, Asset Name, Asset Type, Asset Seat Number, Comment
        // Notes: File Name should include generation date,
        // Sort: Assigned Assets first, non-assigned later

        if (reportType == ReportType.Assignment)
        {
            fileName = $"{spacelessReportName}_Assignment_Report_{dateString}.csv";
            var folder = "reports";
            var dirPath = Path.Combine(_web.WebRootPath, folder);

            // ensure directory exists
            Directory.CreateDirectory(dirPath);

            filePath = Path.Combine(dirPath, fileName);


            // get assignments, sorted by activity
            var activeItemsFirst = await getOrderedIntermediateListAsync(ct);


            WriteToCSV(memoryStream, activeItemsFirst);
            // save the file
            using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                await memoryStream.CopyToAsync(fileStream);
            }


            // Custom Report
            // Data In: Start Date, End Date (optional), Description (optional)
            // ... See Hardware, See Software, See Users, See Office, See when software and/ or warranty expires, See what requires maintenance or replacements
            // Data Out: Assignee, Office, Asset Name, Asset Type, Seat #, Expiration, MaintenanceStatus, Comment
            // Notes: If MaintenanceStatus opt specified: show only those.
        }
        else if (reportType == ReportType.Custom)
        {
            fileName = $"{spacelessReportName}_Assignment_Report_{dateString}.csv";
            var folder = "reports";
            var dirPath = Path.Combine(_web.WebRootPath, folder);

            // ensure directory exists
            Directory.CreateDirectory(dirPath);

            filePath = Path.Combine(dirPath, fileName);

            if (customOptions is null)
            {
                customOptions = new CustomReportOptionsDto { };
            }

            // get assignments, sorted by activity
            var activeItemsFirst = await getOrderedIntermediateListAsync(ct, customOptions);


            WriteToCSV(memoryStream, activeItemsFirst, customOptions);

            // save the file
            using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                await memoryStream.CopyToAsync(fileStream);
            }

        }
        else if (reportType == ReportType.Office)
        {
            fileName = $"{spacelessReportName}_Assignment_Report_{dateString}.csv";
            var folder = "reports";
            var dirPath = Path.Combine(_web.WebRootPath, folder);

            // ensure directory exists
            Directory.CreateDirectory(dirPath);

            filePath = Path.Combine(dirPath, fileName);

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

            var activeItemsFirst = await getOrderedIntermediateListAsync(ct, customOptions);


            WriteToCSV(memoryStream, activeItemsFirst);

        }
        // 3. Reset the MemoryStream position again for the download
        memoryStream.Position = 0;


        var bytes = memoryStream.ToArray();      // snapshot the data

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await System.IO.File.WriteAllBytesAsync(filePath!, bytes, ct);  // write all bytes


        // Store a relative path (portable)
        var blobUri = Path.Combine("reports", fileName).Replace("\\", "/");
        // create report entry
        await _reports.CreateReport(new CreateReportDto
        {
            Name = reportName,

            Type = type,

            Description = desc,

            DateCreated = creationDate,

            // Who/Where generated
            GeneratedByUserID = CreatorUserID,

            GeneratedByOfficeID = OfficeID,
            BlobUri = blobUri
        }, ct);
        // force download
        Response.Headers.Append("Content-Disposition", $"attachment; filename=\"{fileName}\"");
        // Return the file

        return File(bytes, "text/csv", fileName);

    }


    private async Task<List<CSVIntermediate>> getOrderedIntermediateListAsync(CancellationToken ct, CustomReportOptionsDto? opts = null)
    {
        if (opts is not null)
        {
            // get assignments, sorted by activity
            IQueryable<Assignment> activeItemsFirst =
            _db.Assignments
            .AsNoTracking()
            .OrderBy(item => item.UnassignedAtUtc)
            .Include(a => a.Hardware)
            .Include(a => a.Software);

            if (opts.seeHardware && !opts.seeSoftware)
            {
                activeItemsFirst = activeItemsFirst.Where(a => a.AssetKind == AssetKind.Hardware);
            }
            else if (opts.seeSoftware && !opts.seeHardware)
            {
                activeItemsFirst = activeItemsFirst.Where(a => a.AssetKind == AssetKind.Software);
            }

            if (opts.filterByMaintenance)
            {
                activeItemsFirst = activeItemsFirst.Where(a =>
                // Software does not have survey or repair status, so it will implicitly be excluded from this filter
                a.HardwareID != null ? (a.Hardware!.Status == "In Repair" || a.Hardware.Status == "Marked for Survey") : false
                );
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
                                : a.Software!.Comment,
                Expiration = a.AssetKind == AssetKind.Hardware
                                    ? a.Hardware!.WarrantyExpiration
                                    : a.Software!.SoftwareLicenseExpiration


            })
            .ToListAsync(ct);

            return list;
        }
        else
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

            return activeItemsFirst;
        }

    }
    private async Task WriteToCSV(MemoryStream memoryStream, List<CSVIntermediate> list, CustomReportOptionsDto? opts = null)
    {
        if (opts is not null)
        {
            using (var streamWriter = new StreamWriter(memoryStream, Encoding.UTF8, leaveOpen: true))
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
        else
        {
            using (var streamWriter = new StreamWriter(memoryStream, Encoding.UTF8, leaveOpen: true))
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
                    foreach (var assignment in list)
                    {
                        csvWriter.WriteField(assignment.AssignmentID);
                        csvWriter.WriteField(assignment.Assignee);
                        csvWriter.WriteField(assignment.Office);
                        csvWriter.WriteField(assignment.AssetName);
                        csvWriter.WriteField(assignment.AssetType);
                        csvWriter.WriteField(assignment.Comment);
                        csvWriter.NextRecord(); // Move to the next line
                    }

                    await streamWriter.FlushAsync();
                }
            }

        }
    }

    [HttpGet("/download/{id?}")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Download(int? id, CancellationToken ct = default)
    {
        // make sure that id is not null
        if (id is null)
        {
            ModelState.AddModelError(nameof(id), "Please specify a valid ReportID.");
            return BadRequest(ModelState);
        }


        var report = await _reports.GetReportASync((int)id, ct);

        if (report is null)
        {
            ModelState.AddModelError(nameof(id), "Please specify a valid ReportID.");
            return BadRequest(ModelState);
        }

        // Expect BlobUri like "reports/yourfile.csv" (relative to wwwroot)
        var rel = (report.BlobUri ?? "").Replace('\\', '/').TrimStart('/');

        // Build physical path under wwwroot (and prevent traversal)
        var baseDir = Path.GetFullPath(_web.WebRootPath);
        var fullPath = Path.GetFullPath(Path.Combine(baseDir, rel));
        if (!fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Invalid file path." });

        // Ensure the folder exists (in case first run)
        var dir = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(dir);

        _logger.LogInformation($"{fullPath}");

        // Existence + size checks
        if (!System.IO.File.Exists(fullPath))
            return NotFound($"File not found: {report.BlobUri}");

        var fi = new FileInfo(fullPath);
        _logger.LogInformation("Resolved file: {FilePath}, size: {Len} bytes", fullPath, fi.Length);

        if (fi.Length == 0)
            return StatusCode(StatusCodes.Status409Conflict, "File is empty.");

        // Download the file
        // octet-stream means that the file is a) binary and b) should be offered for download.
        return PhysicalFile(fullPath, "application/octet-stream", fileDownloadName: report.Name, enableRangeProcessing: true);
    }

    [HttpGet("/list")]
    public async Task<IActionResult> GetAll()
    {
        var res = await _reports.GetAllReportsAsync(ct: HttpContext.RequestAborted);
        return Ok(res);
    }
}
