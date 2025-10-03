using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIMS.Controllers;
using AIMS.Data;
using AIMS.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AIMS.UnitTests
{
    public class SoftwareBulkEndpointTests
    {
        // Helper to create a SoftwareController with in-memory DB
        private SoftwareController CreateControllerWithDb(string dbName, List<Software> seedSoftware = null)
        {
            var options = new DbContextOptionsBuilder<AimsDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;

            var db = new AimsDbContext(options);

            if (seedSoftware != null)
            {
                db.SoftwareAssets.AddRange(seedSoftware);
                db.SaveChanges();
            }

            var softwareQuery = new SoftwareQuery(db);
            return new SoftwareController(db, softwareQuery);
        }

        [Fact]
        public async Task AddBulkSoftware_ReturnsBadRequest_WhenNullList()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString());
            List<CreateSoftwareDto> dtos = null;

            var result = await controller.AddBulkSoftware(dtos);
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task AddBulkSoftware_ReturnsBadRequest_WhenEmptyList()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString());
            var dtos = new List<CreateSoftwareDto>();

            var result = await controller.AddBulkSoftware(dtos);
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task AddBulkSoftware_ReturnsBadRequest_WhenDuplicateLicenseKey()
        {
            var seed = new List<Software>
            {
                new Software { SoftwareLicenseKey = "DUP-123", SoftwareName = "Word" }
            };
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), seed);

            var dtos = new List<CreateSoftwareDto>
            {
                new CreateSoftwareDto
                {
                    SoftwareName = "Word",
                    SoftwareType = "Productivity",
                    SoftwareVersion = "2025",
                    SoftwareLicenseKey = "DUP-123",
                    SoftwareCost = 100,
                    Comment = "Duplicate test"
                }
            };

            var result = await controller.AddBulkSoftware(dtos);
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task AddBulkSoftware_ReturnsBadRequest_WhenNegativeCost()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString());

            var dtos = new List<CreateSoftwareDto>
            {
                new CreateSoftwareDto
                {
                    SoftwareName = "Photoshop",
                    SoftwareType = "Design",
                    SoftwareVersion = "2024",
                    SoftwareLicenseKey = "PH-001",
                    SoftwareCost = -50,
                    Comment = "Invalid cost"
                }
            };

            var result = await controller.AddBulkSoftware(dtos);
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task AddBulkSoftware_ReturnsBadRequest_WhenLicenseExpired()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString());

            var dtos = new List<CreateSoftwareDto>
            {
                new CreateSoftwareDto
                {
                    SoftwareName = "Excel",
                    SoftwareType = "Spreadsheet",
                    SoftwareVersion = "2023",
                    SoftwareLicenseKey = "EX-001",
                    SoftwareCost = 120,
                    SoftwareLicenseExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                    Comment = "Expired license"
                }
            };

            var result = await controller.AddBulkSoftware(dtos);
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task AddBulkSoftware_ReturnsCreated_WhenValid()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString());

            var dtos = new List<CreateSoftwareDto>
            {
                new CreateSoftwareDto
                {
                    SoftwareName = "Visual Studio",
                    SoftwareType = "IDE",
                    SoftwareVersion = "2025",
                    SoftwareLicenseKey = "VS-2025",
                    SoftwareCost = 0,
                    Comment = "Free community edition"
                }
            };

            var result = await controller.AddBulkSoftware(dtos);

            var created = Assert.IsType<CreatedAtActionResult>(result);
            var returned = Assert.IsAssignableFrom<List<Software>>(created.Value);
            Assert.Single(returned);
            Assert.Equal("Visual Studio", returned[0].SoftwareName);
            Assert.Equal("VS-2025", returned[0].SoftwareLicenseKey);
        }

        [Fact]
        public async Task AddBulkSoftware_CanAddMultipleValidAssets()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString());

            var dtos = new List<CreateSoftwareDto>
            {
                new CreateSoftwareDto
                {
                    SoftwareName = "Slack",
                    SoftwareType = "Communication",
                    SoftwareVersion = "1.0",
                    SoftwareLicenseKey = "SLACK-001",
                    SoftwareCost = 10,
                    Comment = "Team tool"
                },
                new CreateSoftwareDto
                {
                    SoftwareName = "Teams",
                    SoftwareType = "Communication",
                    SoftwareVersion = "2.0",
                    SoftwareLicenseKey = "TEAMS-001",
                    SoftwareCost = 20,
                    Comment = "Enterprise tool"
                }
            };

            var result = await controller.AddBulkSoftware(dtos);

            var created = Assert.IsType<CreatedAtActionResult>(result);
            var returned = Assert.IsAssignableFrom<List<Software>>(created.Value);
            Assert.Equal(2, returned.Count);
            Assert.Contains(returned, s => s.SoftwareName == "Slack");
            Assert.Contains(returned, s => s.SoftwareName == "Teams");
        }
    }
}
