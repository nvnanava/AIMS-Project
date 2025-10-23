using System.Net;
using System.Net.Http.Json;
using AIMS.Data;
using AIMS.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AIMS.Tests.Integration;

[Collection("API Test Collection")]
public class ReportsApiTests
{
    private readonly APIWebApplicationFactory<Program> _factory;
    public ReportsApiTests(APiTestFixture fixture)
    {
        _factory = fixture._webFactory;
    }

    // ---- Helper Methods ---
    // Set up (create reports)
    public async Task InsertReports()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AimsDbContext>();

            await CleanUpReports();

            await db.SaveChangesAsync();
        }
    }
    // Clean up (clean out reports)
    public static async Task CleanUpReports()
    {
        string[] reportNames = { "IntTest1", "IntTest2", "IntTest3", "IntTest4", "IntTest5", "IntTest6", "IntTest7" };
        string[] hardwareNames = { };
        string[] softwareNames = { };
        string[] auditLogEntries = { };

    }


    // --- Tests ---- 

    // -- Creation --
    // Assignment Reports
    // empty report
    // non-empty report

    // Office Reports
    // empty report
    // non-empty report

    // Custom Reports
    // empty report
    // non-empty report

    // -- Listing --
    // list all reports

    // -- Downloading --
    // download one report
    // download multiple reports
}