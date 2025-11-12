using System.IO.Abstractions.TestingHelpers;
using System.Text;
using System.Text.Json;
using AIMS.Controllers.Api;
using AIMS.Data;
using AIMS.Dtos.Reports;
using AIMS.Models;
using AIMS.Queries;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq; // For mocking WebHostLogic
using Xunit.Abstractions;

public class ReportsGenerationTests
{
    private readonly ITestOutputHelper _output;

    public ReportsGenerationTests(ITestOutputHelper output)
    {
        _output = output;
    }
    // Helper method for creating controller with in-memory db
    private ReportsController CreateControllerWithDb(string dbName, List<Assignment>? seedAssignments = null)
    {
        var options = new DbContextOptionsBuilder<AimsDbContext>()
            .UseInMemoryDatabase(databaseName: dbName) // unique per test
            .Options;

        var db = new AimsDbContext(options);

        // Seed hardware if provided
        if (seedAssignments != null)
        {
            db.Assignments.AddRange(seedAssignments);
            db.Offices.Add(new Office
            {
                OfficeID = 15000,
                OfficeName = "Yolo",
                Location = "Placerville"
            });
            db.SaveChanges();
        }

        var reportsQuery = new ReportsQuery(db);

        return new ReportsController(db, reportsQuery);
    }

    public List<Assignment> CreateSeedData()
    {
        return new List<Assignment>
            {
                new Assignment {
                    AssignmentID = 4,
                    UserID = 4,
                    User = new User { FullName = "Robin Williams" },
                    // OfficeID = 1,
                    // Office = new Office {OfficeName = "Bethesda"},
                    AssetKind = AssetKind.Software,
                    HardwareID = null,
                    SoftwareID = 1,
                    AssignedAtUtc = DateTime.Now.AddDays(-1),
                    UnassignedAtUtc = null
                },
                new Assignment{
                    AssignmentID = 11,
                    UserID = 9,
                    User = new User { FullName = "Kate Rosenberg" },
                    // OfficeID = 2,
                    // Office = new Office {OfficeName = "Activision"},
                    AssetKind = AssetKind.Hardware,
                    HardwareID = 3,
                    SoftwareID = null,
                    AssignedAtUtc =DateTime.Now.AddDays(-5),
                    UnassignedAtUtc = null
                },
                new Assignment{
                    AssignmentID = 10,
                    UserID = 11,
                    User = new User { FullName = "Bruce Wayne" },
                    // OfficeID = 3,
                    // Office = new Office {OfficeName = "Blizzard"},
                    AssetKind =  AssetKind.Hardware,
                    HardwareID = 13,
                    SoftwareID = null,
                    AssignedAtUtc = DateTime.Now.AddDays(-4),
                    UnassignedAtUtc = null
                },
                new Assignment  {
                    AssignmentID = 8,
                    UserID = 8,
                    User = new User { FullName = "Maximillian Brandt" },
                    // OfficeID = 4,
                    // Office = new Office {OfficeName = "DICE"},
                    AssetKind =  AssetKind.Hardware,
                    HardwareID = 8,
                    SoftwareID = null,
                    AssignedAtUtc = DateTime.Now.AddDays(1),
                    UnassignedAtUtc = null
                },
                new Assignment  {
                    AssignmentID = 5,
                    UserID = 5,
                    User = new User { FullName = "Sarah Johnson" },
                    // OfficeID = 3,
                    // Office = new Office {OfficeName = "Blizzard"},
                    AssetKind =  AssetKind.Hardware,
                    HardwareID = 5,
                    SoftwareID = null,
                    AssignedAtUtc = DateTime.Now.AddDays(4),
                    UnassignedAtUtc = null
                },
                new Assignment  {
                    AssignmentID = 2,
                    UserID = 2,
                    User = new User { FullName = "Jane Doe" },
                    // OfficeID = 4,
                    // Office = new Office {OfficeName = "DICE"},
                    AssetKind =  AssetKind.Hardware,
                    HardwareID = 17000,
                Hardware = new Hardware {HardwareID = 17000, AssetType = "Hardware", Status = "Marked for Survey"},
                    SoftwareID = null,
                    AssignedAtUtc = DateTime.Now.AddDays(5),
                    UnassignedAtUtc = null
                },
                new Assignment  {
                    AssignmentID = 1,
                    UserID = 1,
                    User = new User { FullName = "John Smith" },
                    // OfficeID = 2,
                    // Office = new Office {OfficeName = "Activision"},
                    AssetKind =  AssetKind.Hardware,
                    HardwareID = 15000,
                    Hardware = new Hardware {HardwareID = 15000, AssetType = "Hardware", Status = "In Repair"},
                    SoftwareID = null,
                    AssignedAtUtc = DateTime.Now,
                    UnassignedAtUtc = null
                }
            };
    }

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
        var result = await controller.Create(
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
            end: DateOnly.FromDateTime(DateTime.Now.AddDays(-15)),
            reportName: "Test Report",
            CreatorUserID: 1,
            type: "Assignment"
        );
        Assert.IsType<BadRequestObjectResult>(result); ;
    }

    [Fact]
    public async Task CreateAssignmentReport_ReturnsIdAndLen_CorrectInputs()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await controller.Create(
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
            end: DateOnly.FromDateTime(DateTime.Now.AddDays(-5)),
            reportName: "Test Report",
            CreatorUserID: 1,
            type: "Assignment"
        );
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedResponse = Assert.IsType<CreateReportResponseDto>(okResult.Value);
        // Access fields
        var id = returnedResponse.ReportID;
        var len = returnedResponse.ContentLength;

        Assert.True(!(id < 0));
        Assert.True(len > 0);
    }
    [Fact]
    public async Task CreateAssignmentReport_ReturnsIdAndLen_CorrectInputsDateFilter()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await controller.Create(
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-1)),
            reportName: "Test Report",
            CreatorUserID: 1,
            type: "Assignment"
        );
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedResponse = Assert.IsType<CreateReportResponseDto>(okResult.Value);
        // Access fields
        var id = returnedResponse.ReportID;
        var len = returnedResponse.ContentLength;

        Assert.True(!(id < 0));
        Assert.True(len > 0);
    }
    [Fact]
    public async Task CreateAssignmentReport_ReturnsBadRequest_InvalidOfficeID()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await controller.Create(
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
            end: DateOnly.FromDateTime(DateTime.Now.AddDays(-5)),
            reportName: "Test Report",
            CreatorUserID: 1,
            OfficeID: 10000,
            type: "Assignment"
        );
        Assert.IsType<BadRequestObjectResult>(result); ;
    }
    [Fact]
    public async Task CreateAssignmentReport_ReturnsBadRequest_InvalidCreatorUserID()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await controller.Create(
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
            end: DateOnly.FromDateTime(DateTime.Now.AddDays(-5)),
            reportName: "Test Report",
            CreatorUserID: 15000,
            type: "Assignment"
        );
        Assert.IsType<BadRequestObjectResult>(result); ;
    }
    [Fact]
    public async Task CreateOfficeReport_ReturnsBadRequest_InvalidOfficeID()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await controller.Create(
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
            end: DateOnly.FromDateTime(DateTime.Now.AddDays(-5)),
            reportName: "Test Report",
            CreatorUserID: 1,
            OfficeID: 10000,
            type: "Office"
        );
        Assert.IsType<BadRequestObjectResult>(result); ;
    }
    [Fact]
    public async Task CreateOfficeReport_ReturnsBadRequest_EndDateIsBeforeStart()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await controller.Create(
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
            end: DateOnly.FromDateTime(DateTime.Now.AddDays(-5)),
            reportName: "Test Report",
            CreatorUserID: 1,
            OfficeID: 10000,
            type: "Office"
        );
        Assert.IsType<BadRequestObjectResult>(result); ;
    }
    [Fact]
    public async Task CreateOfficeReport_ReturnsBadRequest_InvalidCreatorUserID()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await controller.Create(
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
            end: DateOnly.FromDateTime(DateTime.Now.AddDays(-5)),
            reportName: "Test Report",
            CreatorUserID: 15000,
            type: "Office"
        );
        Assert.IsType<BadRequestObjectResult>(result); ;
    }
    [Fact]
    public async Task CreateOfficeReport_ReturnsIdAndLen_CorrectInputs()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await controller.Create(
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
            end: DateOnly.FromDateTime(DateTime.Now.AddDays(-5)),
            reportName: "Test Report",
            CreatorUserID: 1,
            OfficeID: 15000,
            type: "Office"
        );
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedResponse = Assert.IsType<CreateReportResponseDto>(okResult.Value);
        // Access fields
        var id = returnedResponse.ReportID;
        var len = returnedResponse.ContentLength;

        Assert.True(!(id < 0));
        Assert.True(len > 0);
    }
    [Fact]
    public async Task CreateCustomReport_ReturnsBadRequest_EndDateIsBeforeStart()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await controller.Create(
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
            end: DateOnly.FromDateTime(DateTime.Now.AddDays(-15)),
            reportName: "Test Report",
            CreatorUserID: 1,
            type: "Custom"
        );
        Assert.IsType<BadRequestObjectResult>(result); ;
    }

    [Fact]
    public async Task CreateCustomReport_ReturnsBadRequest_InvalidOfficeID()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await controller.Create(
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
            end: DateOnly.FromDateTime(DateTime.Now.AddDays(-5)),
            reportName: "Test Report",
            CreatorUserID: 1,
            OfficeID: 10000,
            type: "Custom"
        );
        Assert.IsType<BadRequestObjectResult>(result); ;
    }

    [Fact]
    public async Task CreateCustomReport_ReturnsBadRequest_InvalidCreatorUserID()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await controller.Create(
            start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
            end: DateOnly.FromDateTime(DateTime.Now.AddDays(-5)),
            reportName: "Test Report",
            CreatorUserID: 15000,
            type: "Custom"
        );
        Assert.IsType<BadRequestObjectResult>(result); ;

    }
    [Fact]
    public async Task CreateCustomReport_ReturnsIdAndLen_SeeHardwareOnly()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await controller.Create(
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
        // Access fields
        var id = returnedResponse.ReportID;
        var len = returnedResponse.ContentLength;

        Assert.True(!(id < 0));
        Assert.True(len > 0);
    }
    [Fact]
    public async Task CreateCustomReport_ReturnsIdAndLen_SeeSoftwareOnly()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await controller.Create(
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
        // Access fields
        var id = returnedResponse.ReportID;
        var len = returnedResponse.ContentLength;

        Assert.True(!(id < 0));
        Assert.True(len > 0);
    }

    [Fact]
    public async Task CreateCustomReport_ReturnsIdAndLen_SeeUsers()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await controller.Create(
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
        // Access fields
        var id = returnedResponse.ReportID;
        var len = returnedResponse.ContentLength;

        Assert.True(!(id < 0));
        Assert.True(len > 0);
    }
    [Fact]
    public async Task CreateCustomReport_ReturnsIdAndLen_SeeOffice()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await controller.Create(
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
        // Access fields
        var id = returnedResponse.ReportID;
        var len = returnedResponse.ContentLength;

        Assert.True(!(id < 0));
        Assert.True(len > 0);
    }
    [Fact]
    public async Task CreateCustomReport_ReturnsIdAndLen_SeeExpiration()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await controller.Create(
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
        // Access fields
        var id = returnedResponse.ReportID;
        var len = returnedResponse.ContentLength;

        Assert.True(!(id < 0));
        Assert.True(len > 0);
    }

    [Fact]
    public async Task CreateCustomReport_ReturnsIdAndLen_FilterByMaintenance()
    {
        var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

        var result = await controller.Create(
             start: DateOnly.FromDateTime(DateTime.Now.AddDays(-1)),
             reportName: "Test Report",
             CreatorUserID: 1,
             type: "Custom",

            customOptions: new CustomReportOptionsDto
            {
                filterByMaintenance = true
            }
         );
        // if (result is BadRequestObjectResult bad)
        // {
        //     // Print error details for debugging
        //     var json = JsonSerializer.Serialize(
        //         bad.Value,
        //         new JsonSerializerOptions { WriteIndented = true }
        //     );

        //     throw new Xunit.Sdk.XunitException($"❌ Controller returned BadRequest:\n{json}");
        // }
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedResponse = Assert.IsType<CreateReportResponseDto>(okResult.Value);
        // Access fields
        var id = returnedResponse.ReportID;
        var len = returnedResponse.ContentLength;

        Assert.True(!(id < 0));
        Assert.True(len > 0);
    }

}
