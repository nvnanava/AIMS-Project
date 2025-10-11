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
using System.IO.Abstractions;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.VisualBasic;

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

    private readonly System.IO.Abstractions.IFileSystem _fs;
    private readonly string _rootPath;

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
        ReportsQuery reports,
        IWebHostEnvironment web,
        System.IO.Abstractions.IFileSystem fs)
    {
        _reports = reports;
        _db = db;
        _web = web;
        _fs = fs;

        _rootPath = _fs.Path.Combine(_web.WebRootPath);
    }

    [HttpPost("/")]
    public async Task<IActionResult> Create(
        [FromQuery] DateOnly start,
        [FromQuery] string reportName,
        [FromQuery] int CreatorUserID,
        [FromQuery] string type,
        [FromQuery] DateOnly? end = null,
        [FromQuery] int? OfficeID = null,
        [FromQuery] string? desc = null,
        [FromQuery] CustomReportOptionsDto? customOptions = null,
         CancellationToken ct = default
        )
    {
        // perform basic checking
        if (end is null)
        {
            end = DateOnly.FromDateTime(DateTime.Now);
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


        if (customOptions is null)
        {
            customOptions = new CustomReportOptionsDto { };
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
            var dirPath = _fs.Path.Combine(_web.WebRootPath, folder);

            // ensure directory exists
            _fs.Directory.CreateDirectory(dirPath);

            filePath = _fs.Path.Combine(dirPath, fileName);


            // get assignments, sorted by activity
            var activeItemsFirst = await getOrderedIntermediateListAsync(start, (DateOnly)end, ct);


            await WriteToCSV(memoryStream, activeItemsFirst);


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
            var dirPath = _fs.Path.Combine(_web.WebRootPath, folder);

            // ensure directory exists
            _fs.Directory.CreateDirectory(dirPath);

            filePath = _fs.Path.Combine(dirPath, fileName);

            // get assignments, sorted by activity
            var activeItemsFirst = await getOrderedIntermediateListAsync(start, (DateOnly)end, ct, customOptions);


            await WriteToCSV(memoryStream, activeItemsFirst, customOptions);

        }
        else if (reportType == ReportType.Office)
        {
            fileName = $"{spacelessReportName}_Assignment_Report_{dateString}.csv";
            var folder = "reports";
            var dirPath = _fs.Path.Combine(_web.WebRootPath, folder);

            // ensure directory exists
            _fs.Directory.CreateDirectory(dirPath);

            filePath = _fs.Path.Combine(dirPath, fileName);

            if (OfficeID is null)
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


            await WriteToCSV(memoryStream, activeItemsFirst);

        }
        // 3. Reset the MemoryStream position again for the download
        memoryStream.Position = 0;


        var bytes = memoryStream.ToArray();      // snapshot the data

        _fs.Directory.CreateDirectory(_fs.Path.GetDirectoryName(filePath)!);
        await _fs.File.WriteAllBytesAsync(filePath!, bytes, ct);  // write all bytes


        // Store a relative path (portable)
        var blobUri = _fs.Path.Combine("reports", fileName).Replace("\\", "/");
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

        return new FileContentResult(bytes, "text/csv")
        {
            FileDownloadName = fileName
        };

    }


    private async Task<List<CSVIntermediate>> getOrderedIntermediateListAsync(DateOnly start, DateOnly end, CancellationToken ct, CustomReportOptionsDto? opts = null)
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

        // optional filters
        if (opts is not null)
        {
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
        }

        return await query
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
    }
    private async Task WriteToCSV(MemoryStream memoryStream, List<CSVIntermediate> list, CustomReportOptionsDto? opts = null)
    {
        var utf8WithoutBom = new UTF8Encoding(false);
        if (opts is not null)
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
        else
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
    // for swagger: octet-stream means that the file is a) binary and b) should be offered for download.
    [Produces("application/octet-stream")]
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

        // Build physical path under wwwroot and prevent traversal
        var baseDir = _fs.Path.GetFullPath(_rootPath); // e.g., env.WebRootPath
        var fullPath = _fs.Path.GetFullPath(_fs.Path.Combine(baseDir, rel));
        if (!fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Invalid file path." });

        // Not found? Say so.
        if (!_fs.File.Exists(fullPath))
            return NotFound($"File not found: {report.BlobUri}");

        // Use I/O abstractions for file info
        var fi = _fs.FileInfo.New(fullPath);
        if (fi.Length == 0)
            return StatusCode(StatusCodes.Status409Conflict, "File is empty.");

        // For browsers/clients: the actual file header says text/csv
        Response.ContentType = "text/csv";
        return PhysicalFile(fullPath, "text/csv", fileDownloadName: _fs.Path.GetFileName(fullPath));
    }

    [HttpGet("/list")]
    public async Task<IActionResult> GetAll()
    {
        var res = await _reports.GetAllReportsAsync(ct: HttpContext.RequestAborted);
        return Ok(res);
    }
}
