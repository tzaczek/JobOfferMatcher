using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Domain.Applications;

/// <summary>
/// A logged interaction with the employer/recruiter (data-model §3, FR-011): when it happened, its
/// direction, a free-text channel (email/phone/LinkedIn…), and a short summary. <c>Channel</c> is
/// deliberately free-text (no fixed catalog). Carries <see cref="OfferId"/>.
/// </summary>
public sealed class ApplicationCommunication
{
    public const int MaxChannelLength = 80;
    public const int MaxSummaryLength = 4000;

    public ApplicationCommunicationId Id { get; private set; }
    public OfferId OfferId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public CommunicationDirection Direction { get; private set; }
    public string Channel { get; private set; } = string.Empty;
    public string Summary { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    private ApplicationCommunication()
    {
        // EF Core materialization.
    }

    public static Result<ApplicationCommunication> Create(
        OfferId offerId,
        DateTimeOffset occurredAt,
        CommunicationDirection direction,
        string channel,
        string summary,
        DateTimeOffset now)
    {
        var trimmedChannel = string.IsNullOrWhiteSpace(channel) ? string.Empty : channel.Trim();
        if (trimmedChannel.Length == 0)
        {
            return new Error("ChannelRequired", "A communication channel is required.");
        }

        if (trimmedChannel.Length > MaxChannelLength)
        {
            return new Error("ChannelTooLong", $"A channel cannot exceed {MaxChannelLength} characters.");
        }

        var trimmedSummary = string.IsNullOrWhiteSpace(summary) ? string.Empty : summary.Trim();
        if (trimmedSummary.Length == 0)
        {
            return new Error("SummaryRequired", "A communication summary is required.");
        }

        if (trimmedSummary.Length > MaxSummaryLength)
        {
            return new Error("SummaryTooLong", $"A summary cannot exceed {MaxSummaryLength} characters.");
        }

        return new ApplicationCommunication
        {
            Id = ApplicationCommunicationId.New(),
            OfferId = offerId,
            OccurredAt = occurredAt,
            Direction = direction,
            Channel = trimmedChannel,
            Summary = trimmedSummary,
            CreatedAt = now,
        };
    }
}
