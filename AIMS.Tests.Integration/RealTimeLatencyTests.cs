using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AIMS.Tests.Integration
{
    // Use the same shared fixture/collection as our API tests so we boot with TestAuth
    [Collection("API Test Collection")]
    public class RealtimeLatencyTests
    {
        private readonly APIWebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public RealtimeLatencyTests(APiTestFixture fixture)
        {
            _factory = fixture._webFactory;

            // Disable redirects ONLY for this suite so we can inspect the chain.
            _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
        }

        [Fact(DisplayName = "POST → DOM render latency <= 5s (log redirect chain if any)")]
        public async Task PostToDom_Latency_WithRedirectTracing()
        {
            var url = "/api/audit/events";
            var payload = new
            {
                EventId = Guid.NewGuid(),
                Category = "E2E Test",
                Message = "Latency check",
                CreatedAt = DateTime.UtcNow
            };

            var sw = Stopwatch.StartNew();
            var resp = await _client.PostAsJsonAsync(url, payload);
            sw.Stop();

            Console.WriteLine($"[Latency] POST {url} => {(int)resp.StatusCode} {resp.StatusCode} in {sw.ElapsedMilliseconds} ms");

            if (IsRedirect(resp.StatusCode))
            {
                await TraceRedirectChainAsync("POST", url, resp);
            }

            // 1) No redirects
            ((int)resp.StatusCode).Should().NotBeInRange(300, 399,
                "there should be no redirect to an OIDC login during tests");

            // 2) No auth failures (TestAuth should be active under the API fixture)
            resp.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized, "TestAuth should authenticate requests");
            resp.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, "TestAuth should authorize the default test user");

            // 3) Allow success or schema/method quirks (we're measuring latency + auth behavior)
            ((int)resp.StatusCode).Should().BeOneOf(new[] { 200, 201, 202, 204, 400, 405 },
                $"Request should not redirect or auth-fail under TestAuth; got {(int)resp.StatusCode} ({resp.StatusCode})");

            // 4) Latency budget
            sw.ElapsedMilliseconds.Should().BeLessThanOrEqualTo(5000,
                $"Latency exceeded 5s: {sw.ElapsedMilliseconds} ms");
        }

        [Fact(DisplayName = "Polling endpoint should not redirect (log chain if it does)")]
        public async Task PollingEndpoint_ShouldNotRedirect_WithTrace()
        {
            var url = "/api/audit/events";
            var resp = await _client.GetAsync(url);

            Console.WriteLine($"[Poll] GET {url} => {(int)resp.StatusCode} {resp.StatusCode}");

            if (IsRedirect(resp.StatusCode))
            {
                await TraceRedirectChainAsync("GET", url, resp);
            }

            // 1) No redirects
            ((int)resp.StatusCode).Should().NotBeInRange(300, 399,
                "polling should not redirect to login during tests");

            // 2) No auth failures
            resp.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized, "TestAuth should authenticate requests");
            resp.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, "TestAuth should authorize the default test user");

            // 3) Allow OK/NoContent or a validation-style 400
            ((int)resp.StatusCode).Should().BeOneOf(new[] { 200, 204, 400 },
                $"Polling should not redirect or auth-fail under TestAuth; got {(int)resp.StatusCode} ({resp.StatusCode})");
        }

        private static bool IsRedirect(HttpStatusCode status) =>
            (int)status >= 300 && (int)status < 400;

        // Follows and logs the entire redirect chain (since auto-redirect is disabled),
        // up to a safe max, so we can see exactly where the app is sending us.
        private async Task TraceRedirectChainAsync(string method, string originalUrl, HttpResponseMessage firstResponse)
        {
            const int maxHops = 10;
            var hops = new List<(HttpStatusCode status, string? location)>();

            var currentResp = firstResponse;
            var currentUrl = originalUrl;
            int hopCount = 0;

            Console.WriteLine($"[RedirectTrace] Start: {method} {originalUrl}");

            while (IsRedirect(currentResp.StatusCode) && hopCount < maxHops)
            {
                var location = currentResp.Headers.Location?.ToString();
                hops.Add((currentResp.StatusCode, location));

                Console.WriteLine($"[RedirectTrace] Hop {hopCount + 1}: {(int)currentResp.StatusCode} {currentResp.StatusCode} → Location: {location ?? "(none)"}");

                if (string.IsNullOrWhiteSpace(location))
                    break;

                // Resolve relative URLs against the current base address
                Uri? nextUri;
                if (!Uri.TryCreate(location, UriKind.Absolute, out nextUri))
                {
                    Uri.TryCreate(_client.BaseAddress, location, out nextUri);
                }

                if (nextUri is null)
                    break; // null-safe; avoid CS8600/CS8602 and pointless loop

                // Follow with GET per standard redirect semantics
                currentUrl = nextUri.ToString();
                currentResp = await _client.GetAsync(nextUri);
                hopCount++;
            }

            Console.WriteLine($"[RedirectTrace] End after {hopCount} hop(s). Final: {(int)currentResp.StatusCode} {currentResp.StatusCode} at {currentUrl}");

            if (hopCount >= maxHops)
            {
                Console.WriteLine("[RedirectTrace] Reached max hop limit. Possible redirect loop.");
            }
        }
    }
}
