using System.Globalization;
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
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Graph;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Microsoft.IdentityModel.Tokens;


var cultureInfo = new CultureInfo("en-US");
CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;


var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);

// -------------------- Services --------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache();
builder.Services.AddResponseCaching();
builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<SummaryCardService>();
builder.Services.AddScoped<AssetTypeCatalogService>();
builder.Services.AddScoped<SoftwareSeatService>();

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

// File system (ReportsController; mockable in tests)
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
builder.Services.AddSignalR(o =>
{
    // Short, reasonable defaults to detect dropped connections faster in dev
    o.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    o.KeepAliveInterval = TimeSpan.FromSeconds(10);
    o.MaximumReceiveMessageSize = 64 * 1024; // 64 KB
});

// Feature flags (AuditRealTime, AuditPollingFallback)
builder.Services.Configure<AIMS.Services.AimsFeatures>(builder.Configuration.GetSection("Feature"));

// -------------------- Rate Limiting --------------------
builder.Services.AddRateLimiter(options =>
{
    // Return 429 instead of 503 when throttled so client can treat it as a gentle hiccup
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Primary audit poll policy (steady trickle + small burst + small queue)
    options.AddPolicy("audit-poll", httpContext =>
        RateLimitPartition.GetTokenBucketLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 8,
                TokensPerPeriod = 1,
                ReplenishmentPeriod = TimeSpan.FromMilliseconds(500), // ~2 req/sec
                AutoReplenishment = true,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 4
            }
        )
    );

    // Softer policy for the /events/latest first-paint endpoint
    options.AddPolicy("audit-poll-soft", httpContext =>
        RateLimitPartition.GetConcurrencyLimiter(
            partitionKey: "audit-soft",
            factory: _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = 16,
                QueueLimit = 8,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }
        )
    );
});

// Broadcaster to decouple hub sends from data layer
builder.Services.AddScoped<AIMS.Services.IAuditEventBroadcaster, AIMS.Services.AuditEventBroadcaster>();

// -------------------- CORS (dev) --------------------
// SignalR with cookie auth requires specific origin + AllowCredentials.
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocalhost", p =>
        p.WithOrigins("http://localhost:5119")
         .AllowAnyHeader()
         .AllowAnyMethod()
         .AllowCredentials());
});

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

builder.Logging.AddConsole();

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

// -------------------- AuthN/AuthZ (switchable) --------------------
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
    // Hybrid scheme: OAuth web app for MVC, JWT for callers that actually send Bearer tokens
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
            {
                var authz = ctx.Request.Headers.Authorization.ToString();
                var hasBearer = authz.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
                return hasBearer
                    ? JwtBearerDefaults.AuthenticationScheme
                    : OpenIdConnectDefaults.AuthenticationScheme; // <- forward to OIDC (Microsoft Identity Web manages Cookies)
            };
        })
        .AddJwtBearer(options =>
        {
            options.Authority = $"https://login.microsoftonline.com/{builder.Configuration["AzureAd:TenantId"]}/v2.0";
            options.Audience = builder.Configuration["AzureAd:ApiAudience"];
            options.RequireHttpsMetadata = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidAudiences = new[]
                {
                    builder.Configuration["AzureAd:ApiAudience"],
                    builder.Configuration["AzureAd:ClientId"]
                }
            };
        })
        .AddMicrosoftIdentityWebApp(options =>
        {
            builder.Configuration.Bind("AzureAd", options);
            options.AccessDeniedPath = "/error/not-authorized";
            options.TokenValidationParameters.RoleClaimType = "roles";

            options.Events ??= new();
            options.Events.OnRedirectToIdentityProvider = ctx =>
            {
                // If the caller sent a Bearer token or is clearly an API/JSON caller, don't redirect — return 401.
                var hasBearer = ctx.Request.Headers["Authorization"]
                    .ToString()
                    .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
                var wantsJson = ctx.Request.Headers["Accept"].ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);

                if (hasBearer || wantsJson || ctx.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    ctx.HandleResponse();
                }
                return Task.CompletedTask;
            };
        });
}

// -------------------- Microsoft Graph setup (secrets-first) --------------------
string? clientSecret = null;
var clientSecretPath = builder.Configuration["AzureAd:ClientSecretFile"];
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
    Console.WriteLine("⚠️ AzureAd ClientSecret not found (Graph auth will fail).");
}
else if (builder.Environment.IsDevelopment())
{
    Console.WriteLine($"✅ AzureAd ClientSecret loaded (len={clientSecret.Length}).");
}

var tenantID = builder.Configuration["AzureAd:TenantId"];
var clientId = builder.Configuration["AzureAd:ClientId"];
var graphScopes = new[] { "https://graph.microsoft.com/.default" };
var graphCredential = new ClientSecretCredential(tenantID, clientId, clientSecret);
builder.Services.AddSingleton(new GraphServiceClient(graphCredential, graphScopes));

builder.Services.AddScoped<IGraphUserService, GraphUserService>();

// Global "must be authenticated" by default
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});

// this line scopes the IAdminUserUpsertService to the AdminUserUpsertService class
builder.Services.AddScoped<IAdminUserUpsertService, AdminUserUpsertService>();



// Role helper policies (username allowlists)
builder.Services.AddAuthorizationBuilder()
  .AddPolicy("mbcAdmin", policy =>
      policy.RequireAssertion(context =>
          context.User.HasClaim(c =>
              c.Type == "preferred_username" &&
              new[] {
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

// Readiness ping for E2E cold-start stabilization (non-prod only)
if (!app.Environment.IsProduction())
{
    app.MapGet("/_ready", async (AIMS.Data.AimsDbContext db) =>
    {
        try
        {
            await db.Database.CanConnectAsync();
            return Results.Ok(new { ok = true });
        }
        catch
        {
            return Results.Problem("DB not ready", statusCode: 503);
        }
    }).AllowAnonymous();
}

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
    app.UseExceptionHandler("/error/not-found"); // ensure proper error page
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Dev impersonation helper (?impersonate=empIdOrEmail)
if (app.Environment.IsDevelopment())
{
    app.Use(async (ctx, next) =>
    {
        var imp = ctx.Request.Query["impersonate"].ToString();
        if (!string.IsNullOrWhiteSpace(imp))
        {
            using var scope = ctx.RequestServices.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AIMS.Data.AimsDbContext>();

            var user = await db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.EmployeeNumber == imp || u.Email == imp);

            if (user != null)
            {
                ctx.Items["ImpersonatedUserId"] = user.UserID;
                ctx.Items["ImpersonatedEmail"] = user.Email;
            }
        }

        await next();
    });
}


// Order matters
app.UseRouting();

// Enable WebSockets for SignalR
app.UseWebSockets();

// CORS (dev-only) — must be after routing, before auth when using cookies
if (app.Environment.IsDevelopment())
{
    app.UseCors("AllowLocalhost");
}

// Apply rate limiting before hitting endpoints
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// TestAuth browser sign-in helper (Playwright UI)
// Only active when UseTestAuth=true and NOT in Production
if (!app.Environment.IsProduction() && app.Configuration.GetValue<bool>("UseTestAuth", false))
{
    app.MapGet("/test/signin", async (HttpContext ctx) =>
    {
        var asUser = ctx.Request.Query["as"].ToString();
        if (string.IsNullOrWhiteSpace(asUser)) asUser = "tburguillos@csus.edu";

        var claims = new[]
        {
            new System.Security.Claims.Claim("preferred_username", asUser),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, asUser),
            new System.Security.Claims.Claim("roles", "Admin"),
        };
        var id = new System.Security.Claims.ClaimsIdentity(claims, AIMS.Utilities.TestAuthHandler.Scheme);
        var principal = new System.Security.Claims.ClaimsPrincipal(id);

        await ctx.SignInAsync(AIMS.Utilities.TestAuthHandler.Scheme, principal);
        ctx.Response.Redirect("/");
    }).AllowAnonymous();
}

app.UseStatusCodePagesWithReExecute("/error/{0}");

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapHub<AuditLogHub>("/hubs/audit"); // realtime
app.MapControllers();
app.MapRazorPages();

// Endpoint list + raw 404 page
app.MapGet("/_endpoints", (Microsoft.AspNetCore.Routing.EndpointDataSource eds) =>
    string.Join("\n", eds.Endpoints.Select(e => e.DisplayName))).AllowAnonymous();

app.MapGet("/error/not-found-raw", () => Results.Content(
    "<!doctype html><html><head><meta charset='utf-8'><title>404</title></head>" +
    "<body style='font-family:system-ui;padding:2rem'><h1 style='color:var(--primary)'>404</h1>" +
    "<p>We couldn’t find that page.</p>" +
    "<a href='/' style='display:inline-block;padding:.6rem 1rem;border-radius:.5rem;background:var(--primary);color:#fff;text-decoration:none;border:1px solid var(--primary)'>Go to Dashboard</a>" +
    "</body></html>",
    "text/html"));

app.Run();

public partial class Program { }
