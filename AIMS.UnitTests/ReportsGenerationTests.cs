using System.Reflection;
using System.Text.Json;
using AIMS.Controllers.Api;
using AIMS.Data;
using AIMS.Dtos.Reports;
using AIMS.Models;
using AIMS.Queries;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Moq;
using Xunit.Abstractions;

public class ReportsGenerationTests
{
    private readonly ITestOutputHelper _output;

    public ReportsGenerationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // Helper: create controller with in-memory DB and seed if provided
    private ReportsController CreateControllerWithDb(string dbName, List<Assignment>? seedAssignments = null)
    {
        var options = new DbContextOptionsBuilder<AimsDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            // .EnableSensitiveDataLogging() // uncomment if we want EF to print conflicting keys
            .Options;

        var db = new AimsDbContext(options);

        // Ensure the Office used by tests exists (for OfficeID = 15000)
        if (!db.Offices.Any(o => o.OfficeID == 15000))
        {
            db.Offices.Add(new Office
            {
                OfficeID = 15000,
                OfficeName = "Yolo",
                Location = "Placerville"
            });
        }

        // Seed the "creator" user the controller expects:
        // tests pass CreatorUserID = 1 → controller looks up Users by GraphObjectID == "1"
        if (!db.Users.Any(u => u.UserID == 1))
        {
            db.Users.Add(new User
            {
                UserID = 1,
                FullName = "John Smith",
                GraphObjectID = "1",      // MUST match CreatorUserID.ToString()
                OfficeID = 15000
            });
        }

        // Add assignments if provided, but REMOVE navigation objects to avoid duplicate tracking
        if (seedAssignments != null && seedAssignments.Count > 0)
        {
            foreach (var a in seedAssignments)
            {
                // keep only the FK values; navigation props cause duplicate tracked entities
                a.User = null;
                a.Hardware = null;
                a.Software = null;
            }

            db.Assignments.AddRange(seedAssignments);
        }

        db.SaveChanges();

        var reportsQuery = new ReportsQuery(db);

        // Mock IWebHostEnvironment
        var mockEnv = new Mock<IWebHostEnvironment>();
        mockEnv.Setup(e => e.WebRootPath).Returns("wwwroot");
        mockEnv.Setup(e => e.ContentRootPath).Returns(Directory.GetCurrentDirectory());
        mockEnv.Setup(e => e.EnvironmentName).Returns("Testing");
        mockEnv.Setup(e => e.ApplicationName).Returns("TestApp");

        return new ReportsController(db, reportsQuery, mockEnv.Object);
    }

    private static async Task<IActionResult> CreateCompat(
        ReportsController controller,
        DateOnly start,
        string reportName,
        int CreatorUserID,
        string type,
        DateOnly? end = null,
        int? OfficeID = null,
        string? desc = null,
        CustomReportOptionsDto? customOptions = null)
    {
        // Try DTO endpoint first
        var dtoMethod = controller.GetType().GetMethod(
            "CreateReport",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { typeof(CreateReportDto), typeof(CancellationToken) },
            modifiers: null);

        if (dtoMethod != null)
        {
            var dto = new CreateReportDto
            {
                Name = reportName,
                Type = type,
                Description = desc,
                // Controller now uses AAD GraphObjectID (string). Use the int as its string form.
                GeneratedByUserID = CreatorUserID,          // stays int in DTO
                GeneratedForOfficeID = OfficeID,
                // Content will be filled by the controller/query; DateCreated defaulted
            };

            var task = (Task<IActionResult>)dtoMethod.Invoke(controller, new object?[] { dto, CancellationToken.None })!;
            return await task.ConfigureAwait(false);
        }

        // Fallback: legacy Create(...) method – map by name and coerce types.
        var legacy = controller.GetType().GetMethod("Create", BindingFlags.Instance | BindingFlags.Public);
        if (legacy == null)
            throw new MissingMethodException("Neither CreateReport(dto, ct) nor Create(...) was found on ReportsController.");

        var ps = legacy.GetParameters();
        object?[] args = new object?[ps.Length];

        for (int i = 0; i < ps.Length; i++)
        {
            var p = ps[i];
            object? val = p.Name switch
            {
                "start" => start,
                "reportName" => reportName,
                "CreatorUserID" => null,  // set below with type-aware coercion
                "type" => type,
                "end" => end,
                "OfficeID" => OfficeID,
                "desc" => desc,
                "customOptions" => customOptions,
                "ct" => CancellationToken.None,
                _ => p.HasDefaultValue ? p.DefaultValue : null
            };

            if (p.Name == "CreatorUserID")
            {
                // If controller expects string (GraphObjectID), use int->string
                if (p.ParameterType == typeof(string))
                    val = CreatorUserID.ToString();
                else
                    val = CreatorUserID; // old signature was int
            }

            args[i] = val;
        }

        var legacyTask = legacy.Invoke(controller, args);
        return await (legacyTask as Task<IActionResult>)!;
    }

    public List<Assignment> CreateSeedData() => new()
    {
        new Assignment
        {
            AssignmentID = 4,
            UserID = 4,
            User = new User { FullName = "Robin Williams" },
            AssetKind = AssetKind.Software,
            HardwareID = null,
            SoftwareID = 1,
            AssignedAtUtc = DateTime.Now.AddDays(-1),
            UnassignedAtUtc = null
        },
        new Assignment
        {
            AssignmentID = 11,
            UserID = 9,
            User = new User { FullName = "Kate Rosenberg" },
            AssetKind = AssetKind.Hardware,
            HardwareID = 3,
            SoftwareID = null,
            AssignedAtUtc = DateTime.Now.AddDays(-5),
            UnassignedAtUtc = null
        },
        new Assignment
        {
            AssignmentID = 10,
            UserID = 11,
            User = new User { FullName = "Bruce Wayne" },
            AssetKind = AssetKind.Hardware,
            HardwareID = 13,
            SoftwareID = null,
            AssignedAtUtc = DateTime.Now.AddDays(-4),
            UnassignedAtUtc = null
        },
        new Assignment
        {
            AssignmentID = 8,
            UserID = 8,
            User = new User { FullName = "Maximillian Brandt" },
            AssetKind = AssetKind.Hardware,
            HardwareID = 8,
            SoftwareID = null,
            AssignedAtUtc = DateTime.Now.AddDays(1),
            UnassignedAtUtc = null
        },
        new Assignment
        {
            AssignmentID = 5,
            UserID = 5,
            User = new User { FullName = "Sarah Johnson" },
            AssetKind = AssetKind.Hardware,
            HardwareID = 5,
            SoftwareID = null,
            AssignedAtUtc = DateTime.Now.AddDays(4),
            UnassignedAtUtc = null
        },
        new Assignment
        {
            AssignmentID = 2,
            UserID = 2,
            User = new User { FullName = "Jane Doe" },
            AssetKind = AssetKind.Hardware,
            HardwareID = 17000,
            Hardware = new Hardware { HardwareID = 17000, AssetType = "Hardware", Status = "Marked for Survey" },
            SoftwareID = null,
            AssignedAtUtc = DateTime.Now.AddDays(5),
            UnassignedAtUtc = null
        },
        new Assignment
        {
            AssignmentID = 1,
            UserID = 1,
            User = new User { FullName = "John Smith" },
            AssetKind = AssetKind.Hardware,
            HardwareID = 15000,
            Hardware = new Hardware { HardwareID = 15000, AssetType = "Hardware", Status = "In Repair" },
            SoftwareID = null,
            AssignedAtUtc = DateTime.Now,
            UnassignedAtUtc = null
        }
    };

    [Fact]
    public async Task CreateAssignmentReport_ReturnsBadRequest_EndDateIsBeforeStart()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        /*
            Task<IActionResult> ReportsController.Create(
                DateOnly start, 
                string reportName, 
                int CreatorUserID, 
                string? type, 
                DateOnly? end, 
                int? OfficeID, 
                string? desc, 
                CustomReportOptionsDto? customOptions, 
                CancellationToken ct = default)
        */
        var result = await CreateCompat(
            controller,
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
            end: DateOnly.FromDateTime(DateTime.Now.AddDays(-15)),
            reportName: "Test Report",
            CreatorUserID: 1,
            type: "Assignment"
        );
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateAssignmentReport_ReturnsIdAndLen_CorrectInputs()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await CreateCompat(
            controller,
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
            end: DateOnly.FromDateTime(DateTime.Now.AddDays(-5)),
            reportName: "Test Report",
            CreatorUserID: 1,
            type: "Assignment"
        );
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedResponse = Assert.IsType<CreateReportResponseDto>(okResult.Value);

        Assert.True(returnedResponse.ReportID >= 0);
        Assert.True(returnedResponse.ContentLength > 0);
    }

    [Fact]
    public async Task CreateAssignmentReport_ReturnsIdAndLen_CorrectInputsDateFilter()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await CreateCompat(
            controller,
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-1)),
            reportName: "Test Report",
            CreatorUserID: 1,
            type: "Assignment"
        );
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedResponse = Assert.IsType<CreateReportResponseDto>(okResult.Value);

        Assert.True(returnedResponse.ReportID >= 0);
        Assert.True(returnedResponse.ContentLength > 0);
    }

    [Fact]
    public async Task CreateAssignmentReport_ReturnsBadRequest_InvalidOfficeID()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await CreateCompat(
            controller,
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
            end: DateOnly.FromDateTime(DateTime.Now.AddDays(-5)),
            reportName: "Test Report",
            CreatorUserID: 1,
            OfficeID: 10000,
            type: "Assignment"
        );
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateAssignmentReport_ReturnsBadRequest_InvalidCreatorUserID()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await CreateCompat(
            controller,
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
            end: DateOnly.FromDateTime(DateTime.Now.AddDays(-5)),
            reportName: "Test Report",
            CreatorUserID: 15000,
            type: "Assignment"
        );
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateOfficeReport_ReturnsBadRequest_InvalidOfficeID()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await CreateCompat(
            controller,
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
            end: DateOnly.FromDateTime(DateTime.Now.AddDays(-5)),
            reportName: "Test Report",
            CreatorUserID: 1,
            OfficeID: 10000,
            type: "Office"
        );
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateOfficeReport_ReturnsBadRequest_EndDateIsBeforeStart()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await CreateCompat(
            controller,
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
            end: DateOnly.FromDateTime(DateTime.Now.AddDays(-15)),
            reportName: "Test Report",
            CreatorUserID: 1,
            OfficeID: 10000,
            type: "Office"
        );
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateOfficeReport_ReturnsBadRequest_InvalidCreatorUserID()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await CreateCompat(
            controller,
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
            end: DateOnly.FromDateTime(DateTime.Now.AddDays(-5)),
            reportName: "Test Report",
            CreatorUserID: 15000,
            type: "Office"
        );
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateOfficeReport_ReturnsIdAndLen_CorrectInputs()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await CreateCompat(
            controller,
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
            end: DateOnly.FromDateTime(DateTime.Now.AddDays(-5)),
            reportName: "Test Report",
            CreatorUserID: 1,
            OfficeID: 15000,
            type: "Office"
        );
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedResponse = Assert.IsType<CreateReportResponseDto>(okResult.Value);

        Assert.True(returnedResponse.ReportID >= 0);
        Assert.True(returnedResponse.ContentLength > 0);
    }

    [Fact]
    public async Task CreateCustomReport_ReturnsBadRequest_EndDateIsBeforeStart()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await CreateCompat(
            controller,
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
            end: DateOnly.FromDateTime(DateTime.Now.AddDays(-15)),
            reportName: "Test Report",
            CreatorUserID: 1,
            type: "Custom"
        );
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateCustomReport_ReturnsBadRequest_InvalidOfficeID()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await CreateCompat(
            controller,
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
            end: DateOnly.FromDateTime(DateTime.Now.AddDays(-5)),
            reportName: "Test Report",
            CreatorUserID: 1,
            OfficeID: 10000,
            type: "Custom"
        );
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateCustomReport_ReturnsBadRequest_InvalidCreatorUserID()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await CreateCompat(
            controller,
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
            end: DateOnly.FromDateTime(DateTime.Now.AddDays(-5)),
            reportName: "Test Report",
            CreatorUserID: 15000,
            type: "Custom"
        );
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateCustomReport_ReturnsIdAndLen_SeeHardwareOnly()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await CreateCompat(
            controller,
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-1)),
            reportName: "Test Report",
            CreatorUserID: 1,
            type: "Custom",
            customOptions: new CustomReportOptionsDto
            {
                seeHardware = true,
                seeSoftware = false,
                seeExpiration = false,
                seeOffice = false,
                seeUsers = false,
                filterByMaintenance = false
            }
        );
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedResponse = Assert.IsType<CreateReportResponseDto>(okResult.Value);

        Assert.True(returnedResponse.ReportID >= 0);
        Assert.True(returnedResponse.ContentLength > 0);
    }

    [Fact]
    public async Task CreateCustomReport_ReturnsIdAndLen_SeeSoftwareOnly()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await CreateCompat(
            controller,
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-1)),
            reportName: "Test Report",
            CreatorUserID: 1,
            type: "Custom",
            customOptions: new CustomReportOptionsDto
            {
                seeHardware = false,
                seeSoftware = true,
                seeExpiration = false,
                seeOffice = false,
                seeUsers = false,
                filterByMaintenance = false
            }
        );
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedResponse = Assert.IsType<CreateReportResponseDto>(okResult.Value);

        Assert.True(returnedResponse.ReportID >= 0);
        Assert.True(returnedResponse.ContentLength > 0);
    }

    [Fact]
    public async Task CreateCustomReport_ReturnsIdAndLen_SeeUsers()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await CreateCompat(
            controller,
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
            end: DateOnly.FromDateTime(DateTime.Now.AddDays(-5)),
            reportName: "Test Report",
            CreatorUserID: 1,
            type: "Custom",
            customOptions: new CustomReportOptionsDto
            {
                seeOffice = false,
                seeUsers = true,
            }
        );
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedResponse = Assert.IsType<CreateReportResponseDto>(okResult.Value);

        Assert.True(returnedResponse.ReportID >= 0);
        Assert.True(returnedResponse.ContentLength > 0);
    }

    [Fact]
    public async Task CreateCustomReport_ReturnsIdAndLen_SeeOffice()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await CreateCompat(
            controller,
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
            end: DateOnly.FromDateTime(DateTime.Now.AddDays(-5)),
            reportName: "Test Report",
            CreatorUserID: 1,
            type: "Custom",
            customOptions: new CustomReportOptionsDto
            {
                seeUsers = true,
            }
        );
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedResponse = Assert.IsType<CreateReportResponseDto>(okResult.Value);

        Assert.True(returnedResponse.ReportID >= 0);
        Assert.True(returnedResponse.ContentLength > 0);
    }

    [Fact]
    public async Task CreateCustomReport_ReturnsIdAndLen_SeeExpiration()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await CreateCompat(
            controller,
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
            end: DateOnly.FromDateTime(DateTime.Now.AddDays(-5)),
            reportName: "Test Report",
            CreatorUserID: 1,
            type: "Custom",
            customOptions: new CustomReportOptionsDto
            {
                seeExpiration = true
            }
        );
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedResponse = Assert.IsType<CreateReportResponseDto>(okResult.Value);

        Assert.True(returnedResponse.ReportID >= 0);
        Assert.True(returnedResponse.ContentLength > 0);
    }

    [Fact]
    public async Task CreateCustomReport_ReturnsIdAndLen_FilterByMaintenance()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await CreateCompat(
            controller,
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-1)),
            reportName: "Test Report",
            CreatorUserID: 1,
            type: "Custom",
            customOptions: new CustomReportOptionsDto
            {
                filterByMaintenance = true
            }
        );

        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedResponse = Assert.IsType<CreateReportResponseDto>(okResult.Value);

        Assert.True(returnedResponse.ReportID >= 0);
        Assert.True(returnedResponse.ContentLength > 0);
    }
}
