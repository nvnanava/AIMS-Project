using System;
using System.Collections.Generic;
using Moq; // For mocking WebHostLogic
using System.Threading.Tasks;
using AIMS.Controllers;
using AIMS.Data;
using AIMS.Models;
using AIMS.Queries;
using AIMS.ViewModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IO.Abstractions.TestingHelpers;
using Xunit;
using System.Text;
using Microsoft.EntityFrameworkCore.Query;
using Xunit.Abstractions;

namespace AIMS.UnitTests.Controllers
{
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
                db.SaveChanges();
            }

            var reportsQuery = new ReportsQuery(db);

            var mockFileSystem = new MockFileSystem();
            var mockWwwrootPath = "/mock/wwwroot";

            var webEnv = new Mock<IWebHostEnvironment>();
            webEnv.Setup(e => e.WebRootPath).Returns(mockWwwrootPath); // Set a mock path
            return new ReportsController(db, reportsQuery, webEnv.Object, mockFileSystem);
        }

        public List<Assignment> CreateSeedData()
        {
            return new List<Assignment>
            {
                new Assignment {
                    AssignmentID = 4,
                    UserID = 4,
                    User = new User { FullName = "Robin Williams" },
                    OfficeID = 1,
                    Office = new Office {OfficeName = "Bethesda"},
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
                    OfficeID = 2,
                    Office = new Office {OfficeName = "Activision"},
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
                    OfficeID = 3,
                    Office = new Office {OfficeName = "Blizzard"},
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
                    OfficeID = 4,
                    Office = new Office {OfficeName = "DICE"},
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
                    OfficeID = 3,
                    Office = new Office {OfficeName = "Blizzard"},
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
                    OfficeID = 4,
                    Office = new Office {OfficeName = "DICE"},
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
                    OfficeID = 2,
                    Office = new Office {OfficeName = "Activision"},
                    AssetKind =  AssetKind.Hardware,
                    HardwareID = 15000,
                    Hardware = new Hardware {HardwareID = 15000, AssetType = "Hardware", Status = "In Repair"},
                    SoftwareID = null,
                    AssignedAtUtc = DateTime.Now,
                    UnassignedAtUtc = null
                }
            };
        }

        public string[] csv2String(FileContentResult res)
        {
            var fileBytes = res.FileContents;
            string csvContent = Encoding.UTF8.GetString(fileBytes);
            string[] lines = csvContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines;
        }
        public string[] getCSVHeaders(string[] lines)
        {
            return lines[0].Split(',');
        }
        public bool checkIfEachLineContains(string[] lines, int headerIndx, string[] value)
        {
            if (lines == null || lines.Length == 0)
                return false;

            if (value is null)
                return false;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var split = line.Split(',');
                if (headerIndx >= split.Length)
                    return false; // line missing column

                var chunk = split[headerIndx].Trim();

                // must contain *at least one* allowed value
                bool containsAny = value.Any(v =>
                    chunk.Contains(v, StringComparison.OrdinalIgnoreCase));

                if (!containsAny)
                {
                    return false;
                }
            }

            return true;
        }

        [Fact]
        public async Task CreateAssignmentReport_ReturnsBadRequest_EndDateIsBeforeStart()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

            /**
ask<IActionResult> ReportsController.Create(DateOnly start, 
string reportName, int CreatorUserID, string? type, DateOnly? end, 
int? OfficeID, string? desc, CustomReportOptionsDto? customOptions, 
[CancellationToken ct = default])
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
        public async Task CreateAssignmentReport_ReturnsFileContentResult_CorrectInputs()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

            var result = await controller.Create(
                start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
                end: DateOnly.FromDateTime(DateTime.Now.AddDays(-5)),
                reportName: "Test Report",
                CreatorUserID: 1,
                type: "Assignment"
            ) as FileContentResult;
            Assert.IsType<FileContentResult>(result);
            Assert.Equal("text/csv", result.ContentType);

            string[] csvLines = csv2String(result);
            Assert.True(csvLines.Length > 0, "CSV content should not be empty.");

            string[] expectedHeaders = { "AssignmentID", "Assignee", "Assignee Office", "Asset Name", "Asset Type", "Comment" };
            string[] actualHeaders = getCSVHeaders(csvLines);
            Assert.Equal(expectedHeaders, actualHeaders);

            Assert.Single(csvLines[1..]);
        }
        [Fact]
        public async Task CreateAssignmentReport_ReturnsFileContentResult_CorrectInputsDateFilter()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

            var result = await controller.Create(
                start: DateOnly.FromDateTime(DateTime.Now.AddDays(-1)),
                reportName: "Test Report",
                CreatorUserID: 1,
                type: "Assignment"
            ) as FileContentResult;
            Assert.IsType<FileContentResult>(result);
            Assert.Equal("text/csv", result.ContentType);

            string[] csvLines = csv2String(result);
            Assert.True(csvLines.Length > 0, "CSV content should not be empty.");

            string[] expectedHeaders = { "AssignmentID", "Assignee", "Assignee Office", "Asset Name", "Asset Type", "Comment" };
            string[] actualHeaders = getCSVHeaders(csvLines);
            Assert.Equal(expectedHeaders, actualHeaders);
            Assert.Equal(2, csvLines[1..].Length);
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
        public async Task CreateOfficeReport_ReturnsFileContentResult_CorrectInputs()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

            var result = await controller.Create(
                start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
                end: DateOnly.FromDateTime(DateTime.Now.AddDays(-5)),
                reportName: "Test Report",
                CreatorUserID: 1,
                OfficeID: 1,
                type: "Office"
            ) as FileContentResult;
            Assert.IsType<FileContentResult>(result);
            Assert.Equal("text/csv", result.ContentType);

            string[] csvLines = csv2String(result);
            Assert.True(csvLines.Length > 0, "CSV content should not be empty.");

            string[] expectedHeaders = { "AssignmentID", "Assignee", "Assignee Office", "Asset Name", "Asset Type", "Comment" };
            string[] actualHeaders = getCSVHeaders(csvLines);
            Assert.Equal(expectedHeaders, actualHeaders);
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
        public async Task CreateCustomReport_ReturnsFileContentResult_SeeHardwareOnly()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

            var result = await controller.Create(
                 start: DateOnly.FromDateTime(DateTime.Now.AddDays(-1)),
                 reportName: "Test Report",
                 CreatorUserID: 1,
                 OfficeID: 1,
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
             ) as FileContentResult;
            Assert.IsType<FileContentResult>(result);
            Assert.Equal("text/csv", result.ContentType);

            string[] csvLines = csv2String(result);
            Assert.True(csvLines.Length > 0, "CSV content should not be empty.");

            string[] expectedHeaders = { "AssignmentID", "Asset Name", "Asset Type", "Comment" };
            string[] actualHeaders = getCSVHeaders(csvLines);
            Assert.Equal(expectedHeaders, actualHeaders);

            Assert.True(checkIfEachLineContains(csvLines[1..], 2, ["Hardware"]));
        }
        [Fact]
        public async Task CreateCustomReport_ReturnsFileContentResult_SeeSoftwareOnly()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

            var result = await controller.Create(
                 start: DateOnly.FromDateTime(DateTime.Now.AddDays(-1)),
                 reportName: "Test Report",
                 CreatorUserID: 1,
                 OfficeID: 1,
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
             ) as FileContentResult;
            Assert.IsType<FileContentResult>(result);
            Assert.Equal("text/csv", result.ContentType);

            string[] csvLines = csv2String(result);
            Assert.True(csvLines.Length > 0, "CSV content should not be empty.");

            string[] expectedHeaders = { "AssignmentID", "Asset Name", "Asset Type", "Comment" };
            string[] actualHeaders = getCSVHeaders(csvLines);
            Assert.Equal(expectedHeaders, actualHeaders);

            Assert.True(checkIfEachLineContains(csvLines[1..], 2, ["Software"]));
        }

        [Fact]
        public async Task CreateCustomReport_ReturnsFileContentResult_SeeUsers()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

            var result = await controller.Create(
                 start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
                 end: DateOnly.FromDateTime(DateTime.Now.AddDays(-5)),
                 reportName: "Test Report",
                 CreatorUserID: 1,
                 OfficeID: 1,
                 type: "Custom",

                customOptions: new CustomReportOptionsDto
                {
                    seeOffice = false,
                    seeUsers = true,
                }
             ) as FileContentResult;
            Assert.IsType<FileContentResult>(result);
            Assert.Equal("text/csv", result.ContentType);

            string[] csvLines = csv2String(result);
            Assert.True(csvLines.Length > 0, "CSV content should not be empty.");

            string[] expectedHeaders = { "AssignmentID", "Assignee", "Asset Name", "Asset Type", "Comment" };
            string[] actualHeaders = getCSVHeaders(csvLines);
            Assert.Equal(expectedHeaders, actualHeaders);
        }
        [Fact]
        public async Task CreateCustomReport_ReturnsFileContentResult_SeeOffice()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

            var result = await controller.Create(
                 start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
                 end: DateOnly.FromDateTime(DateTime.Now.AddDays(-5)),
                 reportName: "Test Report",
                 CreatorUserID: 1,
                 OfficeID: 1,
                 type: "Custom",

                customOptions: new CustomReportOptionsDto
                {
                    seeUsers = true,
                }
             ) as FileContentResult;
            Assert.IsType<FileContentResult>(result);
            Assert.Equal("text/csv", result.ContentType);

            string[] csvLines = csv2String(result);
            Assert.True(csvLines.Length > 0, "CSV content should not be empty.");

            string[] expectedHeaders = { "AssignmentID", "Assignee", "Assignee Office", "Asset Name", "Asset Type", "Comment" };
            string[] actualHeaders = getCSVHeaders(csvLines);
            Assert.Equal(expectedHeaders, actualHeaders);
        }
        [Fact]
        public async Task CreateCustomReport_ReturnsFileContentResult_SeeExpiration()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

            var result = await controller.Create(
                 start: DateOnly.FromDateTime(DateTime.Now.AddDays(-10)),
                 end: DateOnly.FromDateTime(DateTime.Now.AddDays(-5)),
                 reportName: "Test Report",
                 CreatorUserID: 1,
                 OfficeID: 1,
                 type: "Custom",

                customOptions: new CustomReportOptionsDto
                {
                    seeExpiration = true
                }
             ) as FileContentResult;
            Assert.IsType<FileContentResult>(result);
            Assert.Equal("text/csv", result.ContentType);

            string[] csvLines = csv2String(result);
            Assert.True(csvLines.Length > 0, "CSV content should not be empty.");

            string[] expectedHeaders = { "AssignmentID", "Assignee", "Assignee Office", "Asset Name", "Asset Type", "Comment", "Expiration" };
            string[] actualHeaders = getCSVHeaders(csvLines);
            Assert.Equal(expectedHeaders, actualHeaders);
        }

        [Fact]
        public async Task CreateCustomReport_ReturnsFileContentResult_FilterByMaintenance()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), CreateSeedData());

            var result = await controller.Create(
                 start: DateOnly.FromDateTime(DateTime.Now.AddDays(-1)),
                 reportName: "Test Report",
                 CreatorUserID: 1,
                 OfficeID: 1,
                 type: "Custom",

                customOptions: new CustomReportOptionsDto
                {
                    filterByMaintenance = true
                }
             ) as FileContentResult;
            Assert.IsType<FileContentResult>(result);
            Assert.Equal("text/csv", result.ContentType);

            string[] csvLines = csv2String(result);
            Assert.True(csvLines.Length > 0, "CSV content should not be empty.");

            string[] expectedHeaders = { "AssignmentID", "Assignee", "Assignee Office", "Asset Name", "Asset Type", "Comment", "Status" };
            string[] actualHeaders = getCSVHeaders(csvLines);
            Assert.Equal(expectedHeaders, actualHeaders);

            Assert.True(checkIfEachLineContains(csvLines[1..], 6, ["In Repair", "Marked for Survey"]));
        }

    }
}