using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace AIMS.Tests.Integration;

[Collection("API Test Collection")]
public class AssetsAPITests
{
        private readonly HttpClient _client;
        private readonly ITestOutputHelper _output;

        public AssetsAPITests(APiTestFixture fixture, ITestOutputHelper output)
        {
                _client = fixture._webFactory.CreateClient();
                _output = output;
        }

        [Fact]
        public async Task Unique_NoParams_ReturnSuccessAndList()
        {
                // Baseline types that must always exist (seed/csv both)
                var required = new List<string> { "Desktop", "Headset", "Laptop", "Monitor", "Software" };
                // Optional types that may or may not exist depending on seed mode
                var optional = new List<string> { "Charging Cable" };

                List<string>? actual = null;
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                // Retry up to ~15s for cold start/seed settle
                for (var attempt = 1; attempt <= 60; attempt++)
                {
                        actual = await _client.GetFromJsonAsync<List<string>>("/api/assets/types/unique", options);
                        if (actual is not null && required.All(x => actual.Contains(x)))
                                break;

                        _output.WriteLine($"Attempt {attempt}: [{string.Join(", ", actual ?? new())}]");
                        await Task.Delay(250);
                }

                Assert.NotNull(actual);

                // Must contain all required
                var missingRequired = required.Except(actual!).ToList();
                _output.WriteLine("Missing required: " + string.Join(", ", missingRequired));
                Assert.True(!missingRequired.Any(), "Still missing required types after retries.");

                // Assert there are no unknown surprises (beyond required + optional)
                var known = required.Concat(optional).ToList();

                // Assert there are no unknown surprises
                var unknown = actual!.Except(known).ToList();
                _output.WriteLine("Unknown extras: " + string.Join(", ", unknown));
                Assert.True(!unknown.Any(), "Unexpected types present.");

                // ---- Ordering: the API returns an alphabetical list. Assert that, rather than a hard-coded order. ----
                var alpha = actual.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                Assert.Equal(alpha, actual);
        }
}
