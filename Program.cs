using Microsoft.EntityFrameworkCore;
using AssetTrackingSystem.Data;
using AssetTrackingSystem.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

builder.Services.AddDbContext<AssetDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AssetDbContext>();
    if (!db.Assets.Any())
    {
        db.Assets.Add(new Asset
        {
            Name = "Test Asset",
            Type = "Type A",
            Status = "Active",
            Team = "Team X"
        });
        db.SaveChanges();
    }
}

app.MapRazorPages();

app.Run();

