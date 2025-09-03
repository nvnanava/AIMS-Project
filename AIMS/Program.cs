using AIMS.Data;
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

var app = builder.Build();

app.UseSwagger(); // take out after development
app.UseSwaggerUI(); //used for testing APIs. Swagger UI will be available at /swagger/index.html

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();