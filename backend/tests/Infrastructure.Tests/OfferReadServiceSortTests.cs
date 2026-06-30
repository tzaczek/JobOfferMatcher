using JobOfferMatcher.Application.Offers;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Enrichment;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Infrastructure.Persistence;
using JobOfferMatcher.Infrastructure.Tests.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace JobOfferMatcher.Infrastructure.Tests;

/// <summary>
/// US1 regression (T026): the read-model/two-tier-sort rework (feature 002) did NOT change the
/// <c>published</c> sort — offers still come newest-first with date-less ones last (FR-013 / SC US1).
/// Verification-only: no new behavior.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OfferReadServiceSortTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Published_sort_is_newest_first_with_date_less_offers_last()
    {
        await postgres.ResetAsync();
        await using var factory = new JobApiFactory(postgres.ConnectionString, new MutableJustJoinItClient());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var newest = OfferWith("newest", new DateTimeOffset(2026, 6, 28, 0, 0, 0, TimeSpan.Zero));
        var older = OfferWith("older", new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero));
        var dateless = OfferWith("dateless", publishedAt: null);
        db.Offers.AddRange(older, dateless, newest);
        // Satellites are an invariant; create them so the read path mirrors production exactly.
        foreach (var o in new[] { newest, older, dateless })
        {
            db.OfferEnrichments.Add(OfferEnrichment.CreatePending(o.Id));
            db.OfferFits.Add(OfferFit.CreatePending(o.Id));
        }

        await db.SaveChangesAsync();

        var read = scope.ServiceProvider.GetRequiredService<IOfferReadService>();
        var result = await read.ListAsync(new OfferListFilter
        {
            Sort = OfferSort.Published,
            Availability = AvailabilityFilter.All,
            Status = OfferStatusFilter.All,
        });

        result.Data.Select(d => d.Title).ShouldBe(["Role newest", "Role older", "Role dateless"]);
        // The card date still renders (US1): a published offer carries its date.
        result.Data.First().PublishedAt.ShouldNotBeNull();
    }

    private static Offer OfferWith(string nativeKey, DateTimeOffset? publishedAt)
    {
        var content = new OfferContent
        {
            Title = $"Role {nativeKey}",
            Company = "Acme",
            CanonicalUrl = $"https://example.test/{nativeKey}",
            RequiredSkills = ["C#"],
            PublishedAt = publishedAt,
        };
        var externalRef = ExternalRef.Create(SourceId.New(), nativeKey, IdentityKind.NativeId).Value;
        return Offer.Create(OfferId.New(), externalRef, content, ContentFingerprint.Compute(content), DateTimeOffset.UtcNow);
    }
}
