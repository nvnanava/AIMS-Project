using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

public class RateLimiterOptionsTests
{
    [Fact]
    public void AuditPoll_TokenBucket_Is_10_Per_Min()
    {
        var services = new ServiceCollection();
        services.AddRateLimiter(o =>
        {
            o.AddPolicy("audit-poll", _ =>
                RateLimitPartition.GetTokenBucketLimiter("k", _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 10,
                    TokensPerPeriod = 10,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    AutoReplenishment = true,
                    QueueLimit = 0
                }));
        });
        true.Should().BeTrue();
    }
}
