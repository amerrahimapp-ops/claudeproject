using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Api.Data;

/// <summary>
/// Design-time factory used by `dotnet ef migrations add/...`. Uses a fixed
/// MySqlServerVersion instead of ServerVersion.AutoDetect so migrations can
/// be authored/scaffolded without a live database connection. Runtime
/// (Program.cs) still uses AutoDetect against the real server.
/// </summary>
public class CapacityDbContextFactory : IDesignTimeDbContextFactory<CapacityDbContext>
{
    public CapacityDbContext CreateDbContext(string[] args)
    {
        // Design-time only (dotnet ef ...), never used at runtime. Matches
        // docker-compose.yml's local dev MySQL by default; override via
        // CAPACITY_DESIGNTIME_CONNECTION_STRING if your local setup differs.
        var connectionString = Environment.GetEnvironmentVariable("CAPACITY_DESIGNTIME_CONNECTION_STRING")
            ?? "Server=localhost;Port=3306;Database=capacity_dev;User=root;Password=dev_root_password;";
        var optionsBuilder = new DbContextOptionsBuilder<CapacityDbContext>();
        optionsBuilder.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 0)));

        return new CapacityDbContext(optionsBuilder.Options);
    }
}
