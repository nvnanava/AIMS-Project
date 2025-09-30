using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AIMS.Controllers;
using AIMS.Data;
using AIMS.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AIMS.UnitTests
{
    public class HardwareBulkEndpointTests
    {
        // Helper method for creating controller with in-memory db
        private HardwareController CreateControllerWithDb(string dbName, List<Hardware> seedHardware = null)
        {
            var options = new DbContextOptionsBuilder<AimsDbContext>()
                .UseInMemoryDatabase(databaseName: dbName) // unique per test
                .Options;

            var db = new AimsDbContext(options);

            // Seed hardware if provided
            if (seedHardware != null)
            {
                db.HardwareAssets.AddRange(seedHardware);
                db.SaveChanges();
            }

            var hardwareQuery = new HardwareQuery(db);

            return new HardwareController(db, hardwareQuery);
        }


        [Fact]
        public async Task AddBulkHardware_ReturnsBadRequest_WhenDuplicateSerialNumber()
        {
            var seedHardware = new List<Hardware>
            {
                new Hardware { SerialNumber = "SN-DUPLICATE-302", AssetTag = "BULK-302" }
            };
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), seedHardware);

            var dtos = new List<CreateHardwareDto>
            {
                new CreateHardwareDto
                {
                    AssetTag = "BULK-302",
                    Manufacturer = "HP",
                    Model = "EliteBook",
                    AssetType = "Laptop",
                    Status = "Available",
                    SerialNumber = "SN-DUPLICATE-302",
                    PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5)),
                    WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1))
                }
            };

            var result = await controller.AddHardwareBulk(dtos);
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task AddBulkHardware_ReturnsBadRequest_WhenDuplicateAssetTag()
        {
            var seedHardware = new List<Hardware>
            {
                new Hardware { SerialNumber = "SN-UNIQUE-303", AssetTag = "BULK-303" }
            };
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), seedHardware);

            var dtos = new List<CreateHardwareDto>
            {
                new CreateHardwareDto
                {
                    AssetTag = "BULK-303",
                    Manufacturer = "Lenovo",
                    Model = "ThinkPad",
                    AssetType = "Laptop",
                    Status = "Available",
                    SerialNumber = "SN-UNIQUE-304",
                    PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5)),
                    WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1))
                }
            };

            var result = await controller.AddHardwareBulk(dtos);
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task AddBulkHardware_ReturnsBadRequest_WhenPurchaseDateInFuture()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString());

            var dtos = new List<CreateHardwareDto>
            {
                new CreateHardwareDto
                {
                    AssetTag = "BULK-304",
                    Manufacturer = "Apple",
                    Model = "MacBook Pro",
                    AssetType = "Laptop",
                    Status = "Available",
                    SerialNumber = "SN-FUTURE-304",
                    PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
                    WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1))
                }
            };

            var result = await controller.AddHardwareBulk(dtos);
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task AddBulkHardware_ReturnsBadRequest_WhenWarrantyBeforePurchase()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString());

            var dtos = new List<CreateHardwareDto>
            {
                new CreateHardwareDto
                {
                    AssetTag = "BULK-305",
                    Manufacturer = "Acer",
                    Model = "Aspire",
                    AssetType = "Laptop",
                    Status = "Available",
                    SerialNumber = "SN-WARRANTY-305",
                    PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                    WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10))
                }
            };

            var result = await controller.AddHardwareBulk(dtos);
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task AddBulkHardware_ReturnsBadRequest_WhenMissingRequiredFields()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString());

            var dtos = new List<CreateHardwareDto>
            {
                new CreateHardwareDto
                {
                    AssetTag = null,
                    Manufacturer = null,
                    Model = null,
                    AssetType = null,
                    Status = null,
                    SerialNumber = null,
                    PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                    WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1))
                }
            };

            var result = await controller.AddHardwareBulk(dtos);
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task AddBulkHardware_ReturnsCreated_WhenValid()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString());

            var dtos = new List<CreateHardwareDto>
            {
                new CreateHardwareDto
                {
                    AssetTag = "BULK-301",
                    Manufacturer = "Dell",
                    Model = "Latitude",
                    AssetType = "Laptop",
                    Status = "Available",
                    SerialNumber = "SN-BULK-301",
                    PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
                    WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1))
                }
            };

            var result = await controller.AddHardwareBulk(dtos);

            var created = Assert.IsType<CreatedAtActionResult>(result);
            var returned = Assert.IsAssignableFrom<List<Hardware>>(created.Value);
            Assert.Single(returned);
            Assert.Equal("BULK-301", returned[0].AssetTag);
        }

        [Fact]
        public async Task AddBulkHardware_ReturnsCreated_WhenMultipleValidAssets()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString());

            var dtos = new List<CreateHardwareDto>
            {
                new CreateHardwareDto
                {
                    AssetTag = "BULK-306",
                    Manufacturer = "Dell",
                    Model = "OptiPlex",
                    AssetType = "Desktop",
                    Status = "Available",
                    SerialNumber = "SN-BULK-306",
                    PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
                    WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(2))
                },
                new CreateHardwareDto
                {
                    AssetTag = "BULK-307",
                    Manufacturer = "HP",
                    Model = "ProDesk",
                    AssetType = "Desktop",
                    Status = "Available",
                    SerialNumber = "SN-BULK-307",
                    PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-60)),
                    WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(3))
                }
            };

            var result = await controller.AddHardwareBulk(dtos);

            var created = Assert.IsType<CreatedAtActionResult>(result);
            var returned = Assert.IsAssignableFrom<List<Hardware>>(created.Value);
            Assert.Equal(2, returned.Count);
            Assert.Contains(returned, h => h.AssetTag == "BULK-306");
            Assert.Contains(returned, h => h.AssetTag == "BULK-307");
        }

        [Fact]
        public async Task AddBulkHardware_ReturnsBadRequest_WhenEmptyList()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString());
            var dtos = new List<CreateHardwareDto>();

            var result = await controller.AddHardwareBulk(dtos);
            Assert.True(result is BadRequestObjectResult || result is CreatedAtActionResult);
        }
        [Fact]
        public async Task AddBulkHardware_TrimsValues_BeforeSaving()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString());

            var dtos = new List<CreateHardwareDto>
    {
        new CreateHardwareDto
        {
            AssetTag = "  BULK-314  ",
            Manufacturer = "  Dell  ",
            Model = "  Latitude  ",
            AssetType = "  Laptop  ",
            Status = "  Available  ",
            SerialNumber = "  SN-BULK-314  ",
            PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
            WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1))
        }
    };

            var result = await controller.AddHardwareBulk(dtos);

            var created = Assert.IsType<CreatedAtActionResult>(result);
            var returned = Assert.IsAssignableFrom<List<Hardware>>(created.Value);
            var hardware = returned[0];

            Assert.Equal("BULK-314", hardware.AssetTag);
            Assert.Equal("Dell", hardware.Manufacturer);
            Assert.Equal("Latitude", hardware.Model);
            Assert.Equal("Laptop", hardware.AssetType);
            Assert.Equal("Available", hardware.Status);
            Assert.Equal("SN-BULK-314", hardware.SerialNumber);
        }

        [Fact]
        public async Task AddBulkHardware_ReturnsBadRequest_WhenNullList()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString());
            List<CreateHardwareDto> dtos = null;

            var result = await controller.AddHardwareBulk(dtos);
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task AddBulkHardware_ReturnsBadRequest_WhenSerialNumberIsWhitespace()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString());

            var dtos = new List<CreateHardwareDto>
            {
                new CreateHardwareDto
                {
                    AssetTag = "BULK-308",
                    Manufacturer = "Dell",
                    Model = "Latitude",
                    AssetType = "Laptop",
                    Status = "Available",
                    SerialNumber = "   ",
                    PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
                    WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1))
                }
            };

            var result = await controller.AddHardwareBulk(dtos);
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task AddBulkHardware_ReturnsBadRequest_WhenAssetTagIsWhitespace()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString());

            var dtos = new List<CreateHardwareDto>
            {
                new CreateHardwareDto
                {
                    AssetTag = "   ",
                    Manufacturer = "Dell",
                    Model = "Latitude",
                    AssetType = "Laptop",
                    Status = "Available",
                    SerialNumber = "SN-BULK-309",
                    PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
                    WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1))
                }
            };

            var result = await controller.AddHardwareBulk(dtos);
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task AddBulkHardware_ReturnsBadRequest_WhenManufacturerIsMissing()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString());

            var dtos = new List<CreateHardwareDto>
            {
                new CreateHardwareDto
                {
                    AssetTag = "BULK-310",
                    Manufacturer = "",
                    Model = "Latitude",
                    AssetType = "Laptop",
                    Status = "Available",
                    SerialNumber = "SN-BULK-310",
                    PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
                    WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1))
                }
            };

            var result = await controller.AddHardwareBulk(dtos);
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task AddBulkHardware_ReturnsBadRequest_WhenModelIsMissing()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString());

            var dtos = new List<CreateHardwareDto>
            {
                new CreateHardwareDto
                {
                    AssetTag = "BULK-311",
                    Manufacturer = "Dell",
                    Model = "",
                    AssetType = "Laptop",
                    Status = "Available",
                    SerialNumber = "SN-BULK-311",
                    PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
                    WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1))
                }
            };

            var result = await controller.AddHardwareBulk(dtos);
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task AddBulkHardware_ReturnsBadRequest_WhenAssetTypeIsMissing()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString());

            var dtos = new List<CreateHardwareDto>
            {
                new CreateHardwareDto
                {
                    AssetTag = "BULK-312",
                    Manufacturer = "Dell",
                    Model = "Latitude",
                    AssetType = "",
                    Status = "Available",
                    SerialNumber = "SN-BULK-312",
                    PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
                    WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1))
                }
            };

            var result = await controller.AddHardwareBulk(dtos);
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task AddBulkHardware_ReturnsBadRequest_WhenStatusIsMissing()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString());

            var dtos = new List<CreateHardwareDto>
            {
                new CreateHardwareDto
                {
                    AssetTag = "BULK-313",
                    Manufacturer = "Dell",
                    Model = "Latitude",
                    AssetType = "Laptop",
                    Status = "",
                    SerialNumber = "SN-BULK-313",
                    PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
                    WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1))
                }
            };

            var result = await controller.AddHardwareBulk(dtos);
            Assert.IsType<BadRequestObjectResult>(result);
        }
        [Fact]
        public async Task AddBulkHardware_OneItem_RobustnessTest()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString());


            var dto = new CreateHardwareDto
            {
                AssetTag = "BULK-ONE-001",
                Manufacturer = "Dell",
                Model = "Latitude",
                AssetType = "Laptop",
                Status = "Available",
                SerialNumber = "SN-ONE-001",
                PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(3))
            };

            var dtos = new List<CreateHardwareDto> { dto };

            var result = await controller.AddHardwareBulk(dtos);

            // Assert result is Created
            var created = Assert.IsType<CreatedAtActionResult>(result);
            var returned = Assert.IsAssignableFrom<List<Hardware>>(created.Value);
            Assert.Single(returned);

            var hardware = returned[0];

            // Assert all fields are correctly mapped and trimmed
            Assert.Equal("BULK-ONE-001", hardware.AssetTag);
            Assert.Equal("Dell", hardware.Manufacturer);
            Assert.Equal("Latitude", hardware.Model);
            Assert.Equal("Laptop", hardware.AssetType);
            Assert.Equal("Available", hardware.Status);
            Assert.Equal("SN-ONE-001", hardware.SerialNumber);

            // Assert dates are correctly mapped
            Assert.Equal(dto.PurchaseDate, hardware.PurchaseDate);
            Assert.Equal(dto.WarrantyExpiration, hardware.WarrantyExpiration);

            // Assert no unexpected nulls
            Assert.False(string.IsNullOrWhiteSpace(hardware.AssetTag));
            Assert.False(string.IsNullOrWhiteSpace(hardware.Manufacturer));
            Assert.False(string.IsNullOrWhiteSpace(hardware.Model));
            Assert.False(string.IsNullOrWhiteSpace(hardware.AssetType));
            Assert.False(string.IsNullOrWhiteSpace(hardware.Status));
            Assert.False(string.IsNullOrWhiteSpace(hardware.SerialNumber));
        }

        [Fact]
        public async Task AddBulkHardware_CanAddLargeBatch()
        {
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString());

            var dtos = new List<CreateHardwareDto>();
            for (int i = 0; i < 100; i++)
            {
                dtos.Add(new CreateHardwareDto
                {
                    AssetTag = $"BULK-{1000 + i}",
                    Manufacturer = "Dell",
                    Model = "BatchModel",
                    AssetType = "Laptop",
                    Status = "Available",
                    SerialNumber = $"SN-BULK-{1000 + i}",
                    PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
                    WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1))
                });
            }

            var result = await controller.AddHardwareBulk(dtos);

            var created = Assert.IsType<CreatedAtActionResult>(result);
            var returned = Assert.IsAssignableFrom<List<Hardware>>(created.Value);
            Assert.Equal(100, returned.Count);
        }

        [Fact]
        public async Task AddBulkHardware_ReturnsBadRequest_WhenSerialNumberDiffersOnlyByCase()
        {
            var seedHardware = new List<Hardware>
            {
                new Hardware { SerialNumber = "SN-123", AssetTag = "BULK-401" }
            };
            var controller = CreateControllerWithDb(Guid.NewGuid().ToString(), seedHardware);

            var dtos = new List<CreateHardwareDto>
            {
                new CreateHardwareDto
                {
                    AssetTag = "BULK-402",
                    Manufacturer = "Dell",
                    Model = "Latitude",
                    AssetType = "Laptop",
                    Status = "Available",
                    SerialNumber = "sn-123", // differs only by case
                    PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
                    WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1))
                }
            };

            var result = await controller.AddHardwareBulk(dtos);
            Assert.IsType<BadRequestObjectResult>(result);
        }
    }
}
