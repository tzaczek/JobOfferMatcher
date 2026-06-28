using JobOfferMatcher.Infrastructure.Sources.JustJoinIt;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JobOfferMatcher.Infrastructure.Tests;

/// <summary>
/// Boots the real ASP.NET Core host against the Testcontainers Postgres, with the live justjoin.it
/// transport swapped for a deterministic fixture client (offline, Principle V/VI).
/// </summary>
public sealed class JobApiFactory(string connectionString, IJustJoinItClient fakeClient) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:AppDb", connectionString);
        // Tests trigger scans explicitly — keep the background scheduler off.
        builder.UseSetting("Scheduler:Enabled", "false");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IJustJoinItClient>();
            services.AddSingleton(fakeClient);
        });
    }
}
