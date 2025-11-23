using System.Net;
using System.Net.Http.Json;
using AIMS;
using AIMS.Data;
using AIMS.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIMS.Tests.Integration.Controllers
{
    public class DebugControllerOfficeTests
        : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public DebugControllerOfficeTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        private HttpClient CreateClientWithFreshDb(out AimsDbContext db)
        {
            var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {

                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<AimsDbContext>));

                    if (descriptor != null)
                        services.Remove(descriptor);

                    // Add a NEW in-memory database for each test
                    services.AddDbContext<AimsDbContext>(options =>
                    {
                        options.UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}");
                    });
                });
            });

            var scope = factory.Services.CreateScope();
            db = scope.ServiceProvider.GetRequiredService<AimsDbContext>();

            return factory.CreateClient();
        }

        // -------------------------------------------------------------
        // 1. SeedOffices when database is empty
        // -------------------------------------------------------------
        [Fact]
        public async Task SeedOffices_ShouldInsertThreeOffices_WhenDbIsEmpty()
        {
            // Arrange
            var client = CreateClientWithFreshDb(out var db);

            // Ensure DB is empty
            Assert.Empty(db.Offices);

            // Act
            var response = await client.PostAsync("/api/debug/seed-offices", null);
            var message = await response.Content.ReadAsStringAsync();

            // Assert HTTP response
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Seeded 3 offices successfully.", message);

            // Assert DB state
            Assert.Equal(3, db.Offices.Count());

            var names = db.Offices.Select(o => o.OfficeName).ToList();
            Assert.Contains("Houston", names);
            Assert.Contains("San Ramon", names);
            Assert.Contains("Remote", names);
        }

        // -------------------------------------------------------------
        // 2. SeedOffices when offices already exist
        // -------------------------------------------------------------
        [Fact]
        public async Task SeedOffices_ShouldNotInsertDuplicates_WhenDbAlreadyHasRecords()
        {
            // Arrange
            var client = CreateClientWithFreshDb(out var db);

            // Seed one record manually
            db.Offices.Add(new Office { OfficeName = "Houston" });
            await db.SaveChangesAsync();

            // Act
            var response = await client.PostAsync("/api/debug/seed-offices", null);
            var message = await response.Content.ReadAsStringAsync();

            // Assert HTTP response
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Offices already exist — no action taken.", message);

            // Assert that DB was NOT modified
            Assert.Equal(1, db.Offices.Count());
        }
    }
}
