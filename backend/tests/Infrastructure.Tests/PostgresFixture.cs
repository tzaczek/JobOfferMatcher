using JobOfferMatcher.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace JobOfferMatcher.Infrastructure.Tests;

/// <summary>
/// Spins a throwaway real PostgreSQL 17 container for integration tests (Constitution Principle V
/// — the DB is never mocked). Migrations are applied once on start; <see cref="ResetAsync"/>
/// truncates between tests for isolation. Shared across a test collection so the container starts once.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("jobs_test")
        .WithUsername("jobs")
        .WithPassword("jobs")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>A fresh context bound to the container — caller owns disposal.</summary>
    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>Truncate all data tables (keeps schema + __EFMigrationsHistory) for test isolation.</summary>
    public async Task ResetAsync()
    {
        await using var db = CreateContext();
        var tables = await db.Database
            .SqlQueryRaw<string>(
                "SELECT tablename FROM pg_tables WHERE schemaname = 'public' AND tablename <> '__EFMigrationsHistory'")
            .ToListAsync();

        if (tables.Count == 0)
        {
            return;
        }

        var quoted = string.Join(", ", tables.Select(t => $"\"{t}\""));
        await db.Database.ExecuteSqlRawAsync($"TRUNCATE {quoted} RESTART IDENTITY CASCADE;");
    }
}

/// <summary>xUnit collection so integration tests share one container (and its startup cost).</summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
