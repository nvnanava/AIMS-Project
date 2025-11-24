using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace AIMS.Tests.Integration;

[Collection("API Test Collection")]
public class OfficesControllerIntegrationTests
{
    private readonly HttpClient _client;
    private readonly ITestOutputHelper _output;
    private readonly JsonSerializerOptions _jsonOptions;

    public OfficesControllerIntegrationTests(APiTestFixture fixture, ITestOutputHelper output)
    {
        _client = fixture._webFactory.CreateClient();
        _output = output;
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    // ---------- Helpers ----------

    private async Task EnsureOfficesSeededAsync()
    {
        const string url = "/api/debug/seed-offices";

        var response = await _client.PostAsync(url, null);
        _output.WriteLine($"POST {url} -> {(int)response.StatusCode} {response.StatusCode}");
        var body = await response.Content.ReadAsStringAsync();
        _output.WriteLine("SeedOffices response: " + body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<JsonElement[]> GetAvailableOfficesFromDebugAsync()
    {
        const string url = "/api/debug/offices";

        var response = await _client.GetAsync(url);
        _output.WriteLine($"GET {url} -> {(int)response.StatusCode} {response.StatusCode}");
        var body = await response.Content.ReadAsStringAsync();
        _output.WriteLine("Debug GetOffices response: " + body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        return json.EnumerateArray().ToArray();
    }

    private static bool TryGet(JsonElement obj, string[] keys, out JsonElement value)
    {
        foreach (var k in keys)
        {
            if (obj.TryGetProperty(k, out value))
                return true;
        }
        value = default;
        return false;
    }

    // ---------- Tests ----------

    // 1. GetOffices
    [Fact]
    public async Task GetOffices_WhenOfficesExist_ReturnsListWithIdAndName()
    {
        await EnsureOfficesSeededAsync();

        // real endpoint under test
        const string url = "/api/offices";

        var response = await _client.GetAsync(url);
        _output.WriteLine($"GET {url} -> {(int)response.StatusCode} {response.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        Assert.Equal(JsonValueKind.Array, json.ValueKind);

        var offices = json.EnumerateArray().ToArray();
        Assert.NotEmpty(offices);

        var first = offices[0];

        Assert.True(
            TryGet(first, new[] { "officeID", "officeId" }, out var idProp),
            "Expected: officeID or officeId property"
        );
        Assert.Equal(JsonValueKind.Number, idProp.ValueKind);

        Assert.True(first.TryGetProperty("officeName", out var nameProp), "Expected officeName property");
        Assert.Equal(JsonValueKind.String, nameProp.ValueKind);
    }

    // 2. SearchOffices — matching query
    [Fact]
    public async Task SearchOffices_WithMatchingQuery_ReturnsOnlyMatching()
    {
        await EnsureOfficesSeededAsync();

        // We grab an existing name from the debug API
        var offices = await GetAvailableOfficesFromDebugAsync();
        Assert.NotEmpty(offices);

        var sample = offices[0];

        sample.TryGetProperty("officeName", out var nameProp);
        var fullName = nameProp.GetString() ?? "";
        Assert.False(string.IsNullOrWhiteSpace(fullName));

        var query = fullName.Length >= 3 ? fullName[..3] : fullName;
        var encoded = Uri.EscapeDataString(query);

        // real endpoint
        var url = $"/api/offices/search?query={encoded}";

        var response = await _client.GetAsync(url);
        _output.WriteLine($"GET {url} -> {(int)response.StatusCode} {response.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.Equal(JsonValueKind.Array, json.ValueKind);

        var results = json.EnumerateArray().ToArray();
        Assert.NotEmpty(results);

        foreach (var office in results)
        {
            Assert.True(office.TryGetProperty("officeName", out var officeNameProp));
            var officeName = officeNameProp.GetString() ?? "";
            Assert.Contains(query, officeName, StringComparison.OrdinalIgnoreCase);
        }
    }

    // 3. SearchOffices — no matches
    [Fact]
    public async Task SearchOffices_WithNoMatches_ReturnsEmptyList()
    {
        await EnsureOfficesSeededAsync();

        const string query = "__NO_POSSIBLE_MATCH__";
        var encoded = Uri.EscapeDataString(query);

        var url = $"/api/offices/search?query={encoded}";

        var response = await _client.GetAsync(url);
        _output.WriteLine($"GET {url} -> {(int)response.StatusCode} {response.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.Equal(JsonValueKind.Array, json.ValueKind);

        var results = json.EnumerateArray().ToArray();
        Assert.Empty(results);
    }
}
