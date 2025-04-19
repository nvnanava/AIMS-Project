using Microsoft.EntityFrameworkCore;
using AssetTrackingSystem.Data;
using AssetTrackingSystem.Models;

namespace AssetTrackingSystem
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

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

            app.MapGet("/", () => "Hello, Asset Tracking System is running!");

            app.Run();
        }
    }
}

