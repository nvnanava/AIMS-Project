using System;
using System.Threading;
using System.Threading.Tasks;
using AIMS.Data;
using AIMS.Dtos.Hardware;
using AIMS.Models;
using AIMS.Services;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AIMS.UnitTests.Services
{
    public class HardwareServiceTests
    {
        private static AimsDbContext NewDb()
        {
            var opt = new DbContextOptionsBuilder<AimsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new AimsDbContext(opt);
        }

        //reject when list is null or empty
        [Fact]
        public async Task NullOrEmptyDto_ReturnsBadRequest()
        {
            var db = NewDb();
            var svc = new HardwareAssetService(db);
            var req = new BulkHardwareRequest { Dtos = new() };
            var output = await Assert.ThrowsAsync<ArgumentException>(() => svc.AddHardwareBulkAsync(req, CancellationToken.None));
            Console.WriteLine(output.Message);
        }

        [Fact]
        public async Task ValidateEditAsync_DuplicateTag_ReturnsBadRequest()
        {
            var db = NewDb();
            var existing = new Hardware
            {
                HardwareID = 1,
                AssetTag = "TAG1",
                SerialNumber = "SN1",
                PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow),
                WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10))
            };
            db.HardwareAssets.Add(existing);
            db.HardwareAssets.Add(new Hardware
            {
                HardwareID = 2,
                AssetTag = "TAG2",
                SerialNumber = "SN2",
                PurchaseDate = existing.PurchaseDate,
                WarrantyExpiration = existing.WarrantyExpiration
            });
            await db.SaveChangesAsync();

            var svc = new HardwareAssetService(db);
            var target = await db.HardwareAssets.FindAsync(2);
            Assert.NotNull(target);

            var dto = new UpdateHardwareDto { AssetTag = "TAG1" };

            var errors = await svc.ValidateEditAsync(target!, dto, 2, CancellationToken.None);

            Assert.NotEmpty(errors);
            Assert.Contains("A hardware asset with this asset tag already exists.", errors);
        }

        [Fact]
        public async Task ValidateEditAsync_DuplicateSerial_ReturnsBadRequest()
        {
            var db = NewDb();
            db.HardwareAssets.Add(new Hardware
            {
                HardwareID = 1,
                AssetTag = "A1",
                SerialNumber = "SN-DUP",
                PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow),
                WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5))
            });
            db.HardwareAssets.Add(new Hardware
            {
                HardwareID = 2,
                AssetTag = "A2",
                SerialNumber = "SN2",
                PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow),
                WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(6))
            });
            await db.SaveChangesAsync();

            var svc = new HardwareAssetService(db);
            var hw = await db.HardwareAssets.FindAsync(2);
            Assert.NotNull(hw);

            var dto = new UpdateHardwareDto
            {
                SerialNumber = "SN-DUP" // create duplicate
            };
            var errors = await svc.ValidateEditAsync(hw!, dto, 2, CancellationToken.None);

            Assert.NotEmpty(errors);
            Assert.Contains("A hardware asset with this serial number already exists.", errors);
        }

        [Fact]
        public async Task ValidateEditAsync_FuturePurchaseDate_ReturnsBadRequest()
        {
            var db = NewDb();
            db.HardwareAssets.Add(new Hardware
            {
                HardwareID = 5,
                AssetTag = "Xtest",
                SerialNumber = "Ytest",
                PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow),
                WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2))
            });
            await db.SaveChangesAsync();

            var svc = new HardwareAssetService(db);
            var hw = await db.HardwareAssets.FindAsync(5);
            Assert.NotNull(hw);

            var dto = new UpdateHardwareDto
            {
                PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3))
            };

            var errors = await svc.ValidateEditAsync(hw!, dto, 5, CancellationToken.None);

            Assert.NotEmpty(errors);
            Assert.Contains("Purchase date cannot be in the future.", errors);
        }

        [Fact]
        public async Task ValidateEditAsync_WarrantyBeforePurchase_ReturnsBadRequest()
        {
            var db = NewDb();

            db.HardwareAssets.Add(new Hardware
            {
                HardwareID = 7,
                AssetTag = "T",
                SerialNumber = "S",
                PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow),
                WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10))
            });

            await db.SaveChangesAsync();

            var svc = new HardwareAssetService(db);
            var hw = await db.HardwareAssets.FindAsync(7);
            Assert.NotNull(hw);

            // Warranty expiration BEFORE purchase date → invalid
            var dto = new UpdateHardwareDto
            {
                WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1))
            };

            var errors = await svc.ValidateEditAsync(hw!, dto, 7, CancellationToken.None);

            Assert.NotEmpty(errors);
            Assert.Contains("Warranty expiration cannot be before purchase date.", errors);
        }

        [Fact]
        public async Task ValidateEditAsync_Valid_ReturnsNull()
        {
            var db = NewDb();
            // existing hardware
            db.HardwareAssets.Add(new Hardware
            {
                HardwareID = 9,
                AssetTag = "T9",
                SerialNumber = "S9",
                PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow),
                WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5))
            });
            await db.SaveChangesAsync();

            var svc = new HardwareAssetService(db);
            var hw = await db.HardwareAssets.FindAsync(9);
            Assert.NotNull(hw);

            var dto = new UpdateHardwareDto
            {
                AssetName = "NewName"
            };
            var errors = await svc.ValidateEditAsync(hw!, dto, 9, CancellationToken.None);
            Assert.Empty(errors);
        }

        //Normalize DTOs?
        [Fact]
        public async Task NormalizeDto_TrimsFields()
        {
            var db = NewDb();
            var svc = new HardwareAssetService(db);
            var req = new BulkHardwareRequest
            {
                Dtos = new()
                {
                    new CreateHardwareDto
                    {
                        AssetTag = " TAG123 ",
                        Manufacturer = " Dell ",
                        Model = " XPS ",
                        SerialNumber = " SN123 ",
                        AssetType = " Laptop ",
                        Status = " Active ",
                        Comment = "  test comment "
                    }
                }
            };
            var result = await svc.AddHardwareBulkAsync(req);
            var row = result.Single();
            Assert.Equal("TAG123", row.AssetTag);
            Assert.Equal("Dell", row.Manufacturer);
            Assert.Equal("XPS", row.Model);
            Assert.Equal("SN123", row.SerialNumber);
            Assert.Equal("Laptop", row.AssetType);
            Assert.Equal("Active", row.Status);
            Assert.Equal("test comment", row.Comment);
        }

        [Fact]
        public async Task ValidateRows_MissingAssetTag_ThrowsException()
        {
            var db = NewDb();
            var svc = new HardwareAssetService(db);
            var dto = new CreateHardwareDto
            {
                AssetName = "Dell XPS",
                AssetType = "Laptop",
                Status = "Active",
                Manufacturer = "Dell",
                Model = "XPS 13",
                SerialNumber = "SN123",
                PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
                WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1))
            };
            dto.AssetTag = ""; // missing field
            var req = new BulkHardwareRequest { Dtos = new() { dto } };
            var ex = await Assert.ThrowsAsync<Exception>(() => svc.AddHardwareBulkAsync(req));
            Assert.Equal("All fields required.", ex.Message);
        }

        [Fact]
        public async Task ValidateRows_AssetTagTooLong_ThrowsException()
        {
            var db = NewDb();
            var svc = new HardwareAssetService(db);
            var dto = new CreateHardwareDto
            {
                AssetName = "Dell XPS",
                AssetType = "Laptop",
                Status = "Active",
                Manufacturer = "Dell",
                Model = "XPS 13",
                SerialNumber = "SN123",
                PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
                WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1))
            };
            dto.AssetTag = new string('A', 17);
            var req = new BulkHardwareRequest { Dtos = new() { dto } };
            var ex = await Assert.ThrowsAsync<Exception>(() => svc.AddHardwareBulkAsync(req));
            Assert.StartsWith("Asset tag too long:", ex.Message);
        }

        [Fact]
        public async Task ValidateRows_PurchaseDateInFuture_ThrowsException()
        {
            var db = NewDb();
            var svc = new HardwareAssetService(db);
            var dto = new CreateHardwareDto
            {
                AssetTag = "TAG123",
                AssetName = "Dell XPS",
                AssetType = "Laptop",
                Status = "Active",
                Manufacturer = "Dell",
                Model = "XPS 13",
                SerialNumber = "SN123",
                WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1))
            };
            dto.PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
            var req = new BulkHardwareRequest { Dtos = new() { dto } };
            var ex = await Assert.ThrowsAsync<Exception>(() => svc.AddHardwareBulkAsync(req));
            Assert.Equal("Purchase date cannot be in future.", ex.Message);
        }

        [Fact]
        public async Task ValidateRows_WarrantyBeforePurchase_ThrowsException()
        {
            var db = NewDb();
            var svc = new HardwareAssetService(db);
            var dto = new CreateHardwareDto
            {
                AssetTag = "TAG123",
                AssetName = "Dell XPS",
                AssetType = "Laptop",
                Status = "Active",
                Manufacturer = "Dell",
                Model = "XPS 13",
                SerialNumber = "SN123"
            };
            dto.PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow);
            dto.WarrantyExpiration = dto.PurchaseDate.AddDays(-1);
            var req = new BulkHardwareRequest { Dtos = new() { dto } };
            var ex = await Assert.ThrowsAsync<Exception>(() => svc.AddHardwareBulkAsync(req));
            Assert.Equal("Warranty expiration must be after purchase.", ex.Message);
        }

        [Fact]
        public async Task ValidateRows_ValidRow_Passes()
        {
            var db = NewDb();
            var svc = new HardwareAssetService(db);
            var dto = new CreateHardwareDto
            {
                AssetTag = "TEST-001",
                Manufacturer = "Dell",
                Model = "XPS13",
                SerialNumber = "SN-001",
                AssetType = "Laptop",
                Status = "Active",
                PurchaseDate = new DateOnly(2024, 1, 1),
                WarrantyExpiration = new DateOnly(2025, 1, 1),
                AssetName = "Dell XPS13"
            };
            var req = new BulkHardwareRequest { Dtos = new() { dto } };
            var result = await svc.AddHardwareBulkAsync(req);
            Assert.Single(result);
        }

        [Fact]
        public async Task ValidateInternalDuplicates_DuplicateAssetTags_ThrowsException()
        {
            var db = NewDb();
            var svc = new HardwareAssetService(db);
            var dto1 = new CreateHardwareDto
            {
                AssetTag = "DUP-001",
                Manufacturer = "Dell",
                Model = "XPS13",
                SerialNumber = "SN-001",
                AssetType = "Laptop",
                Status = "Active",
                PurchaseDate = new DateOnly(2024, 1, 1),
                WarrantyExpiration = new DateOnly(2025, 1, 1),
                AssetName = "Dell XPS13"
            };
            var dto2 = new CreateHardwareDto
            {
                AssetTag = "DUP-001", // duplicate tag
                Manufacturer = "HP",
                Model = "Spectre",
                SerialNumber = "SN-002",
                AssetType = "Laptop",
                Status = "Active",
                PurchaseDate = new DateOnly(2024, 2, 1),
                WarrantyExpiration = new DateOnly(2025, 2, 1),
                AssetName = "HP Spectre"
            };
            var req = new BulkHardwareRequest { Dtos = new() { dto1, dto2 } };
            var ex = await Assert.ThrowsAsync<Exception>(() => svc.AddHardwareBulkAsync(req));
            Assert.StartsWith("Duplicate asset tag in batch:", ex.Message);
        }

        [Fact]
        public async Task ValidateInternalDuplicates_NoDuplicates_Passes()
        {
            var db = NewDb();
            var svc = new HardwareAssetService(db);
            var rows = new List<CreateHardwareDto>
            {
                new CreateHardwareDto
                {
                    AssetTag = "TAG124",
                    Manufacturer = "Dell",
                    Model = "XPS",
                    SerialNumber = "SN124",
                    AssetType = "Laptop",
                    Status = "Active",
                    PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow),
                    WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5))
                },
                new CreateHardwareDto
                {
                    AssetTag = "TAG125",
                    Manufacturer = "HP",
                    Model = "Spectre",
                    SerialNumber = "SN125",
                    AssetType = "Laptop",
                    Status = "Active",
                    PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow),
                    WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5))
                }
            };
            var req = new BulkHardwareRequest { Dtos = rows };
            var saved = await svc.AddHardwareBulkAsync(req);
            Assert.Equal(2, saved.Count);
            Assert.Equal(2, db.HardwareAssets.Count());
            var dbFirst = db.HardwareAssets.First(h => h.AssetTag == "TAG124");
            Assert.Equal("SN124", dbFirst.SerialNumber);
            var dbSecond = db.HardwareAssets.First(h => h.AssetTag == "TAG125");
            Assert.Equal("SN125", dbSecond.SerialNumber);
        }

        [Fact]
        public async Task ValidateDatabaseDuplicates_ExistingAssetTag_ThrowsException()
        {
            var db = NewDb();
            db.HardwareAssets.Add(new Hardware
            {
                AssetTag = "EXISTING-001",
                Manufacturer = "Dell",
                Model = "XPS13",
                SerialNumber = "SN-EXIST-001",
                AssetType = "Laptop",
                Status = "Active",
                PurchaseDate = new DateOnly(2024, 1, 1),
                WarrantyExpiration = new DateOnly(2025, 1, 1),
                AssetName = "Dell XPS13"
            });
            await db.SaveChangesAsync();

            var svc = new HardwareAssetService(db);
            var dto = new CreateHardwareDto
            {
                AssetTag = "EXISTING-001", // duplicate tag in DB
                Manufacturer = "HP",
                Model = "Spectre",
                SerialNumber = "SN-002",
                AssetType = "Laptop",
                Status = "Active",
                PurchaseDate = new DateOnly(2024, 2, 1),
                WarrantyExpiration = new DateOnly(2025, 2, 1),
                AssetName = "HP Spectre"
            };
            var req = new BulkHardwareRequest { Dtos = new() { dto } };

            var ex = await Assert.ThrowsAsync<Exception>(() => svc.AddHardwareBulkAsync(req));
            Assert.StartsWith("Duplicate asset tag: EXISTING-001", ex.Message);
        }

        [Fact]
        public async Task ValidateDatabaseDuplicates_ExistingSerialNumber_ThrowsException()
        {
            var db = NewDb();
            db.HardwareAssets.Add(new Hardware
            {
                AssetTag = "UNIQUE-001",
                Manufacturer = "Dell",
                Model = "XPS13",
                SerialNumber = "SN-EXIST-001",
                AssetType = "Laptop",
                Status = "Active",
                PurchaseDate = new DateOnly(2024, 1, 1),
                WarrantyExpiration = new DateOnly(2025, 1, 1),
                AssetName = "Dell XPS13"
            });
            await db.SaveChangesAsync();

            var svc = new HardwareAssetService(db);

            var dto = new CreateHardwareDto
            {
                AssetTag = "UNIQUE-002",
                Manufacturer = "HP",
                Model = "Spectre",
                SerialNumber = "SN-EXIST-001", // duplicate serial number in DB
                AssetType = "Laptop",
                Status = "Active",
                PurchaseDate = new DateOnly(2024, 2, 1),
                WarrantyExpiration = new DateOnly(2025, 2, 1),
                AssetName = "HP Spectre"
            };
            var req = new BulkHardwareRequest { Dtos = new() { dto } };

            var ex = await Assert.ThrowsAsync<Exception>(() => svc.AddHardwareBulkAsync(req));
            Assert.StartsWith("Duplicate serial number: SN-EXIST-001", ex.Message);
        }

        [Fact]
        public async Task UpdateHardwareAsync_ValidEdit_UpdatesAndSaves()
        {
            var db = NewDb();
            db.HardwareAssets.Add(new Hardware
            {
                HardwareID = 10,
                AssetTag = "OLD",
                SerialNumber = "SN-1",
                AssetName = "OldName",
                PurchaseDate = new DateOnly(2024, 1, 1),
                WarrantyExpiration = new DateOnly(2025, 1, 1)
            });
            await db.SaveChangesAsync();

            var svc = new HardwareAssetService(db);
            var dto = new UpdateHardwareDto { AssetName = "NewName" };

            var updated = await svc.UpdateHardwareAsync(10, dto, CancellationToken.None);

            Assert.Equal("NewName", updated.AssetName);

            // ensure saved to DB
            var fromDb = await db.HardwareAssets.FindAsync(10);
            Assert.Equal("NewName", fromDb!.AssetName);
        }

        [Fact]
        public async Task UpdateHardwareAsync_InvalidEdit_ThrowsException()
        {
            var db = NewDb();

            db.HardwareAssets.Add(new Hardware
            {
                HardwareID = 10,
                AssetTag = "OLD",
                SerialNumber = "SN-1",
                PurchaseDate = new DateOnly(2024, 1, 1),
                WarrantyExpiration = new DateOnly(2025, 1, 1)
            });

            await db.SaveChangesAsync();

            var svc = new HardwareAssetService(db);

            var dto = new UpdateHardwareDto
            {
                PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)) // INVALID
            };

            var ex = await Assert.ThrowsAsync<Exception>(() =>
                svc.UpdateHardwareAsync(10, dto, CancellationToken.None)
            );
            // check that it contains an error for the relevant incorrect input. in this case, the purchase date is moved to an unacceptable future date.
            Assert.Contains("Purchase date cannot be in the future.", ex.Message);
        }

        [Fact]
        public async Task UpdateHardwareAsync_HardwareNotFound_ThrowsException()
        {
            // Arrange
            var db = NewDb();
            var svc = new HardwareAssetService(db);
            // No hardware is added to DB → ensures FindAsync(id) returns null
            var dto = new UpdateHardwareDto
            {
                AssetName = "Anything"
            };
            var ex = await Assert.ThrowsAsync<Exception>(() =>
                svc.UpdateHardwareAsync(999, dto, CancellationToken.None)
            );
            Assert.Contains("Hardware not found", ex.Message);
        }
    }
}
