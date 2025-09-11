using AIMS.Data;
using AIMS.Queries;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
var builder = WebApplication.CreateBuilder(args);

// -------------------- Services --------------------
builder.Services.AddControllersWithViews();
builder.Services.AddEndpointsApiExplorer();   // dev/test
builder.Services.AddSwaggerGen();             // dev/test
builder.Services.AddMemoryCache();
builder.Services.AddResponseCaching();

// Optional query service (used by DiagnosticsController and others)
builder.Services.AddScoped<UserQuery>();
builder.Services.AddScoped<HardwareQuery>();
builder.Services.AddScoped<SoftwareQuery>();
builder.Services.AddScoped<AssignmentsQuery>();
builder.Services.AddScoped<AssetQuery>();

// ---- Connection string selection (robust + env overrides) ----
string? GetConn(string name)
{
    // Prefer env var "ConnectionStrings__{Name}" if provided (Docker/k8s friendly)
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

// Optional feature flag for prod seeding (defaults false)
var allowProdSeed = builder.Configuration.GetValue<bool>("AllowProdSeed", false);

// -------------------- App pipeline --------------------
var app = builder.Build();

app.UseResponseCaching();

Console.WriteLine($"[Startup] Detected OS Platform: {Environment.OSVersion.Platform}");

if (app.Environment.IsDevelopment())
{
    // Apply migrations and seed in dev to keep DB current
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
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRouting();
app.UseAuthorization();

app.MapStaticAssets();

// MVC default route (views)
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// Attribute-routed APIs (e.g., /api/assets, /api/diag)
app.MapControllers();

app.Run();
