using AIMS.Data;
using AIMS.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddEndpointsApiExplorer(); // take out after development
builder.Services.AddSwaggerGen(); // take out after development


builder.Services.AddDbContext<AimsDbContext>(options =>
{
    var cs = builder.Configuration.GetConnectionString(
        builder.Environment.IsDevelopment() &&
        Environment.OSVersion.Platform == PlatformID.Win32NT
            ? "DefaultConnection"   // LocalDB on Windows
            : "DockerConnection"    // Docker SQL on Mac/Linux
    );
    options.UseSqlServer(cs);
});

// Optional feature flag for prod seeding (defaults false)
var allowProdSeed = builder.Configuration.GetValue<bool>("AllowProdSeed", false);
// See https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection#service-lifetimes
// for dependency injection scopes
builder.Services.AddScoped<UserQuery>();
builder.Services.AddScoped<AssignmentsQuery>();
builder.Services.AddScoped<HardwareQuery>();
builder.Services.AddScoped<SoftwareQuery>();

// TODO: Take out when development is over
// Add CORS services
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocalhost", policy =>
    {
        policy.WithOrigins("http://localhost:5119") // Add your frontend's origin
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});
// Optional feature flag for prod seeding (defaults false)
var allowProdSeed = builder.Configuration.GetValue<bool>("AllowProdSeed", false);

var app = builder.Build();

// Log detected OS
Console.WriteLine($"[Startup] Detected OS Platform: {Environment.OSVersion.Platform}");

if (app.Environment.IsDevelopment())
{
    // Seed on startup (idempotent)
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AimsDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

    Console.WriteLine("[Startup] Running database seeder...");
    await DbSeeder.SeedAsync(db, allowProdSeed: allowProdSeed, logger: logger);
    Console.WriteLine("[Startup] Seeding complete.");
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger(); // take out after development
    app.UseSwaggerUI(); //used for testing APIs. Swagger UI will be available at /swagger/index.html
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
    app.UseHttpsRedirection();

    // TODO: Take out when development is over
    // Use CORS middleware
    app.UseCors("AllowLocalhost");
}

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();