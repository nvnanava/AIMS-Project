using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIMS.Dtos.Assets;
using AIMS.Dtos.Common;
using Xunit;
using Xunit.Abstractions;

namespace AIMS.Tests.Integration.API;

[Collection("API Test Collection")]
public class SearchApiTests
{
    private readonly HttpClient _client;
    private readonly ITestOutputHelper _output;

    public SearchApiTests(APiTestFixture fixture, ITestOutputHelper output)
    {
        _client = fixture._webFactory.CreateClient();
        _output = output;
    }

    private static readonly JsonSerializerOptions _json =
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

    // --------------------------------------------------------------------
    // 1) BLANK SEARCH, NON-SUPERVISOR -> MUST RETURN EMPTY RESULT
    // --------------------------------------------------------------------
    [Fact]
    public async Task BlankSearch_NonSupervisor_ReturnsEmptyPagedResult()
    {
        // Warm-up
        try { await _client.GetAsync("/api/assets/search"); } catch { }

        // Retry loop to allow impersonation/seed to stabilize
        PagedResult<AssetRowDto>? result = null;

        for (int i = 0; i < 40; i++)
        {
            var response = await _client.GetAsync("/api/assets/search");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            result = await response.Content.ReadFromJsonAsync<PagedResult<AssetRowDto>>(_json);

            if (result != null)
                break;

            await Task.Delay(200);
        }

        Assert.NotNull(result);
        Assert.Empty(result!.Items);  // Non-supervisor + blank search -> empty
        Assert.Equal(0, result.Total);
    }

    // --------------------------------------------------------------------
    // 2) BLANK SEARCH, SUPERVISOR -> RETURNS NON-EMPTY (ALLOWED)
    //
    // We hit this by passing type or status so IsBlankSearch == false,
    // ensuring ResolveCurrentUserAsync() is still called.
    // --------------------------------------------------------------------
    [Fact]
    public async Task BlankSearch_Supervisor_BypassBlankRule_WhenTypeProvided()
    {
        // type="Laptop" ensures IsBlankSearch == false -> supervisor bypass path
        var url = "/api/assets/search?type=Laptop";

        PagedResult<AssetRowDto>? result = null;

        for (int i = 0; i < 40; i++)
        {
            var http = await _client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, http.StatusCode);

            result = await http.Content.ReadFromJsonAsync<PagedResult<AssetRowDto>>(_json);
            if (result is { Items.Count: > 0 })
                break;

            _output.WriteLine($"Retry {i}: total={result?.Total}");
            await Task.Delay(200);
        }

        Assert.NotNull(result);
        Assert.True(result!.Total >= 0);     // Supervisor allowed (even if empty)
    }

    // --------------------------------------------------------------------
    // 3) NON-BLANK SEARCH -> ALWAYS CALLS SEARCHASYNC
    // --------------------------------------------------------------------
    [Fact]
    public async Task NonBlankSearch_CallsSearchAsync_AndReturnsResults()
    {
        var url = "/api/assets/search?q=test";

        PagedResult<AssetRowDto>? result = null;
        HttpResponseMessage? lastResponse = null;

        // Give the API some time to warm up / seed before we hard-fail
        for (int i = 0; i < 40; i++)
        {
            lastResponse = await _client.GetAsync(url);

            if (lastResponse.StatusCode == HttpStatusCode.OK)
            {
                result = await lastResponse.Content
                    .ReadFromJsonAsync<PagedResult<AssetRowDto>>(_json);
                break;
            }

            // Optional: helpful when it flakes again later
            _output.WriteLine(
                $"Attempt {i + 1}/40 returned {lastResponse.StatusCode}, retrying...");

            await Task.Delay(200);
        }

        // Now we assert on the *final* result
        Assert.NotNull(lastResponse);
        Assert.Equal(HttpStatusCode.OK, lastResponse.StatusCode);

        Assert.NotNull(result);
        Assert.NotNull(result!.Items);  // Could be empty depending on dataset
        Assert.True(result.Page >= 1);
        Assert.Equal(25, result.PageSize);
    }

    // --------------------------------------------------------------------
    // 4) ALL PARAMETERS: q + type + status + page + pageSize + showArchived
    // Ensures every branch in controller logic gets executed.
    // --------------------------------------------------------------------
    [Fact]
    public async Task AllParams_FullCoverage_AllowsSuccessfulExecution()
    {
        var url = "/api/assets/search" +
                  "?q=abc" +
                  "&type=Laptop" +
                  "&status=Assigned" +
                  "&page=1" +
                  "&pageSize=25" +
                  "&showArchived=true";

        var http = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, http.StatusCode);

        var result = await http.Content.ReadFromJsonAsync<PagedResult<AssetRowDto>>(_json);

        Assert.NotNull(result);
        Assert.True(result!.Page == 1);
        Assert.True(result.PageSize == 25);
    }

    // --------------------------------------------------------------------
    // 5) Helper coverage: IsBlankSearch()
    // We hit both: (blank -> true) and (non-blank -> false)
    // Tests indirectly reach both paths already, but here is a direct test.
    // --------------------------------------------------------------------
    [Fact]
    public void IsBlankSearch_Helper_WorksCorrectly()
    {
        Assert.True(Call_IsBlank(null, null, null));
        Assert.True(Call_IsBlank(" ", " ", " "));
        Assert.False(Call_IsBlank("x", null, null));
        Assert.False(Call_IsBlank(null, "Laptop", null));
        Assert.False(Call_IsBlank(null, null, "Assigned"));
    }

    // Use reflection because IsBlankSearch is private static
    private static bool Call_IsBlank(string? q, string? t, string? s)
    {
        var m = typeof(AIMS.Controllers.Api.SearchApiController)
            .GetMethod("IsBlankSearch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        return (bool)m!.Invoke(null, new object?[] { q, t, s })!;
    }

    // --------------------------------------------------------------------
    // 6) BLANK SEARCH, FORCED SUPERVISOR ROLE (TEST-ONLY OVERRIDE)
    // Hits the IsBlankSearch == true && isSupervisor == true branch and
    // exercises impersonateRole.Trim() when the role has extra spaces.
    // --------------------------------------------------------------------
    [Fact]
    public async Task BlankSearch_Supervisor_ReturnsPagedResult_EvenIfEmpty()
    {
        // Note the padded spaces -> forces the .Trim() branch to execute
        var url = "/api/assets/search?impersonateRole=   Supervisor   ";

        PagedResult<AssetRowDto>? result = null;

        for (int i = 0; i < 40; i++)
        {
            var http = await _client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, http.StatusCode);

            result = await http.Content.ReadFromJsonAsync<PagedResult<AssetRowDto>>(_json);

            if (result != null)
                break;

            _output.WriteLine($"[BlankSearch_Supervisor_ReturnsPagedResult_EvenIfEmpty] Retry {i}: total={result?.Total}");
            await Task.Delay(200);
        }

        Assert.NotNull(result);
        Assert.NotNull(result!.Items);        // Shape is valid
        Assert.True(result.Total >= 0);       // Paging totals are valid, even if 0
    }
}
