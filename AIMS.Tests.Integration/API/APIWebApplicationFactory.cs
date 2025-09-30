using AIMS.Data;
using AIMS.Tests.Integration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

public class APIWebApplicationFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint>, IAsyncLifetime
    where TEntryPoint : class
{
    private DbTestHarness? _harness;

    public APIWebApplicationFactory() { }

    // Method to allow the test to "prime" the factory with the harness
    public void SetHarness(DbTestHarness harness)
    {
        _harness = harness;
    }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // service replacements go here. This is called before the host is built.
        builder.ConfigureServices(services =>
        {
            // Replace production DbContext with a test version
            var dbContextDescriptor = services.SingleOrDefault(d =>
                d.ServiceType == typeof(DbContextOptions<AimsDbContext>));

            if (dbContextDescriptor is not null)
            {
                services.Remove(dbContextDescriptor);
            }

            services.AddDbContext<AimsDbContext>(options =>
            {
                options.UseSqlServer(_harness?.ConnectionString);
            });
        });
    }

    // Override CreateHost to defer configuration until after the harness is ready.
    protected override IHost CreateHost(IHostBuilder builder)
    {
        // First, apply base host configuration
        builder.ConfigureAppConfiguration((context, conf) =>
        {
            // Clear existing configuration
            conf.Sources.Clear();

            // Add test appsettings.json
            conf.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));

            // Override with in-memory configuration from the harness
            conf.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _harness?.ConnectionString
            });
        });

        // Add additional web host configuration, such as setting the content root.
        builder.ConfigureWebHost(webHostBuilder =>
        {
            // Set the content root to the test project's base directory
            webHostBuilder.UseContentRoot(AppContext.BaseDirectory);
        });

        // The base.CreateHost() call will use the test host builder
        return base.CreateHost(builder);
    }

    public async Task InitializeAsync()
    {
        await _harness?.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _harness?.DisposeAsync();
    }
}
