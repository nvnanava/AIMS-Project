using System.Threading;
using System.Threading.Tasks;
using AIMS.Data;
using AIMS.Services;
using AIMS.Dtos.Hardware;
using AIMS.Controllers.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AIMS.UnitTests.Controllers;

public class HardwareControllerEditTests
{
    private static AimsDbContext NewDb()
    {
        var opt = new DbContextOptionsBuilder<AimsDbContext>()
            .UseInMemoryDatabase("edit_hw_" + System.Guid.NewGuid())
            .Options;
        return new AimsDbContext(opt);
    }

    [Fact]
    public async Task EditHardware_NotFound_Returns404()
    {
        using var db = NewDb();
        var controller = new HardwareController(db, new AIMS.Queries.HardwareQuery(db), new HardwareAssetService(db));
        var result = await controller.EditHardware(999, new UpdateHardwareDto(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task EditHardware_ValidationFailure_ReturnsBadRequest()
    {
        using var db = NewDb();
        db.HardwareAssets.Add(new AIMS.Models.Hardware { HardwareID = 1, AssetTag = "A", SerialNumber = "S", PurchaseDate = DateOnly.FromDateTime(System.DateTime.UtcNow), WarrantyExpiration = DateOnly.FromDateTime(System.DateTime.UtcNow.AddDays(1)) });
        await db.SaveChangesAsync();

        var controller = new HardwareController(db, new AIMS.Queries.HardwareQuery(db), new HardwareAssetService(db));
        // Force duplicate by adding same tag in dto
        var result = await controller.EditHardware(1, new UpdateHardwareDto { AssetTag = "A", PurchaseDate = DateOnly.FromDateTime(System.DateTime.UtcNow.AddDays(5)) }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task EditHardware_Success_ReturnsOk()
    {
        using var db = NewDb();
        db.HardwareAssets.Add(new AIMS.Models.Hardware { HardwareID = 5, AssetTag = "X5", SerialNumber = "S5", PurchaseDate = DateOnly.FromDateTime(System.DateTime.UtcNow), WarrantyExpiration = DateOnly.FromDateTime(System.DateTime.UtcNow.AddDays(5)), AssetName = "Orig" });
        await db.SaveChangesAsync();

        var controller = new HardwareController(db, new AIMS.Queries.HardwareQuery(db), new HardwareAssetService(db));
        var dto = new UpdateHardwareDto { AssetName = "Updated" };

        var result = await controller.EditHardware(5, dto, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var updated = Assert.IsAssignableFrom<AIMS.Models.Hardware>(ok.Value);
        Assert.Equal("Updated", updated.AssetName);
    }
}