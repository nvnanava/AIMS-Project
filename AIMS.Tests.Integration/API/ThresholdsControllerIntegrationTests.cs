using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace AIMS.Tests.Integration;

[Collection("API Test Collection")]
public class ThresholdsControllerIntegrationTests
{
    private readonly HttpClient _client;
    private readonly ITestOutputHelper _output;
    private readonly JsonSerializerOptions _jsonOptions;

    public ThresholdsControllerIntegrationTests(APiTestFixture fixture, ITestOutputHelper output)
    {
        _client = fixture._webFactory.CreateClient();
        _output = output;
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    // Small helper so we always send a body that matches (even if DTO changes)
    private async Task<HttpResponseMessage> UpsertThresholdAsync(string assetType, int value)
    {
        var payload = new
        {
            assetType,           // safe even if DTO ignores it
            thresholdValue = value
        };

        var response = await _client.PutAsJsonAsync($"/api/thresholds/{assetType}", payload);
        _output.WriteLine($"PUT /api/thresholds/{assetType} -> {(int)response.StatusCode} {response.StatusCode}");
        var body = await response.Content.ReadAsStringAsync();
        _output.WriteLine("Response body: " + body);
        return response;
    }

    // 1. GET /api/thresholds
    [Fact]
    public async Task GetThresholds_ReturnsSeededThresholds_OrderedByAssetType()
    {
        // Arrange: ensure these thresholds exist in the DB for this test run
        var required = new[] { "Laptop", "Monitor", "Printer" };
        var random = new Random();

        foreach (var type in required)
        {
            var value = random.Next(1, 50);
            var put = await UpsertThresholdAsync(type, value);
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        }

        // Act
        var response = await _client.GetAsync("/api/thresholds");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.Equal(JsonValueKind.Array, json.ValueKind);

        var items = json.EnumerateArray().ToArray();
        Assert.NotEmpty(items);

        var assetTypes = items
            .Select(i => i.GetProperty("assetType").GetString())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        foreach (var type in required)
        {
            Assert.Contains(type, assetTypes, StringComparer.OrdinalIgnoreCase);
        }

        var sorted = assetTypes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(sorted, assetTypes);
    }

    // 2. PUT creates new threshold
    [Fact]
    public async Task Put_CreatesNewThreshold_WhenNoneExists()
    {
        const string newAssetType = "DockingStation-TestCreate";

        var putResponse = await UpsertThresholdAsync(newAssetType, 7);
        Assert.Equal(HttpStatusCode.NoContent, putResponse.StatusCode);

        var getResponse = await _client.GetAsync("/api/thresholds");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var json = await getResponse.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        var created = json.EnumerateArray()
            .FirstOrDefault(i => string.Equals(
                i.GetProperty("assetType").GetString(),
                newAssetType,
                StringComparison.OrdinalIgnoreCase));

        Assert.Equal(JsonValueKind.Object, created.ValueKind);
        Assert.Equal(7, created.GetProperty("thresholdValue").GetInt32());
    }

    // 3. PUT updates existing + cache invalidation
    [Fact]
    public async Task Put_UpdatesExistingThreshold_AndNextGetReflectsChange()
    {
        const string assetType = "Laptop-TestUpdate";

        // Arrange: create a known threshold first
        var createResponse = await UpsertThresholdAsync(assetType, 5);
        Assert.Equal(HttpStatusCode.NoContent, createResponse.StatusCode);

        // Act: update it
        var updateResponse = await UpsertThresholdAsync(assetType, 10);
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        var afterResponse = await _client.GetAsync("/api/thresholds");
        Assert.Equal(HttpStatusCode.OK, afterResponse.StatusCode);

        var afterJson = await afterResponse.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var updated = afterJson.EnumerateArray()
            .First(i => string.Equals(
                i.GetProperty("assetType").GetString(),
                assetType,
                StringComparison.OrdinalIgnoreCase));

        Assert.Equal(10, updated.GetProperty("thresholdValue").GetInt32());
    }

    // 4. Validation failure stays as-is (this one already passes for them)
    [Fact]
    public async Task Put_InvalidPayload_ReturnsBadRequest_WithProblemDetails()
    {
        const string assetType = "Laptop";

        var invalidJson = """{ "thresholdValue": "not-a-number" }""";
        var content = new StringContent(invalidJson, Encoding.UTF8, "application/json");

        var response = await _client.PutAsync($"/api/thresholds/{assetType}", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.Equal(JsonValueKind.Object, problem.ValueKind);

        Assert.True(problem.TryGetProperty("status", out var statusProp));
        Assert.Equal(400, statusProp.GetInt32());
    }
}
