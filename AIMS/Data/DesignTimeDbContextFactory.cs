using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AIMS.Data
{
    public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AimsDbContext>
    {
        public AimsDbContext CreateDbContext(string[] args)
        {
            // Detect OS
            bool isWindows = OperatingSystem.IsWindows();
            bool isContainer = File.Exists("/.dockerenv");

            // Find the project folder that has appsettings.json
            var cwd = Directory.GetCurrentDirectory();
            var aimsPath = Directory.Exists(Path.Combine(cwd, "AIMS")) ? Path.Combine(cwd, "AIMS") : cwd;
            var settingsRoot = File.Exists(Path.Combine(aimsPath, "appsettings.json")) ? aimsPath : cwd;

            // Env (ASPNETCORE_ENVIRONMENT has priority, fallback to DOTNET_ENVIRONMENT, default Development)
            var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                      ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                      ?? "Development";

            // Build config
            var cfg = new ConfigurationBuilder()
                .SetBasePath(settingsRoot)
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{env}.json", optional: true)
                .AddEnvironmentVariables() // supports ConnectionStrings__Name
                .Build();

            // 1) CLI override: dotnet ef ... --connection "<CS>"
            string? cliConn = null;
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], "--connection", StringComparison.OrdinalIgnoreCase))
                    cliConn = args[i + 1];

            // 2) Env overrides (great for mac devs)
            string? envConn =
                   Environment.GetEnvironmentVariable("ConnectionStrings__Override")
                ?? Environment.GetEnvironmentVariable("ConnectionStrings__CliConnection")
                ?? Environment.GetEnvironmentVariable("ConnectionStrings__DockerConnection")
                ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
                ?? Environment.GetEnvironmentVariable("ConnectionStrings__LocalConnection");

            // 3) Config (JSON) options
            string? jsonCompose = cfg.GetConnectionString("ComposeConnection");
            string? jsonDocker = cfg.GetConnectionString("DockerConnection");
            string? jsonLocal = cfg.GetConnectionString("LocalConnection");
            string? jsonDefault = cfg.GetConnectionString("DefaultConnection");
            string? jsonCli = cfg.GetConnectionString("CliConnection");

            // Choose by OS/container with sensible priority
            // Order of preference:
            //   CLI > Env Override > Compose (if in container) > Docker (non-Windows) > LocalDB (Windows) > Default > Cli
            string? chosen =
                   cliConn
                ?? envConn
                ?? (isContainer ? jsonCompose : null)
                ?? (!isWindows ? jsonDocker : null)
                ?? (isWindows ? jsonLocal : null)
                ?? jsonDefault
                ?? jsonCli;

            if (string.IsNullOrWhiteSpace(chosen))
            {
                throw new InvalidOperationException(
                    "No SQL connection string was resolved for design-time AimsDbContext.\n" +
                    "Supply one via:\n" +
                    "  1) CLI:   dotnet ef database update --connection \"Server=localhost,1433;Database=AIMS;User Id=sa;Password=...;TrustServerCertificate=True;\"\n" +
                    "  2) ENV:   export ConnectionStrings__Override=\"<connection-string>\"\n" +
                    "  3) JSON:  appsettings.Development.json -> ConnectionStrings.DockerConnection / LocalConnection / DefaultConnection\n"
                );
            }

            var options = new DbContextOptionsBuilder<AimsDbContext>()
                .UseSqlServer(chosen)
                .EnableSensitiveDataLogging()
                .Options;

            return new AimsDbContext(options);
        }
    }
}
