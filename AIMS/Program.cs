using System.Security.Claims;
using System.Text.Json.Serialization;
using AIMS.Data;
using AIMS.Queries;
using AIMS.Services;
using AIMS.Utilities;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;   // for UseForwardedHeaders
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using System.IO.Abstractions;
using Microsoft.Identity.Web.UI;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Graph.Users.Item.MemberOf;
using Microsoft.Graph.Authentication;
using Azure.Identity;



var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);


// -------------------- Services --------------------
builder.Services.AddEndpointsApiExplorer();   // dev/test
builder.Services.AddSwaggerGen();             // dev/test
builder.Services.AddMemoryCache();
builder.Services.AddResponseCaching();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<SummaryCardService>();
builder.Services.AddScoped<AssetTypeCatalogService>();

// ★ Route constraint for allow-listed asset types (used for /assets/{type:allowedAssetType})
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

// ★ Policy for restricted routes (bulk upload). Supervisors excluded per AC.
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

// -------------------- Azure AD AuthN/AuthZ --------------------
builder.Services
    .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(options =>
    {
        builder.Configuration.Bind("AzureAd", options);
        options.AccessDeniedPath = "/error/not-authorized";
        options.TokenValidationParameters.RoleClaimType = "roles";
    });

//Microsoft Graph setup
builder.Services.AddSingleton<GraphServiceClient>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var tenantID = configuration["AzureAd:TenantId"];
    var clientId = configuration["AzureAd:ClientId"];
    var clientSecret = configuration["AzureAd:ClientSecret"];

    var scopes = new[] { "https://graph.microsoft.com/.default" };
    var credential = new ClientSecretCredential(tenantID, clientId, clientSecret);
    return new GraphServiceClient(credential, scopes);


});

// Register GraphUserService and its interface for DI
builder.Services.AddScoped<IGraphUserService, GraphUserService>();

builder.Services.AddAuthorization(options => // Require auth by default, you must now sign in to access the application
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

// -------------------- (Dev) CORS helper --------------------
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocalhost", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

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


// TODO: Remove when we add proper Azure Blob storage
// allow saving to wwwroot folder
app.UseStaticFiles();

// Order matters: Routing -> AuthN -> AuthZ -> status pages -> endpoints
app.UseRouting();


// CORS for Dev must come after routing
if (app.Environment.IsDevelopment())
{

    // CORS only in dev (handy for local frontend)
    app.UseCors("AllowLocalhost");
}
app.UseAuthentication();
app.UseAuthorization();

// ★ Re-execute to /error/{code} for 403/404/etc.
app.UseStatusCodePagesWithReExecute("/error/{0}");

app.MapStaticAssets();

// MVC conventional route (views)
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// Attribute-routed APIs (e.g., /api/assets)
app.MapControllers();

// Identity UI pages
app.MapRazorPages();

// Endpoint list for debugging
app.MapGet("/_endpoints", (Microsoft.AspNetCore.Routing.EndpointDataSource eds) =>
    string.Join("\n", eds.Endpoints.Select(e => e.DisplayName)));
app.MapGet("/error/not-found-raw", () => Results.Content("<!doctype html><html><head><meta charset='utf-8'><title>404</title></head><body style='font-family:system-ui;padding:2rem'><h1 style='color:var(--primary)'>404</h1><p>We couldn’t find that page.</p><a href='/' style='display:inline-block;padding:.6rem 1rem;border-radius:.5rem;background:var(--primary);color:#fff;text-decoration:none;border:1px solid var(--primary)'>Go to Dashboard</a></body></html>", "text/html"));

app.Run();

public partial class Program
{
}




