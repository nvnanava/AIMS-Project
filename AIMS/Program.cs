using AIMS.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Microsoft.Extensions.Options;

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

builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme) // Configure the app to use OpenID Connect authentication with Azure AD
//.AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd")); // Bind the Azure AD settings from configuration to the authentication options
.AddMicrosoftIdentityWebApp(options =>
{
    builder.Configuration.Bind("AzureAd", options);
    options.AccessDeniedPath = "/Home/Error"; // Redirects users to a custom error page if they are denied access
    options.TokenValidationParameters.RoleClaimType = "roles"; // This  maps the "roles" claim from the token to the user's roles in the application
});

builder.Services.AddAuthorizationBuilder()
  .AddPolicy("mbcAdmin", policy => // Only users with the "Admin" role can access any action methods in this controller
    policy.RequireAssertion(context =>
        context.User.HasClaim(c =>
            c.Type == "preferred_username" &&
            new[] {
                // our test accounts for now, you should be able to login with your csus accounts.
                "niyant397@gmail.com",
                "nvnanavati@csus.edu",
                "akalustatsingh@csus.edu",
                "tburguillos@csus.edu",
                "keeratkhandpur@csus.edu",
                "suhailnajimudeen@csus.edu",
                "hkaur@csus.edu",
                "cameronlanzaro@csus.edu",
                "norinphlong@csus.edu"


            }.Contains(c.Value))))

  .AddPolicy("mbcHelpDesk", policy => //Help Desk Users, dummy names for now but you can add your email here to test.
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



builder.Services.AddRazorPages() // Add support for Razor Pages and integrate Microsoft Identity UI components
    .AddMicrosoftIdentityUI(); // Adds Razor UI pages for authentication and user management


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
    app.UseRouting();
}


app.UseAuthentication(); // Enable Azure authentication middleware
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
