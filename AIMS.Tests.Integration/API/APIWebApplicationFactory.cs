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
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((context, conf) =>
        {
            conf.Sources.Clear();
            conf.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true);
            conf.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _harness?.ConnectionString
            });
        });


        // service replacements go here. This is called before the host is built.
        builder.ConfigureServices(services =>
        {
            // Remove the app's real OIDC registration so it never validates ClientId/etc.
            services.RemoveAll<IConfigureOptions<OpenIdConnectOptions>>();
            services.RemoveAll<IPostConfigureOptions<OpenIdConnectOptions>>();
            services.RemoveAll<IValidateOptions<OpenIdConnectOptions>>();

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

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                options.DefaultChallengeScheme = TestAuthHandler.Scheme;
                options.DefaultScheme = TestAuthHandler.Scheme;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });

            services.AddAuthorization();

            // Make OIDC inert so validation won't throw if it's ever resolved
            services.PostConfigureAll<OpenIdConnectOptions>(o =>
            {
                // Required fields so Validate() passes
                o.ClientId ??= "test-client";
                o.Authority ??= "https://login.microsoftonline.com/common";
                o.CallbackPath = "/signin-oidc";
                o.TokenValidationParameters ??= new TokenValidationParameters();

                // Make it as no-op as possible
                o.DisableTelemetry = true;
                o.SaveTokens = false;
                // (optional) If something challenges explicitly with OIDC, map sign-in to our test scheme
                o.SignInScheme ??= TestAuthHandler.Scheme;
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
