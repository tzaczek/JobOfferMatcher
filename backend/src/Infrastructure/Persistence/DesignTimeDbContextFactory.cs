using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobOfferMatcher.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations</c> build the context without running the web host. The runtime
/// connection string lives in user-secrets (read by the host); design-time only needs a valid
/// string to construct the model, so an env override or a localhost default suffices.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connection =
            Environment.GetEnvironmentVariable("ConnectionStrings__AppDb")
            ?? "Host=localhost;Port=5432;Database=jobs;Username=jobs;Password=postgres";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connection, npg => npg.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name))
            .Options;

        return new AppDbContext(options);
    }
}
