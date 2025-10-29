using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIMS.Data;
using AIMS.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using AIMS.Dtos.Reports;
using AIMS.ViewModels;

namespace AIMS.Tests.Integration.API;

[Collection("API Test Collection")]
public class ReportsApiTests
{
    private readonly APIWebApplicationFactory<Program> _factory;
    public ReportsApiTests(APiTestFixture fixture)
    {
        _factory = fixture._webFactory;

        var culture = new CultureInfo("en-US");
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }


    // ---- Helper Methods ---
    public string[] getCSVHeaders(string[] lines)
    {
        return lines[0].Split(',');
    }
    public bool checkIfEachLineContains(string[] lines, int headerIndx, string[] value)
    {
        if (lines == null || lines.Length == 0)
            return false;

        if (value is null)
            return false;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var split = line.Split(',');
            if (headerIndx >= split.Length)
                return false; // line missing column

            var chunk = split[headerIndx].Trim();

            // must contain *at least one* allowed value
            bool containsAny = value.Any(v =>
                chunk.Contains(v, StringComparison.OrdinalIgnoreCase));

            if (!containsAny)
            {
                return false;
            }
        }

        return true;
    }

    private static string NewSerial() => Guid.NewGuid().ToString("N");
    // Set up (create reports)
    private async Task<DateTime> InsertAssetData()
    {

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AimsDbContext>();


            await CleanUpReports(db);

            await db.SaveChangesAsync();

            var currentTime = DateTime.Now;
            // generate hardware assets
            var m1 = new Hardware { AssetType = "ReportsTest", SerialNumber = NewSerial() };
            var m2 = new Hardware { AssetType = "ReportsTest", SerialNumber = NewSerial() };
            var m3 = new Hardware { AssetType = "ReportsTest", Status = "Marked for Survey", SerialNumber = NewSerial() };
            var m4 = new Hardware { AssetType = "ReportsTest", Status = "In Repair", SerialNumber = NewSerial() };


            // generate software assets
            var s1 = new Software { SoftwareType = "ReportsTest", SoftwareLicenseKey = "ReportsTest1", LicenseTotalSeats = 56, LicenseSeatsUsed = 54 };
            var s2 = new Software { SoftwareType = "ReportsTest", SoftwareLicenseKey = "ReportsTest2" };
            var s3 = new Software { SoftwareType = "ReportsTest", SoftwareLicenseKey = "ReportsTest3", SoftwareLicenseExpiration = DateOnly.FromDateTime(currentTime.AddDays(1)) };

            db.HardwareAssets.AddRange(m1, m2, m3, m4);
            db.SoftwareAssets.AddRange(s1, s2, s3);

            await db.SaveChangesAsync();
            return currentTime;
        }

    }
    private async Task InsertReports()
    {

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AimsDbContext>();


            await CleanUpReports(db);

            await db.SaveChangesAsync();

            var office = await GetRandomOffice();

            var r1 = new Report { Name = "IntTest1", Type = "Assignment", DateCreated = DateTime.Now };
            var r2 = new Report { Name = "IntTest2", Type = "Office", DateCreated = DateTime.Now, GeneratedForOfficeID = office.OfficeID };
            var r3 = new Report { Name = "IntTest3", Type = "Custom", DateCreated = DateTime.Now, GeneratedForOfficeID = office.OfficeID };

            db.Reports.AddRange(r1, r2, r3);
            await db.SaveChangesAsync();
        }
    }

    // Clean up (clean out reports)
    private static async Task CleanUpReports(AimsDbContext db)
    {
        string[] reportNames = { "IntTest1", "IntTest2", "IntTest3", "IntTest4", "IntTest5", "IntTest6", "IntTest7" };
        string[] hardwareTypes = { "ReportsTest" };
        string[] softwareTypes = { "ReportsTest" };

        var reports = await db.Reports
                            .Where(r => reportNames.Contains(r.Name))
                            .ToListAsync();
        db.Reports.RemoveRange(reports);

        var hw = await db.HardwareAssets
               .Where(h => hardwareTypes.Contains(h.AssetType))
               .ToListAsync();
        db.HardwareAssets.RemoveRange(hw);
        var sw = await db.SoftwareAssets
               .Where(s => softwareTypes.Contains(s.SoftwareType))
               .ToListAsync();
        db.SoftwareAssets.RemoveRange(sw);

        await db.SaveChangesAsync();

    }

    private async Task<User> GetRandomUser()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AimsDbContext>();
            return await db.Users.Take<User>(1).FirstAsync();
        }
    }
    private async Task<Office> GetRandomOffice()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AimsDbContext>();
            return await db.Offices.Take<Office>(1).FirstAsync();
        }
    }


    // --- Tests ---- 

    // -- Creation --
    // Assignment Reports
    // empty report
    [Fact]
    public async Task CreateAssignmentReports_FutureRange_CreatesEmptyReport()
    {

        var startDate = await InsertAssetData();
        var user = await GetRandomUser();

        var client = _factory.CreateClient();

        var query = new Dictionary<string, string?>
        {
            ["start"] = startDate.AddDays(16).ToString("MM-dd-yyyy"),
            ["end"] = startDate.AddDays(18).ToString("MM-dd-yyyy"),
            ["reportName"] = "IntTest1",
            ["CreatorUserID"] = user.UserID.ToString(),
            ["type"] = "Assignment"
        };

        var url = QueryHelpers.AddQueryString("/api/reports", query);
        var resp = await client.PostAsync(url, null);
        var content = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, $"Expected OK, got {resp.StatusCode}. Body: {content}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var stream = await resp.Content.ReadAsStreamAsync();
        var json = await JsonDocument.ParseAsync(stream);

        // Access fields
        var id = json.RootElement.GetProperty("reportID").GetInt32();
        var len = json.RootElement.GetProperty("contentLength").GetInt32();

        Assert.True(!(id < 0));
        // the headers are 126 bytes
        Assert.Equal(126, len);

    }
    // non-empty report
    [Fact]
    public async Task CreateAssignmentReports_ValidRange_CreatesNonEmptyReport()
    {

        var startDate = await InsertAssetData();
        var user = await GetRandomUser();

        var client = _factory.CreateClient();

        var query = new Dictionary<string, string?>
        {
            ["start"] = startDate.ToString("MM-dd-yyyy"),
            ["end"] = startDate.AddDays(1).ToString("MM-dd-yyyy"),
            ["reportName"] = "IntTest2",
            ["CreatorUserID"] = user.UserID.ToString(),
            ["type"] = "Assignment"
        };

        var url = QueryHelpers.AddQueryString("/api/reports", query);
        var resp = await client.PostAsync(url, null);
        var content = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, $"Expected OK, got {resp.StatusCode}. Body: {content}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var stream = await resp.Content.ReadAsStreamAsync();
        var json = await JsonDocument.ParseAsync(stream);

        // Access fields
        var id = json.RootElement.GetProperty("reportID").GetInt32();
        var len = json.RootElement.GetProperty("contentLength").GetInt32();

        Assert.True(!(id < 0));
        // the headers are 69 bytes
        Assert.True(len > 0);

        // get report and verify headers
        var downloadUrl = $"/api/reports/download/{id}";
        var downloadResp = await client.GetAsync(downloadUrl);

        Assert.True(resp.IsSuccessStatusCode, $"Expected OK, got {resp.StatusCode}. Body: {content}");

        // Assert content type
        Assert.Equal("text/csv", downloadResp.Content.Headers.ContentType?.MediaType);

        var fileStr = await downloadResp.Content.ReadAsStringAsync();
        Assert.NotEmpty(fileStr);

        var lines = fileStr.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        string[] expectedHeaders = { "AuditLogID", "Assignee", "Assignee Office", "Asset Name", "Asset Type", "Asset License or Serial", "Action", "Description", "Timestamp", "Asset Comment" };
        string[] actualHeaders = getCSVHeaders(lines);
        Assert.Equal(expectedHeaders, actualHeaders);


    }

    // Office Reports
    // empty report
    [Fact]
    public async Task CreateOfficeReports_FutureRange_CreatesEmptyReport()
    {

        var startDate = await InsertAssetData();
        var user = await GetRandomUser();
        var office = await GetRandomOffice();

        var client = _factory.CreateClient();

        var query = new Dictionary<string, string?>
        {
            ["start"] = startDate.AddDays(16).ToString("MM-dd-yyyy"),
            ["end"] = startDate.AddDays(18).ToString("MM-dd-yyyy"),
            ["reportName"] = "IntTest3",
            ["CreatorUserID"] = user.UserID.ToString(),
            ["type"] = "Office",
            ["OfficeID"] = office.OfficeID.ToString()
        };

        var url = QueryHelpers.AddQueryString("/api/reports", query);
        var resp = await client.PostAsync(url, null);
        var content = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, $"Expected OK, got {resp.StatusCode}. Body: {content}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var stream = await resp.Content.ReadAsStreamAsync();
        var json = await JsonDocument.ParseAsync(stream);

        // Access fields
        var id = json.RootElement.GetProperty("reportID").GetInt32();
        var len = json.RootElement.GetProperty("contentLength").GetInt32();

        Assert.True(!(id < 0));
        // the headers are 126 bytes
        Assert.Equal(126, len);

    }
    // non-empty report
    [Fact]
    public async Task CreateOfficeReports_ValidRange_CreatesNonEmptyReport()
    {

        var startDate = await InsertAssetData();
        var user = await GetRandomUser();
        var office = await GetRandomOffice();

        var client = _factory.CreateClient();

        var query = new Dictionary<string, string?>
        {
            ["start"] = startDate.ToString("MM-dd-yyyy"),
            ["end"] = startDate.AddDays(1).ToString("MM-dd-yyyy"),
            ["reportName"] = "IntTest4",
            ["CreatorUserID"] = user.UserID.ToString(),
            ["type"] = "Office",
            ["OfficeID"] = office.OfficeID.ToString()
        };

        var url = QueryHelpers.AddQueryString("/api/reports", query);
        var resp = await client.PostAsync(url, null);
        var content = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, $"Expected OK, got {resp.StatusCode}. Body: {content}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var stream = await resp.Content.ReadAsStreamAsync();
        var json = await JsonDocument.ParseAsync(stream);

        // Access fields
        var id = json.RootElement.GetProperty("reportID").GetInt32();
        var len = json.RootElement.GetProperty("contentLength").GetInt32();

        Assert.True(!(id < 0));
        // the headers are 69 bytes
        Assert.True(len > 0);

        // get report and verify headers
        var downloadUrl = $"/api/reports/download/{id}";
        var downloadResp = await client.GetAsync(downloadUrl);

        Assert.True(resp.IsSuccessStatusCode, $"Expected OK, got {resp.StatusCode}. Body: {content}");

        // Assert content type
        Assert.Equal("text/csv", downloadResp.Content.Headers.ContentType?.MediaType);

        var fileStr = await downloadResp.Content.ReadAsStringAsync();
        Assert.NotEmpty(fileStr);

        var lines = fileStr.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        string[] expectedHeaders = { "AuditLogID", "Assignee", "Assignee Office", "Asset Name", "Asset Type", "Asset License or Serial", "Action", "Description", "Timestamp", "Asset Comment" };
        string[] actualHeaders = getCSVHeaders(lines);
        Assert.Equal(expectedHeaders, actualHeaders);
    }

    // Custom Reports
    // empty report
    [Fact]
    public async Task CreateCustomReports_FutureRange_CreatesEmptyReport()
    {

        var startDate = await InsertAssetData();
        var user = await GetRandomUser();

        var client = _factory.CreateClient();

        var query = new Dictionary<string, string?>
        {
            ["start"] = startDate.AddDays(16).ToString("MM-dd-yyyy"),
            ["end"] = startDate.AddDays(18).ToString("MM-dd-yyyy"),
            ["reportName"] = "IntTest3",
            ["CreatorUserID"] = user.UserID.ToString(),
            ["type"] = "Custom",
        };

        var url = QueryHelpers.AddQueryString("/api/reports", query);
        var resp = await client.PostAsync(url, null);
        var content = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, $"Expected OK, got {resp.StatusCode}. Body: {content}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var stream = await resp.Content.ReadAsStreamAsync();
        var json = await JsonDocument.ParseAsync(stream);

        // Access fields
        var id = json.RootElement.GetProperty("reportID").GetInt32();
        var len = json.RootElement.GetProperty("contentLength").GetInt32();

        Assert.True(!(id < 0));
        // the headers are 126 bytes
        Assert.Equal(126, len);

    }
    // Hardware-Only
    [Fact]
    public async Task CreateCustomReports_HardwareOnly_CreatesNonEmptyReport()
    {

        var startDate = await InsertAssetData();
        var user = await GetRandomUser();

        var client = _factory.CreateClient();

        var customOptions = new CustomReportOptionsDto
        {
            seeSoftware = false,
        };

        var query = new Dictionary<string, string?>
        {
            ["start"] = startDate.AddDays(16).ToString("MM-dd-yyyy"),
            ["end"] = startDate.AddDays(18).ToString("MM-dd-yyyy"),
            ["reportName"] = "IntTest3",
            ["CreatorUserID"] = user.UserID.ToString(),
            ["type"] = "Custom",
        };

        var url = QueryHelpers.AddQueryString("/api/reports", query);
        var resp = await client.PostAsJsonAsync(url, customOptions);
        var content = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, $"Expected OK, got {resp.StatusCode}. Body: {content}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var stream = await resp.Content.ReadAsStreamAsync();
        var json = await JsonDocument.ParseAsync(stream);

        // Access fields
        var id = json.RootElement.GetProperty("reportID").GetInt32();
        var len = json.RootElement.GetProperty("contentLength").GetInt32();

        Assert.True(!(id < 0));
        // the headers are 69 bytes
        Assert.True(len > 0);

        // get report and verify headers
        var downloadUrl = $"/api/reports/download/{id}";
        var downloadResp = await client.GetAsync(downloadUrl);

        Assert.True(resp.IsSuccessStatusCode, $"Expected OK, got {resp.StatusCode}. Body: {content}");

        // Assert content type
        Assert.Equal("text/csv", downloadResp.Content.Headers.ContentType?.MediaType);

        var fileStr = await downloadResp.Content.ReadAsStringAsync();
        Assert.NotEmpty(fileStr);

        var lines = fileStr.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        string[] expectedHeaders = { "AuditLogID", "Assignee", "Assignee Office", "Asset Name", "Asset Type", "Asset License or Serial", "Action", "Description", "Timestamp", "Asset Comment" };
        string[] actualHeaders = getCSVHeaders(lines);
        Assert.Equal(expectedHeaders, actualHeaders);

        Assert.True(checkIfEachLineContains(lines[1..], 3, ["Hardware"]));

    }

    // Sofware-Only
    [Fact]
    public async Task CreateCustomReports_SoftwareOnly_CreatesNonEmptyReport()
    {

        var startDate = await InsertAssetData();
        var user = await GetRandomUser();

        var client = _factory.CreateClient();

        var customOptions = new CustomReportOptionsDto
        {
            seeHardware = false,
        };


        var query = new Dictionary<string, string?>
        {
            ["start"] = startDate.AddDays(16).ToString("MM-dd-yyyy"),
            ["end"] = startDate.AddDays(18).ToString("MM-dd-yyyy"),
            ["reportName"] = "IntTest3",
            ["CreatorUserID"] = user.UserID.ToString(),
            ["type"] = "Custom",
        };

        var url = QueryHelpers.AddQueryString("/api/reports", query);
        var resp = await client.PostAsJsonAsync(url, customOptions);
        var content = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, $"Expected OK, got {resp.StatusCode}. Body: {content}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var stream = await resp.Content.ReadAsStreamAsync();
        var json = await JsonDocument.ParseAsync(stream);

        // Access fields
        var id = json.RootElement.GetProperty("reportID").GetInt32();
        var len = json.RootElement.GetProperty("contentLength").GetInt32();

        Assert.True(!(id < 0));
        // the headers are 69 bytes
        Assert.True(len > 0);

        // get report and verify headers
        var downloadUrl = $"/api/reports/download/{id}";
        var downloadResp = await client.GetAsync(downloadUrl);

        Assert.True(resp.IsSuccessStatusCode, $"Expected OK, got {resp.StatusCode}. Body: {content}");

        // Assert content type
        Assert.Equal("text/csv", downloadResp.Content.Headers.ContentType?.MediaType);

        var fileStr = await downloadResp.Content.ReadAsStringAsync();
        Assert.NotEmpty(fileStr);

        var lines = fileStr.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        string[] expectedHeaders = { "AuditLogID", "Assignee", "Assignee Office", "Asset Name", "Asset Type", "Asset License or Serial", "Action", "Description", "Timestamp", "Asset Comment" };
        string[] actualHeaders = getCSVHeaders(lines);
        Assert.Equal(expectedHeaders, actualHeaders);

        Assert.True(checkIfEachLineContains(lines[1..], 3, ["Software"]));

    }
    // Toggle Offices
    [Fact]
    public async Task CreateCustomReports_OfficeOff_CreatesNonEmptyReport()
    {

        var startDate = await InsertAssetData();
        var user = await GetRandomUser();

        var client = _factory.CreateClient();

        var customOptions = new CustomReportOptionsDto
        {
            seeOffice = false,
            seeUsers = true,
        };

        var query = new Dictionary<string, string?>
        {
            ["start"] = startDate.AddDays(16).ToString("MM-dd-yyyy"),
            ["end"] = startDate.AddDays(18).ToString("MM-dd-yyyy"),
            ["reportName"] = "IntTest3",
            ["CreatorUserID"] = user.UserID.ToString(),
            ["type"] = "Custom",
        };

        var url = QueryHelpers.AddQueryString("/api/reports", query);
        var resp = await client.PostAsJsonAsync(url, customOptions);
        var content = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, $"Expected OK, got {resp.StatusCode}. Body: {content}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var stream = await resp.Content.ReadAsStreamAsync();
        var json = await JsonDocument.ParseAsync(stream);

        // Access fields
        var id = json.RootElement.GetProperty("reportID").GetInt32();
        var len = json.RootElement.GetProperty("contentLength").GetInt32();

        Assert.True(!(id < 0));
        // the headers are 69 bytes
        Assert.True(len > 0);

        // get report and verify headers
        var downloadUrl = $"/api/reports/download/{id}";
        var downloadResp = await client.GetAsync(downloadUrl);

        Assert.True(resp.IsSuccessStatusCode, $"Expected OK, got {resp.StatusCode}. Body: {content}");

        // Assert content type
        Assert.Equal("text/csv", downloadResp.Content.Headers.ContentType?.MediaType);

        var fileStr = await downloadResp.Content.ReadAsStringAsync();
        Assert.NotEmpty(fileStr);

        var lines = fileStr.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        string[] expectedHeaders = { "AuditLogID", "Assignee", "Asset Name", "Asset Type", "Asset License or Serial", "Action", "Description", "Timestamp", "Asset Comment" };
        string[] actualHeaders = getCSVHeaders(lines);
        Assert.Equal(expectedHeaders, actualHeaders);
    }
    [Fact]
    public async Task CreateCustomReports_OfficeOn_CreatesNonEmptyReport()
    {

        var startDate = await InsertAssetData();
        var user = await GetRandomUser();

        var client = _factory.CreateClient();

        var customOptions = new CustomReportOptionsDto
        {
            seeHardware = true,
            seeSoftware = true,
            seeExpiration = false,
            seeOffice = true,
            seeUsers = false,
            filterByMaintenance = false
        };

        var query = new Dictionary<string, string?>
        {
            ["start"] = startDate.AddDays(16).ToString("MM-dd-yyyy"),
            ["end"] = startDate.AddDays(18).ToString("MM-dd-yyyy"),
            ["reportName"] = "IntTest3",
            ["CreatorUserID"] = user.UserID.ToString(),
            ["type"] = "Custom",
        };

        var url = QueryHelpers.AddQueryString("/api/reports", query);
        var resp = await client.PostAsJsonAsync(url, customOptions);
        var content = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, $"Expected OK, got {resp.StatusCode}. Body: {content}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var stream = await resp.Content.ReadAsStreamAsync();
        var json = await JsonDocument.ParseAsync(stream);

        // Access fields
        var id = json.RootElement.GetProperty("reportID").GetInt32();
        var len = json.RootElement.GetProperty("contentLength").GetInt32();

        Assert.True(!(id < 0));
        // the headers are 69 bytes
        Assert.True(len > 0);

        // get report and verify headers
        var downloadUrl = $"/api/reports/download/{id}";
        var downloadResp = await client.GetAsync(downloadUrl);

        Assert.True(resp.IsSuccessStatusCode, $"Expected OK, got {resp.StatusCode}. Body: {content}");

        // Assert content type
        Assert.Equal("text/csv", downloadResp.Content.Headers.ContentType?.MediaType);

        var fileStr = await downloadResp.Content.ReadAsStringAsync();
        Assert.NotEmpty(fileStr);

        var lines = fileStr.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        string[] expectedHeaders = { "AuditLogID", "Assignee Office", "Asset Name", "Asset Type", "Asset License or Serial", "Action", "Description", "Timestamp", "Asset Comment" };
        string[] actualHeaders = getCSVHeaders(lines);
        Assert.Equal(expectedHeaders, actualHeaders);
    }
    // Toggle Users
    [Fact]
    public async Task CreateCustomReports_UsersOn_CreatesNonEmptyReport()
    {

        var startDate = await InsertAssetData();
        var user = await GetRandomUser();

        var client = _factory.CreateClient();

        var customOptions = new CustomReportOptionsDto
        {
            seeUsers = true,
        };


        var query = new Dictionary<string, string?>
        {
            ["start"] = startDate.AddDays(16).ToString("MM-dd-yyyy"),
            ["end"] = startDate.AddDays(18).ToString("MM-dd-yyyy"),
            ["reportName"] = "IntTest3",
            ["CreatorUserID"] = user.UserID.ToString(),
            ["type"] = "Custom",
        };

        var url = QueryHelpers.AddQueryString("/api/reports", query);
        var resp = await client.PostAsJsonAsync(url, customOptions);
        var content = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, $"Expected OK, got {resp.StatusCode}. Body: {content}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var stream = await resp.Content.ReadAsStreamAsync();
        var json = await JsonDocument.ParseAsync(stream);

        // Access fields
        var id = json.RootElement.GetProperty("reportID").GetInt32();
        var len = json.RootElement.GetProperty("contentLength").GetInt32();

        Assert.True(!(id < 0));
        // the headers are 69 bytes
        Assert.True(len > 0);

        // get report and verify headers
        var downloadUrl = $"/api/reports/download/{id}";
        var downloadResp = await client.GetAsync(downloadUrl);

        Assert.True(resp.IsSuccessStatusCode, $"Expected OK, got {resp.StatusCode}. Body: {content}");

        // Assert content type
        Assert.Equal("text/csv", downloadResp.Content.Headers.ContentType?.MediaType);

        var fileStr = await downloadResp.Content.ReadAsStringAsync();
        Assert.NotEmpty(fileStr);

        var lines = fileStr.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        string[] expectedHeaders = { "AuditLogID", "Assignee", "Assignee Office", "Asset Name", "Asset Type", "Asset License or Serial", "Action", "Description", "Timestamp", "Asset Comment" };
        string[] actualHeaders = getCSVHeaders(lines);
        Assert.Equal(expectedHeaders, actualHeaders);
    }
    [Fact]
    public async Task CreateCustomReports_UsersOff_CreatesNonEmptyReport()
    {

        var startDate = await InsertAssetData();
        var user = await GetRandomUser();

        var client = _factory.CreateClient();

        var customOptions = new CustomReportOptionsDto
        {
            seeUsers = false,
        };

        var query = new Dictionary<string, string?>
        {
            ["start"] = startDate.AddDays(16).ToString("MM-dd-yyyy"),
            ["end"] = startDate.AddDays(18).ToString("MM-dd-yyyy"),
            ["reportName"] = "IntTest3",
            ["CreatorUserID"] = user.UserID.ToString(),
            ["type"] = "Custom",
        };

        var url = QueryHelpers.AddQueryString("/api/reports", query);
        var resp = await client.PostAsJsonAsync(url, customOptions);
        var content = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, $"Expected OK, got {resp.StatusCode}. Body: {content}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var stream = await resp.Content.ReadAsStreamAsync();
        var json = await JsonDocument.ParseAsync(stream);

        // Access fields
        var id = json.RootElement.GetProperty("reportID").GetInt32();
        var len = json.RootElement.GetProperty("contentLength").GetInt32();

        Assert.True(!(id < 0));
        // the headers are 69 bytes
        Assert.True(len > 0);

        // get report and verify headers
        var downloadUrl = $"/api/reports/download/{id}";
        var downloadResp = await client.GetAsync(downloadUrl);

        Assert.True(resp.IsSuccessStatusCode, $"Expected OK, got {resp.StatusCode}. Body: {content}");

        // Assert content type
        Assert.Equal("text/csv", downloadResp.Content.Headers.ContentType?.MediaType);

        var fileStr = await downloadResp.Content.ReadAsStringAsync();
        Assert.NotEmpty(fileStr);

        var lines = fileStr.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        string[] expectedHeaders = { "AuditLogID", "Assignee Office", "Asset Name", "Asset Type", "Asset License or Serial", "Action", "Description", "Timestamp", "Asset Comment" };
        string[] actualHeaders = getCSVHeaders(lines);
        Assert.Equal(expectedHeaders, actualHeaders);
    }
    // Expiration
    [Fact]
    public async Task CreateCustomReports_ExpiringOnly_CreatesNonEmptyReport()
    {

        var startDate = await InsertAssetData();
        var user = await GetRandomUser();

        var client = _factory.CreateClient();

        var customOptions = new CustomReportOptionsDto
        {
            seeExpiration = true
        };

        var query = new Dictionary<string, string?>
        {
            ["start"] = startDate.AddDays(16).ToString("MM-dd-yyyy"),
            ["end"] = startDate.AddDays(18).ToString("MM-dd-yyyy"),
            ["reportName"] = "IntTest3",
            ["CreatorUserID"] = user.UserID.ToString(),
            ["type"] = "Custom",
        };

        var url = QueryHelpers.AddQueryString("/api/reports", query);
        var resp = await client.PostAsJsonAsync(url, customOptions);
        var content = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, $"Expected OK, got {resp.StatusCode}. Body: {content}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var stream = await resp.Content.ReadAsStreamAsync();
        var json = await JsonDocument.ParseAsync(stream);

        // Access fields
        var id = json.RootElement.GetProperty("reportID").GetInt32();
        var len = json.RootElement.GetProperty("contentLength").GetInt32();

        Assert.True(!(id < 0));
        // the headers are 69 bytes
        Assert.True(len > 0);

        // get report and verify headers
        var downloadUrl = $"/api/reports/download/{id}";
        var downloadResp = await client.GetAsync(downloadUrl);

        Assert.True(resp.IsSuccessStatusCode, $"Expected OK, got {resp.StatusCode}. Body: {content}");

        // Assert content type
        Assert.Equal("text/csv", downloadResp.Content.Headers.ContentType?.MediaType);

        var fileStr = await downloadResp.Content.ReadAsStringAsync();
        Assert.NotEmpty(fileStr);

        var lines = fileStr.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        string[] expectedHeaders = { "AuditLogID", "Assignee", "Assignee Office", "Asset Name", "Asset Type", "Asset License or Serial", "Action", "Description", "Timestamp", "Asset Comment", "Expiration" };
        string[] actualHeaders = getCSVHeaders(lines);
        Assert.Equal(expectedHeaders, actualHeaders);
    }
    // Maintanance
    [Fact]
    public async Task CreateCustomReports_MaintanenceOnly_CreatesNonEmptyReport()
    {

        var startDate = await InsertAssetData();
        var user = await GetRandomUser();

        var client = _factory.CreateClient();

        var customOptions = new CustomReportOptionsDto
        {
            filterByMaintenance = true,
        };

        var query = new Dictionary<string, string?>
        {
            ["start"] = startDate.AddDays(16).ToString("MM-dd-yyyy"),
            ["end"] = startDate.AddDays(18).ToString("MM-dd-yyyy"),
            ["reportName"] = "IntTest3",
            ["CreatorUserID"] = user.UserID.ToString(),
            ["type"] = "Custom",
        };

        var url = QueryHelpers.AddQueryString("/api/reports", query);
        var resp = await client.PostAsJsonAsync(url, customOptions);
        var content = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.IsSuccessStatusCode, $"Expected OK, got {resp.StatusCode}. Body: {content}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var stream = await resp.Content.ReadAsStreamAsync();
        var json = await JsonDocument.ParseAsync(stream);

        // Access fields
        var id = json.RootElement.GetProperty("reportID").GetInt32();
        var len = json.RootElement.GetProperty("contentLength").GetInt32();

        Assert.True(!(id < 0));
        // the headers are 69 bytes
        Assert.True(len > 0);

        // get report and verify headers
        var downloadUrl = $"/api/reports/download/{id}";
        var downloadResp = await client.GetAsync(downloadUrl);

        Assert.True(resp.IsSuccessStatusCode, $"Expected OK, got {resp.StatusCode}. Body: {content}");

        // Assert content type
        Assert.Equal("text/csv", downloadResp.Content.Headers.ContentType?.MediaType);

        var fileStr = await downloadResp.Content.ReadAsStringAsync();
        Assert.NotEmpty(fileStr);

        var lines = fileStr.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        string[] expectedHeaders = { "AuditLogID", "Assignee", "Assignee Office", "Asset Name", "Asset Type", "Asset License or Serial", "Action", "Description", "Timestamp", "Asset Comment", "Status" };
        string[] actualHeaders = getCSVHeaders(lines);
        Assert.Equal(expectedHeaders, actualHeaders);

        Assert.True(checkIfEachLineContains(lines[1..], 6, ["In Repair", "Marked for Survey", "Seats Remaining"]));
    }

    // -- Listing --
    // list all reports
    [Fact]
    public async Task ListReports_ReturnsList()
    {

        await InsertAssetData();
        await InsertReports();

        var client = _factory.CreateClient();
        var url = "api/reports/list";

        var resp = await client.GetAsync(url);
        var content = await resp.Content.ReadFromJsonAsync<List<ReportsVm>>();


        var seededReports = new[] { "IntTest1", "IntTest2", "IntTest3" };
        Assert.True(resp.IsSuccessStatusCode, $"Expected OK, got {resp.StatusCode}. Body: {content}");
        Assert.True(content?.Count >= 3);
        Assert.Contains(content, r => seededReports.Contains(r.Name));
    }

    // -- Downloading -- This is contained in the validation part of the
    // creating report tests
}