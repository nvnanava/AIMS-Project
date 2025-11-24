using AIMS.Controllers.Api;
using AIMS.Data;
using AIMS.Dtos.Assets;
using AIMS.Models;
using AIMS.Queries;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AIMS.Services;

namespace AIMS.UnitTests
{
    public class ArchiveEndpointTests
    {
        private AimsDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<AimsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new AimsDbContext(options);
        }

        private HardwareController CreateController(AimsDbContext db)
        {
            // Provide both dependencies to match HardwareController constructor
            return new HardwareController(db, new HardwareQuery(db), new HardwareAssetService(db));
        }

        // ------------------------------------------------------
        //  Archive should mark archived + unassign
        // ------------------------------------------------------
        [Fact]
        public async Task ArchiveHardware_SetsIsArchivedTrue_AndUnassigns()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<AimsDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            await using var db = new AimsDbContext(options);

            // Seed a hardware record
            var hardware = new Hardware
            {
                AssetTag = "PC-001",
                AssetName = "Dell Optiplex",
                AssetType = "Desktop",
                Status = "Available",
                Manufacturer = "Dell",
                Model = "Optiplex 7050",
                SerialNumber = "ABC123",
                PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
                WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1))
            };

            db.HardwareAssets.Add(hardware);
            await db.SaveChangesAsync();
            Console.WriteLine($"Seeded HardwareID = {hardware.HardwareID}");
            Console.WriteLine($"Count before archive = {await db.HardwareAssets.CountAsync()}");
            Assert.True(hardware.HardwareID > 0);

            // Seed an active assignment (optional)
            db.Assignments.Add(new Assignment
            {
                AssetKind = AssetKind.Hardware,
                HardwareID = hardware.HardwareID,
                UserID = 1,
                AssignedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            var controller = CreateController(db);

            // Act
            var result = await controller.ArchiveHardware(hardware.HardwareID);

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result);
            var dto = Assert.IsType<AssetRowDto>(ok.Value);
            Assert.True(dto.IsArchived);
            Assert.Equal("Archived", dto.Status);

            var reloaded = await db.HardwareAssets.IgnoreQueryFilters().SingleAsync();
            Assert.True(reloaded.IsArchived);
            Assert.Equal("Archived", reloaded.Status);

            var assignment = await db.Assignments.SingleOrDefaultAsync();
            Assert.NotNull(assignment);
            Assert.NotNull(assignment!.UnassignedAtUtc);
        }

        // ------------------------------------------------------
        //  Unarchive should flip IsArchived = false
        // ------------------------------------------------------
        [Fact]
        public async Task UnarchiveHardware_SetsIsArchivedFalse()
        {
            var db = CreateContext();
            db.HardwareAssets.Add(new Hardware
            {
                HardwareID = 2,
                AssetName = "Monitor",
                AssetTag = "MN-002",
                AssetType = "Display",
                IsArchived = true,
                Status = "Archived"
            });
            await db.SaveChangesAsync();

            var controller = CreateController(db);

            var ok = Assert.IsType<OkObjectResult>(await controller.UnarchiveHardware(2));
            var dto = Assert.IsType<AssetRowDto>(ok.Value);
            Assert.False(dto.IsArchived);
            Assert.Equal("Available", dto.Status);

            var reloaded = await db.HardwareAssets.FirstAsync();
            Assert.False(reloaded.IsArchived);
            Assert.Equal("Available", reloaded.Status);
        }

        // ------------------------------------------------------
        //  Archive invalid ID → 404
        // ------------------------------------------------------
        [Fact]
        public async Task ArchiveHardware_ReturnsNotFound_ForInvalidId()
        {
            var db = CreateContext();
            var controller = CreateController(db);

            var result = await controller.ArchiveHardware(999);

            Assert.IsType<NotFoundResult>(result);
        }

        // ------------------------------------------------------
        //  Unarchive invalid ID → 404
        // ------------------------------------------------------
        [Fact]
        public async Task UnarchiveHardware_ReturnsNotFound_ForInvalidId()
        {
            var db = CreateContext();
            var controller = CreateController(db);

            var result = await controller.UnarchiveHardware(999);

            Assert.IsType<NotFoundResult>(result);
        }

        // ------------------------------------------------------
        //  Archive twice (idempotent)
        // ------------------------------------------------------
        [Fact]
        public async Task ArchiveHardware_Idempotent_WhenAlreadyArchived()
        {
            var db = CreateContext();
            db.HardwareAssets.Add(new Hardware
            {
                HardwareID = 3,
                AssetName = "Docking Station",
                AssetTag = "DS-300",
                AssetType = "Accessory",
                IsArchived = true,
                Status = "Archived"
            });
            await db.SaveChangesAsync();

            var controller = CreateController(db);

            var result = await controller.ArchiveHardware(3) as OkObjectResult;

            var ok = Assert.IsType<OkObjectResult>(await controller.ArchiveHardware(3));
            var dto = Assert.IsType<AssetRowDto>(ok.Value);
            Assert.True(dto.IsArchived);
        }

        // ------------------------------------------------------
        //  Unarchive twice (idempotent)
        // ------------------------------------------------------
        [Fact]
        public async Task UnarchiveHardware_Idempotent_WhenAlreadyActive()
        {
            var db = CreateContext();
            db.HardwareAssets.Add(new Hardware
            {
                HardwareID = 4,
                AssetName = "Keyboard",
                AssetTag = "KB-400",
                AssetType = "Peripheral",
                IsArchived = false,
                Status = "Available"
            });
            await db.SaveChangesAsync();

            var controller = CreateController(db);

            var ok = Assert.IsType<OkObjectResult>(await controller.UnarchiveHardware(4));
            var dto = Assert.IsType<AssetRowDto>(ok.Value);
            Assert.False(dto.IsArchived);
            Assert.Equal("Available", dto.Status);
        }
        // ------------------------------------------------------
        //  Archive should work when no assignment exists
        // ------------------------------------------------------
        [Fact]
        public async Task ArchiveHardware_Works_WhenNoAssignmentExists()
        {
            var db = CreateContext();
            var hw = new Hardware { AssetTag = "NOASSIGN", AssetType = "Desktop", Status = "Available" };
            db.HardwareAssets.Add(hw);
            await db.SaveChangesAsync();

            var controller = CreateController(db);

            var ok = Assert.IsType<OkObjectResult>(await controller.ArchiveHardware(hw.HardwareID));
            var dto = Assert.IsType<AssetRowDto>(ok.Value);
            Assert.True(dto.IsArchived);
        }
        // ------------------------------------------------------
        //  Returns not found when id is long random number not in db
        // ------------------------------------------------------
        [Fact]
        public async Task ArchiveHardware_ReturnsNotFound_ForLargeId()
        {
            var db = CreateContext();
            var controller = CreateController(db);
            var result = await controller.ArchiveHardware(int.MaxValue);
            Assert.IsType<NotFoundResult>(result);
        }

    }
}
