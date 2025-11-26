using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using AIMS.Data;
using AIMS.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIMS.Tests.Integration.Controllers
{
    public class SeedOfficesIntegrationTests : IClassFixture<APIWebApplicationFactory<Program>>
    {
        private readonly APIWebApplicationFactory<Program> _factory;

        public SeedOfficesIntegrationTests(APIWebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task SeedOffices_WhenDatabaseIsEmpty_InsertsTestOffice()
        {
            var client = _factory.CreateClient();

            // Clear DB first
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AimsDbContext>();
                db.Offices.RemoveRange(db.Offices);
                await db.SaveChangesAsync();
            }

            // Call endpoint
            var response = await client.PostAsync("/api/office/seed-offices", null);
            var content = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Seeded a test office successfully.", content);

            // Verify DB
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AimsDbContext>();
                var offices = await db.Offices.ToListAsync();
                Assert.Single(offices);
                Assert.Equal("Test Office", offices.First().OfficeName);
            }
        }

        [Fact]
        public async Task SeedOffices_WhenOfficesAlreadyExist_DoesNotDuplicate()
        {
            var client = _factory.CreateClient();

            // Seed a test office first
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AimsDbContext>();
                if (!db.Offices.Any())
                {
                    db.Offices.Add(new Office { OfficeName = "Test Office", Location = "Test Office" });
                    await db.SaveChangesAsync();
                }
            }

            // Call endpoint again
            var response = await client.PostAsync("/api/office/seed-offices", null);
            var content = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Offices already exist — no action taken.", content);

            // Verify DB still has only one office
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AimsDbContext>();
                var offices = await db.Offices.ToListAsync();
                Assert.Single(offices);
                Assert.Equal("Test Office", offices.First().OfficeName);
            }
        }
    }
}
