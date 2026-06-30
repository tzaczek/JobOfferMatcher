using System.Net;
using JobOfferMatcher.Infrastructure.Sources.JustJoinIt;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JobOfferMatcher.Infrastructure.Tests;

/// <summary>
/// Boots the real ASP.NET Core host against the Testcontainers Postgres, with the live justjoin.it
/// transport swapped for a deterministic fixture client (offline, Principle V/VI). By default it
/// simulates a loopback connection so the fail-closed <c>/api/enrichment/*</c> guard admits the test
/// client (TestServer leaves RemoteIpAddress null); pass <c>simulateLoopback: false</c> to assert the
/// guard's 403 behavior.
/// </summary>
public sealed class JobApiFactory(
    string connectionString,
    IJustJoinItClient fakeClient,
    bool simulateLoopback = true,
    IReadOnlyDictionary<string, string?>? settings = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:AppDb", connectionString);
        // Tests trigger scans explicitly — keep the background scheduler off.
        builder.UseSetting("Scheduler:Enabled", "false");

        // Extra config (e.g. Cv:StoragePath / Backup:StoragePath) so a test can isolate on-disk dirs.
        if (settings is not null)
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IJustJoinItClient>();
            services.AddSingleton(fakeClient);

            if (simulateLoopback)
            {
                services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter>(new LoopbackRemoteIpStartupFilter());
            }
        });
    }
}

/// <summary>Inserts front-of-pipeline middleware that stamps a loopback remote IP (TestServer sets none).</summary>
internal sealed class LoopbackRemoteIpStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            context.Connection.RemoteIpAddress ??= IPAddress.Loopback;
            await nextMiddleware();
        });
        next(app);
    };
}
