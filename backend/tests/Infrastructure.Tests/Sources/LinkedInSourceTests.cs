using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.Scans;
using JobOfferMatcher.Domain.Sources;
using JobOfferMatcher.Infrastructure.Sources;
using JobOfferMatcher.Infrastructure.Sources.LinkedIn;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobOfferMatcher.Infrastructure.Tests.Sources;

/// <summary>
/// Feature 008, US1 (T017): <see cref="LinkedInSource.CollectAsync"/> on a valid session streams each
/// recommended card to <c>onOffer</c> mapped by LinkedIn job id, and the login call carries the scan's
/// attended-ness (Manual → interactive; Scheduled → not) read from the scoped <see cref="ScanContext"/>.
/// Uses a fake <see cref="ILinkedInClient"/> — never live LinkedIn (Principles V/VI).
/// </summary>
public sealed class LinkedInSourceTests
{
    private static readonly SourceId Source = SourceId.From(new Guid("44444444-4444-4444-4444-444444444444"));

    private static LinkedInSource Build(FakeLinkedInClient client, TriggerType trigger)
    {
        var context = new ScanContext();
        context.Begin(ScanRunId.New(), trigger);
        return new LinkedInSource(Source, client, context, Options.Create(new LinkedInOptions()), NullLogger<LinkedInSource>.Instance);
    }

    [Fact]
    public async Task Recommended_pass_streams_each_card_mapped_by_job_id()
    {
        var client = new FakeLinkedInClient();
        client.SetRecommended(
            SourceFetchStatus.Ok,
            FakeLinkedInClient.Card("4428922336", title: "Senior .NET Engineer", company: "Acme", location: "Kraków (Remote)", mode: WorkMode.Remote),
            FakeLinkedInClient.Card("4428922337", title: "Backend Dev", company: "Globex"));
        var source = Build(client, TriggerType.Manual);

        var collected = new List<CollectedOffer>();
        var result = await source.CollectAsync(
            new JobSourceSearch { IncludeRecommended = true },
            (offer, _) => { collected.Add(offer); return Task.CompletedTask; },
            CancellationToken.None);

        result.Outcome.ShouldBe(ScanOutcome.Complete);
        result.CollectedCount.ShouldBe(2);
        collected.Count.ShouldBe(2);

        var first = collected.Single(o => o.ExternalRef.NativeKey == "4428922336");
        first.ExternalRef.Kind.ShouldBe(IdentityKind.NativeId);
        first.Content.Title.ShouldBe("Senior .NET Engineer");
        first.Content.Company.ShouldBe("Acme");
        first.Content.WorkMode.ShouldBe(WorkMode.Remote);
        first.Content.CanonicalUrl.ShouldBe("https://www.linkedin.com/jobs/view/4428922336/");
        first.Content.SalaryBands.ShouldBeEmpty(); // LinkedIn rarely discloses → "not available"
    }

    [Fact]
    public async Task Manual_scan_attempts_interactive_login()
    {
        var client = new FakeLinkedInClient();
        var source = Build(client, TriggerType.Manual);

        await source.CollectAsync(new JobSourceSearch { IncludeRecommended = true }, (_, _) => Task.CompletedTask, CancellationToken.None);

        client.LoginCalls.ShouldBe(1);
        client.LastInteractive.ShouldBe(true); // Manual → attended login allowed
    }

    [Fact]
    public async Task Scheduled_scan_does_not_allow_interactive_login()
    {
        var client = new FakeLinkedInClient();
        var source = Build(client, TriggerType.Scheduled);

        await source.CollectAsync(new JobSourceSearch { IncludeRecommended = true }, (_, _) => Task.CompletedTask, CancellationToken.None);

        client.LastInteractive.ShouldBe(false); // Scheduled → unattended, no window
    }

    [Fact]
    public async Task Failed_login_records_login_not_completed_and_collects_nothing()
    {
        var client = new FakeLinkedInClient { LoginResult = _ => false };
        client.SetRecommended(SourceFetchStatus.Ok, FakeLinkedInClient.Card("1"));
        var source = Build(client, TriggerType.Scheduled);

        var collected = new List<CollectedOffer>();
        var result = await source.CollectAsync(
            new JobSourceSearch { IncludeRecommended = true },
            (offer, _) => { collected.Add(offer); return Task.CompletedTask; },
            CancellationToken.None);

        result.Outcome.ShouldBe(ScanOutcome.Failed);
        result.Reason.ShouldBe(IncompleteReason.LoginNotCompleted);
        collected.ShouldBeEmpty();
    }

    [Fact]
    public async Task Fetch_body_delegates_to_the_client_by_job_id()
    {
        var client = new FakeLinkedInClient();
        client.SetBody("4428922336", "<p>Real requirements.</p>");
        var source = Build(client, TriggerType.Manual);

        var card = FakeLinkedInClient.Card("4428922336");
        var offer = LinkedInMapperProbe.Map(card, Source);
        var body = await source.FetchBodyAsync(offer, CancellationToken.None);

        body.ShouldBe("<p>Real requirements.</p>");
    }
}

/// <summary>Exposes the internal mapper to build a <c>CollectedOffer</c> for the body-fetch test.</summary>
internal static class LinkedInMapperProbe
{
    public static CollectedOffer Map(LinkedInJobCard card, SourceId source)
    {
        var content = new OfferContent
        {
            Title = card.Title,
            Company = card.Company,
            Location = card.Location,
            WorkMode = card.WorkMode,
            CanonicalUrl = card.CanonicalUrl,
        };
        var externalRef = ExternalRef.Create(source, card.JobId, IdentityKind.NativeId).Value;
        return new CollectedOffer(externalRef, content);
    }
}
