using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Sources;
using JobOfferMatcher.Infrastructure.Persistence.Seed;
using JobOfferMatcher.Infrastructure.Sources.Browser;
using JobOfferMatcher.Infrastructure.Sources.Generic;
using JobOfferMatcher.Infrastructure.Sources.JustJoinIt;
using Microsoft.Extensions.DependencyInjection;

namespace JobOfferMatcher.Infrastructure.Sources;

/// <summary>
/// Resolves the adapter for a configured source (FR-003). The well-known justjoin.it source uses its
/// dedicated client; any OTHER DirectApi source routes to the generic scaffold — so a second source
/// is added by config without touching the orchestrator, ranking, or feed.
/// </summary>
internal sealed class JobSourceFactory(IServiceProvider services) : IJobSourceFactory
{
    public IJobSource Create(JobSource source) => source.Kind switch
    {
        SourceKind.DirectApi when source.Id == DatabaseSeeder.DefaultJustJoinItSourceId =>
            ActivatorUtilities.CreateInstance<JustJoinItSource>(services, source.Id),
        SourceKind.DirectApi =>
            ActivatorUtilities.CreateInstance<GenericDirectApiSource>(services, source.Id),
        SourceKind.InteractiveBrowser =>
            ActivatorUtilities.CreateInstance<NotConfiguredInteractiveBrowserSource>(services, source.Id),
        _ => throw new NotSupportedException($"Unsupported source kind: {source.Kind}"),
    };
}
