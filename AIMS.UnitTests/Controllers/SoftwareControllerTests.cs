using AIMS.Controllers;
using AIMS.Data;
using AIMS.Dtos.Software;
using AIMS.Models;
using AIMS.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AIMS.Controllers.Api;
using System.Threading.Tasks;
using AIMS.Queries;
using Xunit;

namespace AIMS.UnitTests.Controllers;

public class SoftwareControllerTests
{
    private readonly AimsDbContext _db;
    private readonly SoftwareController _controller;

    public SoftwareControllerTests()
    {
        var options = new DbContextOptionsBuilder<AimsDbContext>()
            .UseInMemoryDatabase($"AimsTestDb_Controller_{Guid.NewGuid()}")
            .Options;
        _db = new AimsDbContext(options);

        var service = new SoftwareUpdateService(_db);
        var query = new SoftwareQuery(_db);

        _controller = new SoftwareController(_db, query, service);
    }

    [Fact]
    public async Task EditSoftware_Should_Return_NotFound_When_Software_DoesNotExist()
    {
        var dto = new UpdateSoftwareDto { SoftwareName = "GhostApp" };

        var result = await _controller.EditSoftware(999, dto);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task EditSoftware_Should_Return_BadRequest_When_InvalidData()
    {
        var software = new Software { SoftwareName = "TestApp" };
        _db.SoftwareAssets.Add(software);
        _db.SaveChanges();

        var dto = new UpdateSoftwareDto { LicenseTotalSeats = -10 };

        var result = await _controller.EditSoftware(software.SoftwareID, dto);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.IsType<ValidationProblemDetails>(obj.Value);

        // ValidationProblem() sets no StatusCode
        Assert.Null(obj.StatusCode);

    }

    [Fact]
    public async Task EditSoftware_Should_Return_Ok_When_Valid()
    {
        var software = new Software { SoftwareName = "ValidApp" };
        _db.SoftwareAssets.Add(software);
        _db.SaveChanges();

        var dto = new UpdateSoftwareDto { SoftwareName = "UpdatedApp" };

        var result = await _controller.EditSoftware(software.SoftwareID, dto);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var updated = Assert.IsType<Software>(okResult.Value);
        Assert.Equal("UpdatedApp", updated.SoftwareName);
    }
}
