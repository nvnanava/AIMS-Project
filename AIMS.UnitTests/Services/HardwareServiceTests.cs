using System;
using System.Threading;
using System.Threading.Tasks;
using AIMS.Data;
using AIMS.Models;
using AIMS.Services;
using AIMS.Dtos.Hardware;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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
            var existing = new Hardware { HardwareID = 1, AssetTag = "TAG1", SerialNumber = "SN1", PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow), WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)) };
            db.HardwareAssets.Add(existing);
            db.HardwareAssets.Add(new Hardware { HardwareID = 2, AssetTag = "TAG2", SerialNumber = "SN2", PurchaseDate = existing.PurchaseDate, WarrantyExpiration = existing.WarrantyExpiration });
            await db.SaveChangesAsync();

            var svc = new HardwareAssetService(db);
            var target = await db.HardwareAssets.FindAsync(2);
            var dto = new UpdateHardwareDto { AssetTag = "TAG1" };
            var ms = new ModelStateDictionary();

            var result = await svc.ValidateEditAsync(target!, dto, 2, ms, CancellationToken.None);

            Assert.NotNull(result);
            Assert.True(ms.ContainsKey(nameof(dto.AssetTag)));
        }

        [Fact]
        public async Task ValidateEditAsync_DuplicateSerial_ReturnsBadRequest()
        {
            var db = NewDb();
            db.HardwareAssets.Add(new Hardware { HardwareID = 1, AssetTag = "A1", SerialNumber = "SN-DUP", PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow), WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)) });
            db.HardwareAssets.Add(new Hardware { HardwareID = 2, AssetTag = "A2", SerialNumber = "SN2", PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow), WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(6)) });
            await db.SaveChangesAsync();

            var svc = new HardwareAssetService(db);
            var hw = await db.HardwareAssets.FindAsync(2);
            var dto = new UpdateHardwareDto { SerialNumber = "SN-DUP" };
            var ms = new ModelStateDictionary();

            var result = await svc.ValidateEditAsync(hw!, dto, 2, ms, CancellationToken.None);

            Assert.NotNull(result);
            Assert.True(ms.ContainsKey(nameof(dto.SerialNumber)));
        }

        [Fact]
        public async Task ValidateEditAsync_FuturePurchaseDate_ReturnsBadRequest()
        {
            var db = NewDb();
            db.HardwareAssets.Add(new Hardware { HardwareID = 5, AssetTag = "X", SerialNumber = "Y", PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow), WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)) });
            await db.SaveChangesAsync();

            var svc = new HardwareAssetService(db);
            var hw = await db.HardwareAssets.FindAsync(5);
            var dto = new UpdateHardwareDto { PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)) };
            var ms = new ModelStateDictionary();

            var result = await svc.ValidateEditAsync(hw!, dto, 5, ms, CancellationToken.None);

            Assert.NotNull(result);
            Assert.True(ms.ContainsKey(nameof(dto.PurchaseDate)));
        }

        [Fact]
        public async Task ValidateEditAsync_WarrantyBeforePurchase_ReturnsBadRequest()
        {
            var db = NewDb();
            db.HardwareAssets.Add(new Hardware { HardwareID = 7, AssetTag = "T", SerialNumber = "S", PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow), WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)) });
            await db.SaveChangesAsync();

            var svc = new HardwareAssetService(db);
            var hw = await db.HardwareAssets.FindAsync(7);
            var dto = new UpdateHardwareDto { WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)) };
            var ms = new ModelStateDictionary();

            var result = await svc.ValidateEditAsync(hw!, dto, 7, ms, CancellationToken.None);

            Assert.NotNull(result);
            Assert.True(ms.ContainsKey(nameof(dto.WarrantyExpiration)));
        }

        [Fact]
        public async Task ValidateEditAsync_Valid_ReturnsNull()
        {
            var db = NewDb();
            db.HardwareAssets.Add(new Hardware { HardwareID = 9, AssetTag = "T9", SerialNumber = "S9", PurchaseDate = DateOnly.FromDateTime(DateTime.UtcNow), WarrantyExpiration = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)) });
            await db.SaveChangesAsync();

            var svc = new HardwareAssetService(db);
            var hw = await db.HardwareAssets.FindAsync(9);
            var dto = new UpdateHardwareDto { AssetName = "NewName" };
            var ms = new ModelStateDictionary();

            var result = await svc.ValidateEditAsync(hw!, dto, 9, ms, CancellationToken.None);

            Assert.Null(result);
            Assert.True(ms.IsValid);
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
        public async Task NormalizeDtos_RemovesCompletelyBlankRow()
        {
            var svc = new HardwareAssetService(NewDb());

            var req = new BulkHardwareRequest
            {
                Dtos = new()
        {
            new CreateHardwareDto() // all empty
        }
            };

            var result = await svc.AddHardwareBulkAsync(req);

            Assert.NotNull(result);
            Assert.Empty(result);  // <-- the correct expectation
        }
        [Fact]
        public async Task ValidateRows_MissingAssetTag_Throws()
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
        public async Task ValidateRows_AssetTagTooLong_Throws()
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
        public async Task ValidateRows_PurchaseDateInFuture_Throws()
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
        public async Task ValidateRows_WarrantyBeforePurchase_Throws()
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
        public async Task ValidateInternalDuplicates_DuplicateAssetTags_Throws()
        {

            //test for duplicates in the same batch entry before sending to db.
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

            // Act
            var saved = await svc.AddHardwareBulkAsync(req);

            // Assert #1 – service returned new entities
            Assert.Equal(2, saved.Count);

            // Assert #2 – they were persisted in the DB
            Assert.Equal(2, db.HardwareAssets.Count());

            var dbFirst = db.HardwareAssets.First(h => h.AssetTag == "TAG124");
            Assert.Equal("SN124", dbFirst.SerialNumber);

            var dbSecond = db.HardwareAssets.First(h => h.AssetTag == "TAG125");
            Assert.Equal("SN125", dbSecond.SerialNumber);
        }
        [Fact]
        public async Task ValidateDatabaseDuplicates_ExistingAssetTag_Throws()
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
        public async Task ValidateDatabaseDuplicates_ExistingSerialNumber_Throws()
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
        public async Task ValidateDatabaseDuplicates_DupSerialAndTagShowsBothErrors()
        {
            var db = NewDb();
            db.HardwareAssets.Add(new Hardware
            {
                AssetTag = "DUP-TAG",
                Manufacturer = "Dell",
                Model = "XPS13",
                SerialNumber = "DUP-SN",
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
                AssetTag = "DUP-TAG", // duplicate tag
                Manufacturer = "HP",
                Model = "Spectre",
                SerialNumber = "DUP-SN", // duplicate serial
                AssetType = "Laptop",
                Status = "Active",
                PurchaseDate = new DateOnly(2024, 2, 1),
                WarrantyExpiration = new DateOnly(2025, 2, 1),
                AssetName = "HP Spectre"
            };
            var req = new BulkHardwareRequest { Dtos = new() { dto } };

            var ex = await Assert.ThrowsAsync<Exception>(() => svc.AddHardwareBulkAsync(req));
            Assert.Contains("Duplicate asset tag: DUP-TAG", ex.Message);
            Assert.Contains("Duplicate serial number: DUP-SN", ex.Message);
        }



    }

}