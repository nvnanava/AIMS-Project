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
            // Make sure we point at the AIMS project folder even when running from solution root.
            var basePath = Directory.GetCurrentDirectory();
            var aimsPath = Path.Combine(basePath, "AIMS");
            if (File.Exists(Path.Combine(aimsPath, "appsettings.json")))
                basePath = aimsPath; // use AIMS/ if present

            var env = System.Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development";

            var cfg = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{env}.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            // Prefer docker in container, otherwise pick what exists
            var isContainer = File.Exists("/.dockerenv");
            string? cs =
                (isContainer ? cfg.GetConnectionString("DockerConnection") : null)
                ?? cfg.GetConnectionString("DockerConnection")
                ?? cfg.GetConnectionString("DefaultConnection")
                ?? cfg.GetConnectionString("CliConnection")
                ?? System.Environment.GetEnvironmentVariable("ConnectionStrings__DockerConnection")
                ?? System.Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
                ?? System.Environment.GetEnvironmentVariable("ConnectionStrings__CliConnection");

            if (string.IsNullOrWhiteSpace(cs))
            {
                throw new InvalidOperationException(
                    "DesignTimeDbContextFactory could not find a connection string. " +
                    "Looked for ConnectionStrings: DockerConnection, DefaultConnection, CliConnection " +
                    "in appsettings(.Development).json under the AIMS project folder, and in environment variables " +
                    "(ConnectionStrings__*). Set one (e.g., export ConnectionStrings__DockerConnection=...) and retry."
                );
            }

            var options = new DbContextOptionsBuilder<AimsDbContext>()
                .UseSqlServer(cs)
                .Options;

            return new AimsDbContext(options);
        }
    }
}
