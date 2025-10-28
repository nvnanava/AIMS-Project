using System.IO.Abstractions;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using AIMS.Data;
using AIMS.Hubs;
using AIMS.Queries;
using AIMS.Services;
using AIMS.Utilities; // TestAuthHandler
using Azure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Graph;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;

var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);

// -------------------- Services --------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache();
builder.Services.AddResponseCaching();
builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<SummaryCardService>();
builder.Services.AddScoped<AssetTypeCatalogService>();

// Route constraint for allow-listed asset types (used for /assets/{type:allowedAssetType})
builder.Services.Configure<RouteOptions>(o =>
{
    o.ConstraintMap["allowedAssetType"] = typeof(AIMS.Routing.AllowedAssetTypeConstraint);
});

builder.Services
    .AddControllersWithViews()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.DictionaryKeyPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// Register the concrete FileSystem class for the IFileSystem interface (ReportsController)
// necessary for mocking in UnitTesting
builder.Services.AddSingleton<IFileSystem, FileSystem>();

// Policy for restricted routes (bulk upload). Supervisors excluded.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CanBulkUpload", policy =>
        policy.RequireRole("Admin", "Manager"));
});

// Query/DAO services
builder.Services.AddScoped<UserQuery>();
builder.Services.AddScoped<AssignmentsQuery>();
builder.Services.AddScoped<HardwareQuery>();
builder.Services.AddScoped<SoftwareQuery>();
builder.Services.AddScoped<AssetQuery>();
builder.Services.AddScoped<AuditLogQuery>();
// builder.Services.AddScoped<FeedbackQuery>(); # Scaffolded
builder.Services.AddScoped<AssetSearchQuery>();
builder.Services.AddScoped<ReportsQuery>();

// SignalR for real-time audit updates
builder.Services.AddSignalR();

// Feature flags (AuditRealTime, AuditPollingFallback)
builder.Services.Configure<AIMS.Services.AimsFeatures>(builder.Configuration.GetSection("Feature"));

// Rate limiter for polling fallback (~10 req/min/IP)
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("audit-poll", httpContext =>
        RateLimitPartition.GetTokenBucketLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 10,                         // max burst
                TokensPerPeriod = 10,                    // refill amount
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                AutoReplenishment = true,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0                           // don’t queue
            })
    );
});

// Broadcaster to decouple hub sends from data layer
builder.Services.AddScoped<AIMS.Services.IAuditEventBroadcaster, AIMS.Services.AuditEventBroadcaster>();

// ---- Connection string selection (env-aware, robust) ----
string? GetConn(string name)
{
    var env = Environment.GetEnvironmentVariable($"ConnectionStrings__{name}");
    return string.IsNullOrWhiteSpace(env)
        ? builder.Configuration.GetConnectionString(name)
        : env;
}

// Prefer LocalDB on Windows Dev; otherwise Docker SQL
var preferName =
    builder.Environment.IsDevelopment() &&
    Environment.OSVersion.Platform == PlatformID.Win32NT
        ? "LocalConnection"
        : "DockerConnection";

var cs =
    GetConn(preferName) ??
    GetConn("DefaultConnection") ??
    GetConn("DockerConnection") ??
    GetConn("CliConnection");

if (string.IsNullOrWhiteSpace(cs))
{
    throw new InvalidOperationException(
        "No usable connection string found. Tried DefaultConnection, DockerConnection, CliConnection (env or appsettings).");
}

var csb = new SqlConnectionStringBuilder(cs);
Console.WriteLine($"[Startup] Using SQL host: {csb.DataSource}, DB: {csb.InitialCatalog}");

builder.Services.AddDbContext<AimsDbContext>(opt =>
    opt.UseSqlServer(cs, sql =>
        sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(2), errorNumbersToAdd: null))
);

var allowProdSeed = builder.Configuration.GetValue<bool>("AllowProdSeed", false);

// -------------------- AuthN/AuthZ (switch) --------------------

// Safe gate: TestAuth can *never* be enabled in Production.
var useTestAuth =
    !builder.Environment.IsProduction() &&
    (builder.Configuration.GetValue<bool>("UseTestAuth", false) ||
     string.Equals(builder.Environment.EnvironmentName, "Test", StringComparison.OrdinalIgnoreCase));

Console.WriteLine($"[Startup] UseTestAuth={useTestAuth}");

if (useTestAuth)
{
    builder.Services
        .AddAuthentication(options =>
        {
            options.DefaultScheme = TestAuthHandler.Scheme;
            options.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
            options.DefaultChallengeScheme = TestAuthHandler.Scheme;
        })
        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
            TestAuthHandler.Scheme,
            o => { o.TimeProvider = TimeProvider.System; }
        );
}
else
{
    builder.Services
        .AddAuthentication(options =>
        {
            options.DefaultScheme = "AppOrApi";
            options.DefaultAuthenticateScheme = "AppOrApi";
            options.DefaultChallengeScheme = "AppOrApi";
        })
        .AddPolicyScheme("AppOrApi", "AppOrApi", options =>
        {
            options.ForwardDefaultSelector = ctx =>
                ctx.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
                    ? JwtBearerDefaults.AuthenticationScheme
                    : OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.Authority = $"https://login.microsoftonline.com/{builder.Configuration["AzureAd:TenantId"]}/v2.0";
            options.Audience = builder.Configuration["AzureAd:ApiAudience"];
            options.RequireHttpsMetadata = false;
        })
        .AddMicrosoftIdentityWebApp(options =>
        {
            builder.Configuration.Bind("AzureAd", options);
            options.AccessDeniedPath = "/error/not-authorized";
            options.TokenValidationParameters.RoleClaimType = "roles";

            // For API calls, respond 401 instead of redirecting to AAD.
            options.Events ??= new();
            options.Events.OnRedirectToIdentityProvider = ctx =>
            {
                if (ctx.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    ctx.HandleResponse();
                }
                return Task.CompletedTask;
            };
        });
}

// -------------------- Microsoft Graph setup (secrets-first) --------------------
var clientSecretPath = builder.Configuration["AzureAd:ClientSecretFile"];
string? clientSecret = null;

if (!string.IsNullOrWhiteSpace(clientSecretPath) && File.Exists(clientSecretPath))
{
    clientSecret = File.ReadAllText(clientSecretPath).Trim();
}
else
{
    // Fallbacks: env var, then (last resort) appsettings
    clientSecret = Environment.GetEnvironmentVariable("AzureAd__ClientSecret")
                   ?? builder.Configuration["AzureAd:ClientSecret"];
}

if (string.IsNullOrWhiteSpace(clientSecret))
{
    Console.WriteLine("⚠️ AzureAd ClientSecret not found (auth will fail).");
}
else if (builder.Environment.IsDevelopment())
{
    // Avoid printing this in prod; just length for a quick sanity check in dev
    Console.WriteLine($"✅ AzureAd ClientSecret loaded (len={clientSecret.Length}).");
}

var tenantID = builder.Configuration["AzureAd:TenantId"];
var clientId = builder.Configuration["AzureAd:ClientId"];
var scopes = new[] { "https://graph.microsoft.com/.default" };

var credential = new ClientSecretCredential(tenantID, clientId, clientSecret);
builder.Services.AddSingleton(new GraphServiceClient(credential, scopes));

builder.Services.AddScoped<IGraphUserService, GraphUserService>();

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});

builder.Services.AddAuthorizationBuilder()
  .AddPolicy("mbcAdmin", policy =>
      policy.RequireAssertion(context =>
          context.User.HasClaim(c =>
              c.Type == "preferred_username" &&
              new[] {
                  // test accounts for now
                  "nvnanavati@csus.edu",
                  "akalustatsingh@csus.edu",
                  "tburguillos@csus.edu",
                  "keeratkhandpur@csus.edu",
                  "suhailnajimudeen@csus.edu",
                  "hkaur20@csus.edu",
                  "cameronlanzaro@csus.edu",
                  "norinphlong@csus.edu"
              }.Contains(c.Value))))
  .AddPolicy("mbcHelpDesk", policy =>
      policy.RequireAssertion(context =>
          context.User.HasClaim(c =>
              c.Type == "preferred_username" &&
              new[] {
                  "barryAllen@centralcity.edu"
              }.Contains(c.Value))))
  .AddPolicy("mbcSupervisor", policy =>
      policy.RequireAssertion(context =>
          context.User.HasClaim(c =>
              c.Type == "preferred_username" &&
              new[] {
                  "richardGrayson@gotham.edu",
                  "niyant397@gmail.com",
                  "tnburg@pacbell.net"
              }.Contains(c.Value))));

builder.Services.AddRazorPages().AddMicrosoftIdentityUI();

// -------------------- App pipeline --------------------
var app = builder.Build();

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Normalize path so the check is robust across OSes
        var path = ctx.File.PhysicalPath?.Replace('\\', '/').ToLowerInvariant() ?? "";
        if (path.Contains("/images/asset-icons/"))
        {
            // 1 year + immutable: browsers will keep icons across refreshes
            ctx.Context.Response.Headers["Cache-Control"] =
                "public, max-age=31536000, immutable";
        }
    }
});

app.UseResponseCaching();

Console.WriteLine($"[Startup] Detected OS Platform: {Environment.OSVersion.Platform}");

if (app.Environment.IsDevelopment())
{
    // Auto-migrate + seed in dev
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AimsDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

    Console.WriteLine("[Startup] Applying migrations (dev)...");
    await db.Database.MigrateAsync();

    Console.WriteLine("[Startup] Running database seeder...");
    try
    {
        await DbSeeder.SeedAsync(db, allowProdSeed: allowProdSeed, logger: logger);
        Console.WriteLine("[Startup] Seeding complete.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Seeder failed (continuing in dev).");
        Console.WriteLine("[Startup] Seeder failed, continuing in dev.");
    }

    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler("/error/not-found"); // ★ ensure proper error page
    app.UseHsts();
    app.UseHttpsRedirection();
}

if (app.Environment.IsDevelopment())
{
    app.Use(async (ctx, next) =>
    {
        // Allow ?impersonate=28809  OR  ?impersonate=john.smith@aims.local
        var imp = ctx.Request.Query["impersonate"].ToString();
        if (!string.IsNullOrWhiteSpace(imp))
        {
            // Try to resolve the user from DB once per request
            using var scope = ctx.RequestServices.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AIMS.Data.AimsDbContext>();

            var user = await db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.EmployeeNumber == imp || u.Email == imp);

            if (user != null)
            {
                // Store for downstream code
                ctx.Items["ImpersonatedUserId"] = user.UserID;
                ctx.Items["ImpersonatedEmail"] = user.Email;
            }
        }

        await next();
    });
}

// allow saving to wwwroot folder
app.UseStaticFiles();

// Order matters: Routing -> AuthN -> AuthZ -> status pages -> endpoints
app.UseRouting();

// Apply rate limiting before hitting endpoints
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{

    app.UseCors("AllowLocalhost");
}

app.UseAuthentication();
app.UseAuthorization();

app.UseStatusCodePagesWithReExecute("/error/{0}");

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapHub<AuditLogHub>("/hubs/audit");
app.MapControllers();
app.MapRazorPages();

app.MapGet("/_endpoints", (Microsoft.AspNetCore.Routing.EndpointDataSource eds) =>
    string.Join("\n", eds.Endpoints.Select(e => e.DisplayName)));

app.MapGet("/error/not-found-raw", () => Results.Content(
    "<!doctype html><html><head><meta charset='utf-8'><title>404</title></head>" +
    "<body style='font-family:system-ui;padding:2rem'><h1 style='color:var(--primary)'>404</h1>" +
    "<p>We couldn’t find that page.</p>" +
    "<a href='/' style='display:inline-block;padding:.6rem 1rem;border-radius:.5rem;background:var(--primary);color:#fff;text-decoration:none;border:1px solid var(--primary)'>Go to Dashboard</a>" +
    "</body></html>",
    "text/html"));

app.Run();

public partial class Program { }
