using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace AIMS.Data
{
    
    /*
        Lets 'dotnet ef' create AimsDbContext without booting the whole app.
        We choose the right connection string based on environment and whether
        we’re running inside a container.
    */
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AimsDbContext>
    {
        public AimsDbContext CreateDbContext(string[] args)
        {
            // Load config similarly to Program.cs
            var env = System.Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                      ?? "Development";

            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{env}.json", optional: true)
                .AddEnvironmentVariables();

            var config = builder.Build();

            var isContainer = File.Exists("/.dockerenv"); // present inside Docker
            var isWindows = System.Environment.OSVersion.Platform == PlatformID.Win32NT;

            // Choose the right CS for CLI:
            // - In Docker: use service name (sqlserver-dev)
            // - On Windows host: LocalDB
            // - On Mac/Linux host: localhost:1433
            var cs =
                isContainer
                    ? config.GetConnectionString("DockerConnection")
                    : (isWindows
                        ? config.GetConnectionString("DefaultConnection")
                        : config.GetConnectionString("CliConnection")); // localhost

            var options = new DbContextOptionsBuilder<AimsDbContext>()
                .UseSqlServer(cs)
                .Options;

            return new AimsDbContext(options);
        }
    }
}