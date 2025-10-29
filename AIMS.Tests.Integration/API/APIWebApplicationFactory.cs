using System.Collections.Generic;
using System.Linq;
using AIMS.Data;
using AIMS.Tests.Integration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

public class APIWebApplicationFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint> where TEntryPoint : class
{
    private DbTestHarness? _harness;

    public void SetHarness(DbTestHarness harness) => _harness = harness;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        // 1) Resolve a single connection string we will push through all channels
        var conn = _harness?.ConnectionString;
        if (string.IsNullOrWhiteSpace(conn))
        {
            // Fallback if harness not set yet
            conn = "Server=localhost,1433;Database=AIMS_Test;User Id=sa;Password=StrongP@ssword!;TrustServerCertificate=True;Encrypt=False";
        }

        // 2) Host settings
        builder.UseSetting("UseTestAuth", "true");
        builder.UseSetting("ConnectionStrings:DockerConnection", conn);
        builder.UseSetting("ConnectionStrings:DefaultConnection", conn);
        builder.UseSetting("ConnectionStrings:CliConnection", conn);
        builder.UseSetting("AzureAd:TenantId", "dummy");
        builder.UseSetting("AzureAd:ClientId", "dummy");
        builder.UseSetting("AzureAd:ClientSecret", "dummy");
        builder.UseSetting("AzureAd:ApiAudience", "api://dummy");

        // 3) Env vars
        System.Environment.SetEnvironmentVariable("UseTestAuth", "true");
        System.Environment.SetEnvironmentVariable("ConnectionStrings__DockerConnection", conn);
        System.Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", conn);
        System.Environment.SetEnvironmentVariable("ConnectionStrings__CliConnection", conn);

        // 4) In-memory config
        builder.ConfigureAppConfiguration((ctx, config) =>
        {
            var dict = new Dictionary<string, string?>
            {
                ["UseTestAuth"] = "true",
                ["AzureAd:TenantId"] = "dummy",
                ["AzureAd:ClientId"] = "dummy",
                ["AzureAd:ClientSecret"] = "dummy",
                ["AzureAd:ApiAudience"] = "api://dummy",
                ["ConnectionStrings:DockerConnection"] = conn,
                ["ConnectionStrings:DefaultConnection"] = conn,
                ["ConnectionStrings:CliConnection"] = conn,
            };
            config.AddInMemoryCollection(dict!);
        });

        // 5) Force AimsDbContext to use the harness connection (or conn fallback) and ensure schema
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AimsDbContext>));
            if (descriptor != null) services.Remove(descriptor);

            services.AddDbContext<AimsDbContext>(opt =>
            {
                opt.UseSqlServer(conn!,
                    sql => sql.EnableRetryOnFailure(5, System.TimeSpan.FromSeconds(2), null));
            });

            // Build a scoped provider to run migrations so DB schema exists
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AimsDbContext>();
            db.Database.Migrate();
        });
    }
}
