using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace AbroadQs.Bot.Data;

/// <summary>
/// Used by EF Core tools (e.g. dotnet ef migrations) to create DbContext at design time.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var basePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "AbroadQs.Bot.Host.Webhook");
        var config = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
        var connStr = config.GetConnectionString("DefaultConnection")
            ?? "Server=localhost,1433;Database=AbroadQsBot;User Id=sa;Password=YourStrong@Pass123;TrustServerCertificate=True;";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connStr)
            .Options;
        return new ApplicationDbContext(options);
    }
}
