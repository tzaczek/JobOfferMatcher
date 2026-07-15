using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Sources;
using JobOfferMatcher.Infrastructure.Persistence.Seed;
using JobOfferMatcher.Infrastructure.Sources.Generic;
using JobOfferMatcher.Infrastructure.Sources.JustJoinIt;
using JobOfferMatcher.Infrastructure.Sources.LinkedIn;
using JobOfferMatcher.Infrastructure.Sources.NoFluffJobs;
using JobOfferMatcher.Infrastructure.Sources.TheProtocol;
using Microsoft.Extensions.DependencyInjection;

namespace JobOfferMatcher.Infrastructure.Sources;

/// <summary>
/// Resolves the adapter for a configured source (FR-003). Each well-known DirectApi source (justjoin.it,
/// theprotocol.it, nofluffjobs.com) routes to its dedicated adapter by stable id; any OTHER DirectApi
/// source routes to the generic scaffold. Every <see cref="SourceKind.InteractiveBrowser"/> source routes
/// to <see cref="LinkedInSource"/> — LinkedIn is the sole login-gated source, so routing by kind (not a
/// per-id discriminator) supports FR-007 (user-created LinkedIn sources) without a schema change (ADR-1).
/// So a source is added by config without touching the orchestrator, ranking, or feed.
/// </summary>
internal sealed class JobSourceFactory(IServiceProvider services) : IJobSourceFactory
{
    public IJobSource Create(JobSource source) => source.Kind switch
    {
        SourceKind.DirectApi when source.Id == DatabaseSeeder.DefaultJustJoinItSourceId =>
            ActivatorUtilities.CreateInstance<JustJoinItSource>(services, source.Id),
        SourceKind.DirectApi when source.Id == DatabaseSeeder.DefaultTheProtocolSourceId =>
            ActivatorUtilities.CreateInstance<TheProtocolSource>(services, source.Id),
        SourceKind.DirectApi when source.Id == DatabaseSeeder.DefaultNoFluffJobsSourceId =>
            ActivatorUtilities.CreateInstance<NoFluffJobsSource>(services, source.Id),
        SourceKind.DirectApi =>
            ActivatorUtilities.CreateInstance<GenericDirectApiSource>(services, source.Id),
        SourceKind.InteractiveBrowser =>
            ActivatorUtilities.CreateInstance<LinkedInSource>(services, source.Id),
        _ => throw new NotSupportedException($"Unsupported source kind: {source.Kind}"),
    };
}
