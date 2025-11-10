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
        // Same pattern as AssetsAPITests
        _client = fixture._webFactory.CreateClient();
        _output = output;
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    // ---------------- 1. GET /api/thresholds ----------------
    [Fact]
    public async Task GetThresholds_ReturnsSeededThresholds_OrderedByAssetType()
    {
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

        Assert.Contains("Laptop", assetTypes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Monitor", assetTypes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Printer", assetTypes, StringComparer.OrdinalIgnoreCase);

        var sorted = assetTypes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(sorted, assetTypes);
    }

    // ---------------- 2. PUT creates new threshold ----------------
    [Fact]
    public async Task Put_CreatesNewThreshold_WhenNoneExists()
    {
        const string newAssetType = "DockingStation";

        var payload = new
        {
            thresholdValue = 7
        };

        var putResponse = await _client.PutAsJsonAsync($"/api/thresholds/{newAssetType}", payload);
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

    // ---------------- 3. PUT updates existing + cache invalidation ----------------
    [Fact]
    public async Task Put_UpdatesExistingThreshold_AndNextGetReflectsChange()
    {
        const string existingAssetType = "Laptop";

        var initialResponse = await _client.GetAsync("/api/thresholds");
        Assert.Equal(HttpStatusCode.OK, initialResponse.StatusCode);

        var initialJson = await initialResponse.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var laptop = initialJson.EnumerateArray()
            .First(i => string.Equals(
                i.GetProperty("assetType").GetString(),
                existingAssetType,
                StringComparison.OrdinalIgnoreCase));

        var currentValue = laptop.GetProperty("thresholdValue").GetInt32();
        var newValue = currentValue + 5;

        var payload = new
        {
            thresholdValue = newValue
        };

        var putResponse = await _client.PutAsJsonAsync($"/api/thresholds/{existingAssetType}", payload);
        Assert.Equal(HttpStatusCode.NoContent, putResponse.StatusCode);

        var afterResponse = await _client.GetAsync("/api/thresholds");
        Assert.Equal(HttpStatusCode.OK, afterResponse.StatusCode);

        var afterJson = await afterResponse.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var updatedLaptop = afterJson.EnumerateArray()
            .First(i => string.Equals(
                i.GetProperty("assetType").GetString(),
                existingAssetType,
                StringComparison.OrdinalIgnoreCase));

        Assert.Equal(newValue, updatedLaptop.GetProperty("thresholdValue").GetInt32());
    }

    // ---------------- 4. Validation failure -> 400 ProblemDetails ----------------
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
