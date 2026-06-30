using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Offers;

namespace JobOfferMatcher.Domain.Tests;

/// <summary>
/// "Applied" flag unit tests: marking sets the flag plus an optional date/note and is orthogonal to
/// <see cref="UserOfferStatus"/>; the note is trimmed and length-capped in the Domain (Principle III);
/// clearing resets all three.
/// </summary>
public sealed class OfferApplicationTests
{
    private static readonly DateTimeOffset At = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);

    private static Offer NewOffer()
    {
        var content = new OfferContent
        {
            Title = "Role x",
            Company = "Acme",
            CanonicalUrl = "https://example.test/x",
            RequiredSkills = ["C#"],
            DescriptionHtml = "<p>Build.</p>",
        };
        var externalRef = ExternalRef.Create(SourceId.New(), "x", IdentityKind.NativeId).Value;
        return Offer.Create(OfferId.New(), externalRef, content, ContentFingerprint.Compute(content), At);
    }

    [Fact]
    public void New_offer_is_not_applied()
    {
        var offer = NewOffer();
        offer.Applied.ShouldBeFalse();
        offer.AppliedAt.ShouldBeNull();
        offer.ApplicationNote.ShouldBeNull();
    }

    [Fact]
    public void Mark_applied_sets_flag_date_and_trimmed_note()
    {
        var offer = NewOffer();

        var result = offer.MarkApplied(At, "  referred by Anna  ");

        result.IsSuccess.ShouldBeTrue();
        offer.Applied.ShouldBeTrue();
        offer.AppliedAt.ShouldBe(At);
        offer.ApplicationNote.ShouldBe("referred by Anna");
    }

    [Fact]
    public void Mark_applied_allows_omitting_date_and_note()
    {
        var offer = NewOffer();

        offer.MarkApplied(null, "   ").IsSuccess.ShouldBeTrue();

        offer.Applied.ShouldBeTrue();
        offer.AppliedAt.ShouldBeNull();
        offer.ApplicationNote.ShouldBeNull(); // a blank note normalizes to null
    }

    [Fact]
    public void Mark_applied_is_orthogonal_to_user_status_and_leaves_it_untouched()
    {
        var offer = NewOffer();
        offer.ChangeUserStatus(UserOfferStatus.Interested);

        offer.MarkApplied(At, null);

        offer.Applied.ShouldBeTrue();
        offer.UserStatus.ShouldBe(UserOfferStatus.Interested);
    }

    [Fact]
    public void Re_marking_overwrites_the_date_and_note()
    {
        var offer = NewOffer();
        offer.MarkApplied(At, "first");

        var later = At.AddDays(1);
        offer.MarkApplied(later, "second");

        offer.AppliedAt.ShouldBe(later);
        offer.ApplicationNote.ShouldBe("second");
    }

    [Fact]
    public void Over_long_note_is_rejected_and_leaves_the_offer_unchanged()
    {
        var offer = NewOffer();
        var tooLong = new string('x', Offer.MaxApplicationNoteLength + 1);

        var result = offer.MarkApplied(At, tooLong);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ApplicationNoteTooLong");
        offer.Applied.ShouldBeFalse();
    }

    [Fact]
    public void Clear_applied_resets_flag_date_and_note_and_reports_the_transition()
    {
        var offer = NewOffer();
        offer.MarkApplied(At, "note");

        offer.ClearApplied().ShouldBeTrue(); // it actually transitioned

        offer.Applied.ShouldBeFalse();
        offer.AppliedAt.ShouldBeNull();
        offer.ApplicationNote.ShouldBeNull();
    }

    [Fact]
    public void Clearing_a_never_applied_offer_is_a_no_op_so_no_spurious_event_is_logged()
    {
        var offer = NewOffer();

        offer.ClearApplied().ShouldBeFalse(); // nothing to clear → caller skips the timeline event

        offer.Applied.ShouldBeFalse();
    }
}
