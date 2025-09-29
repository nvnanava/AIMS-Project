using AIMS.Data;
using AIMS.Queries;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
// ★ NEW usings (for route constraint + policies)
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

// -------------------- Services --------------------
builder.Services.AddControllersWithViews();
builder.Services.AddEndpointsApiExplorer();   // dev/test
builder.Services.AddSwaggerGen();             // dev/test
builder.Services.AddMemoryCache();
builder.Services.AddResponseCaching();

// ★ Route constraint for allow-listed asset types (used for /assets/{type:allowedAssetType})
builder.Services.Configure<RouteOptions>(o =>
{
    o.ConstraintMap["allowedAssetType"] = typeof(AIMS.Routing.AllowedAssetTypeConstraint);
});

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
builder.Services.AddScoped<FeedbackQuery>();

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
        ? "DefaultConnection"
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
        options.AccessDeniedPath = "/error/not-authorized"; // ★ corrected to use your error page
        options.TokenValidationParameters.RoleClaimType = "roles";
    });

// keep your existing custom policies
builder.Services.AddAuthorizationBuilder()
  .AddPolicy("mbcAdmin", policy =>
      policy.RequireAssertion(context =>
          context.User.HasClaim(c =>
              c.Type == "preferred_username" &&
              new[] {
                  // test accounts for now
                  "niyant397@gmail.com",
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
                  "richardGrayson@gotham.edu"
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

    // CORS only in dev (handy for local frontend)
    app.UseCors("AllowLocalhost");
}
else
{
    app.UseExceptionHandler("/error/not-found"); // ★ ensure proper error page
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Order matters: Routing -> AuthN -> AuthZ -> status pages -> endpoints
app.UseRouting();
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

