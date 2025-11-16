using System.Threading;
using System.Threading.Tasks;
using AIMS.Data;
using AIMS.Dtos.Software;
using AIMS.Models;
using AIMS.Services;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AIMS.UnitTests.Services
{
    public class SoftwareUpdateServiceTests
    {
        private readonly AimsDbContext _db;
        private readonly SoftwareUpdateService _service;

        public SoftwareUpdateServiceTests()
        {
            // Use EF Core's InMemory provider for fast, isolated tests
            var options = new DbContextOptionsBuilder<AimsDbContext>()
                .UseInMemoryDatabase($"AimsTestDb_SoftwareUpdateService_{Guid.NewGuid()}")
                .Options;

            _db = new AimsDbContext(options);
            _service = new SoftwareUpdateService(_db);

            // Seed one software record for license key uniqueness testing
            _db.SoftwareAssets.Add(new Software
            {
                SoftwareID = 4499,
                SoftwareName = "Adobe Acrobat",
                SoftwareLicenseKey = "ABC-123",
                LicenseTotalSeats = 10,
                LicenseSeatsUsed = 2
            });
            _db.SaveChanges();
        }

        [Fact]
        public async Task ValidateEditAsync_Should_AddError_When_LicenseKeyNotUnique()
        {
            // existing seeded record: ABC-123
            var existing = await _db.SoftwareAssets.FirstAsync();

            // add a duplicate with same license key but different ID
            _db.SoftwareAssets.Add(new Software
            {
                SoftwareName = "DuplicateApp",
                SoftwareLicenseKey = "ABC-123",
                LicenseTotalSeats = 5,
                LicenseSeatsUsed = 1
            });
            await _db.SaveChangesAsync();

            var dto = new UpdateSoftwareDto
            {
                SoftwareLicenseKey = "ABC-123"
            };

            var modelState = new ModelStateDictionary();
            var result = await _service.ValidateEditAsync(existing, dto, existing.SoftwareID, modelState, default);

            Assert.NotNull(result);
            Assert.False(modelState.IsValid);
            Assert.True(modelState.ContainsKey(nameof(dto.SoftwareLicenseKey)));
        }

        [Fact]
        public async Task ValidateEditAsync_Should_AddError_When_SeatsNegative()
        {
            var existing = await _db.SoftwareAssets.FirstAsync();
            var dto = new UpdateSoftwareDto
            {
                LicenseTotalSeats = -5,
                LicenseSeatsUsed = -1
            };

            var modelState = new ModelStateDictionary();

            var result = await _service.ValidateEditAsync(existing, dto, existing.SoftwareID, modelState, default);

            Assert.NotNull(result);
            Assert.False(modelState.IsValid);
            Assert.Contains(nameof(dto.LicenseTotalSeats), modelState.Keys);
            Assert.Contains(nameof(dto.LicenseSeatsUsed), modelState.Keys);
        }

        [Fact]
        public async Task ValidateEditAsync_Should_AddError_When_UsedExceedsTotal()
        {
            var existing = await _db.SoftwareAssets.FirstAsync();
            var dto = new UpdateSoftwareDto
            {
                LicenseTotalSeats = 5,
                LicenseSeatsUsed = 10
            };

            var modelState = new ModelStateDictionary();

            var result = await _service.ValidateEditAsync(existing, dto, existing.SoftwareID, modelState, default);

            Assert.NotNull(result);
            Assert.False(modelState.IsValid);
            Assert.True(modelState.ContainsKey(nameof(dto.LicenseSeatsUsed)));
        }

        [Fact]
        public async Task ValidateEditAsync_Should_Pass_When_DataValid()
        {
            var existing = await _db.SoftwareAssets.FirstAsync();
            var dto = new UpdateSoftwareDto
            {
                SoftwareLicenseKey = "UNIQUE-999",
                LicenseTotalSeats = 10,
                LicenseSeatsUsed = 3
            };

            var modelState = new ModelStateDictionary();

            var result = await _service.ValidateEditAsync(existing, dto, existing.SoftwareID, modelState, default);

            Assert.Null(result);
            Assert.True(modelState.IsValid);
        }
        [Fact]
        public async Task ValidateEditAsync_Should_Skip_LicenseKeyValidation_When_NullOrEmpty()
        {
            var existing = await _db.SoftwareAssets.FirstAsync();
            var dto = new UpdateSoftwareDto { SoftwareLicenseKey = "   " };

            var modelState = new ModelStateDictionary();
            var result = await _service.ValidateEditAsync(existing, dto, existing.SoftwareID, modelState, default);

            Assert.Null(result);
            Assert.True(modelState.IsValid); // no errors should be added
        }

        [Fact]
        public async Task ValidateEditAsync_Should_Pass_When_UsedEqualsTotal()
        {
            var existing = await _db.SoftwareAssets.FirstAsync();
            var dto = new UpdateSoftwareDto
            {
                LicenseTotalSeats = 5,
                LicenseSeatsUsed = 5
            };

            var modelState = new ModelStateDictionary();
            var result = await _service.ValidateEditAsync(existing, dto, existing.SoftwareID, modelState, default);

            Assert.Null(result);
            Assert.True(modelState.IsValid);
        }
    }
}
