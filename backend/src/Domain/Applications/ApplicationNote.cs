using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Domain.Applications;

/// <summary>
/// One dated entry in an application's append-only journal (data-model §3, FR-006). Immutable once
/// created — earlier entries are never rewritten. Carries <see cref="OfferId"/> (FK → the application).
/// </summary>
public sealed class ApplicationNote
{
    /// <summary>Longest note body the Domain accepts.</summary>
    public const int MaxBodyLength = 4000;

    public ApplicationNoteId Id { get; private set; }
    public OfferId OfferId { get; private set; }
    public string Body { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    private ApplicationNote()
    {
        // EF Core materialization.
    }

    public static Result<ApplicationNote> Create(OfferId offerId, string body, DateTimeOffset createdAt)
    {
        var trimmed = string.IsNullOrWhiteSpace(body) ? string.Empty : body.Trim();
        if (trimmed.Length == 0)
        {
            return new Error("NoteBodyRequired", "A note cannot be empty.");
        }

        if (trimmed.Length > MaxBodyLength)
        {
            return new Error("NoteTooLong", $"A note cannot exceed {MaxBodyLength} characters.");
        }

        return new ApplicationNote
        {
            Id = ApplicationNoteId.New(),
            OfferId = offerId,
            Body = trimmed,
            CreatedAt = createdAt,
        };
    }
}
