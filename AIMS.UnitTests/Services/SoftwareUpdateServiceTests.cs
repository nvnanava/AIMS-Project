using System.Threading;
using System.Threading.Tasks;
using AIMS.Data;
using AIMS.Dtos.Software;
using AIMS.Models;
using AIMS.Services;
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
            // In-memory database setup using EF Core
            var options = new DbContextOptionsBuilder<AimsDbContext>()
                .UseInMemoryDatabase($"AimsTestDb_SoftwareUpdateService_{Guid.NewGuid()}")
                .Options;

            _db = new AimsDbContext(options);
            _service = new SoftwareUpdateService(_db);
            // Seed with initial data
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

        // duplicate license key validation
        [Fact]
        public async Task ValidateEditAsync_Throws_When_LicenseKeyNotUnique()
        {
            var existing = await _db.SoftwareAssets.FirstAsync();

            _db.SoftwareAssets.Add(new Software
            {
                SoftwareName = "DuplicateApp",
                SoftwareLicenseKey = "ABC-123"
            });
            await _db.SaveChangesAsync();

            var dto = new UpdateSoftwareDto { SoftwareLicenseKey = "ABC-123" };

            await Assert.ThrowsAsync<Exception>(() =>
                _service.ValidateEditAsync(existing, dto, existing.SoftwareID, default)
            );
        }

        // null or whitespace name skips validation
        [Fact]
        public async Task ValidateEditAsync_Throws_When_NameNotUnique()
        {
            var existing = await _db.SoftwareAssets.FirstAsync();

            _db.SoftwareAssets.Add(new Software
            {
                SoftwareName = "Adobe Acrobat" // duplicate name
            });
            await _db.SaveChangesAsync();

            var dto = new UpdateSoftwareDto { SoftwareName = "Adobe Acrobat" };

            await Assert.ThrowsAsync<Exception>(() =>
                _service.ValidateEditAsync(existing, dto, existing.SoftwareID, default)
            );
        }

        //negative seats
        [Fact]
        public async Task ValidateEditAsync_Throws_When_SeatsNegative()
        {
            var existing = await _db.SoftwareAssets.FirstAsync();
            var dto = new UpdateSoftwareDto
            {
                LicenseTotalSeats = -5,
                LicenseSeatsUsed = -1
            };

            await Assert.ThrowsAsync<Exception>(() =>
                _service.ValidateEditAsync(existing, dto, existing.SoftwareID, default)
            );
        }

        // Used > Total
        [Fact]
        public async Task ValidateEditAsync_Throws_When_UsedExceedsTotal()
        {
            var existing = await _db.SoftwareAssets.FirstAsync();
            var dto = new UpdateSoftwareDto
            {
                LicenseTotalSeats = 5,
                LicenseSeatsUsed = 10
            };

            await Assert.ThrowsAsync<Exception>(() =>
                _service.ValidateEditAsync(existing, dto, existing.SoftwareID, default)
            );
        }

        // Valid DTO → PASS
        [Fact]
        public async Task ValidateEditAsync_Passes_When_Valid()
        {
            var existing = await _db.SoftwareAssets.FirstAsync();

            var dto = new UpdateSoftwareDto
            {
                SoftwareLicenseKey = "UNIQUE-999",
                LicenseTotalSeats = 10,
                LicenseSeatsUsed = 3
            };

            await _service.ValidateEditAsync(existing, dto, existing.SoftwareID, default);
        }

        // Skip License Key Validation
        [Fact]
        public async Task ValidateEditAsync_Passes_When_LicenseKeyBlank()
        {
            var existing = await _db.SoftwareAssets.FirstAsync();
            var dto = new UpdateSoftwareDto { SoftwareLicenseKey = "   " };

            await _service.ValidateEditAsync(existing, dto, existing.SoftwareID, default);
        }

        // Used == Total → allowed
        [Fact]
        public async Task ValidateEditAsync_Passes_When_UsedEqualsTotal()
        {
            var existing = await _db.SoftwareAssets.FirstAsync();
            var dto = new UpdateSoftwareDto
            {
                LicenseTotalSeats = 5,
                LicenseSeatsUsed = 5
            };

            await _service.ValidateEditAsync(existing, dto, existing.SoftwareID, default);
        }
    }
}
