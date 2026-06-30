using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Domain.Enrichment;

/// <summary>
/// The AI fit satellite for one offer (data-model §5) — 1:1 with <see cref="Offer"/> (PK =
/// <see cref="OfferId"/>). Standalone so the worker writes it without loading the offer aggregate.
/// <b><see cref="Score"/> is only ever displayed when <see cref="State"/>==Produced and the stored
/// <see cref="InputsHash"/> equals the current one</b> — otherwise the UI shows pending/failed;
/// never a non-AI fallback (FR-005).
/// </summary>
public sealed class OfferFit
{
    private IReadOnlyList<string> _matched = [];
    private IReadOnlyList<string> _missing = [];

    public OfferId OfferId { get; private set; }
    public EnrichmentState State { get; private set; }
    public int Attempts { get; private set; }
    public int? Score { get; private set; }
    public IReadOnlyList<string> Matched => _matched;
    public IReadOnlyList<string> Missing => _missing;
    public string? Rationale { get; private set; }
    public string? InputsHash { get; private set; }
    public DateTimeOffset? ProducedAt { get; private set; }
    public string? LastError { get; private set; }

    private OfferFit()
    {
        // EF Core materialization.
    }

    public static OfferFit CreatePending(OfferId offerId) => new()
    {
        OfferId = offerId,
        State = EnrichmentState.Pending,
        Attempts = 0,
    };

    public void MarkProduced(
        int score, IReadOnlyList<string> matched, IReadOnlyList<string> missing,
        string? rationale, string inputsHash, DateTimeOffset at)
    {
        Score = score;
        _matched = [.. matched];
        _missing = [.. missing];
        Rationale = rationale;
        InputsHash = inputsHash;
        ProducedAt = at;
        State = EnrichmentState.Produced;
        Attempts = 0;
        LastError = null;
    }

    public void RecordFailure(string? error, int retryLimit)
    {
        Attempts++;
        LastError = error;
        if (Attempts >= retryLimit)
        {
            State = EnrichmentState.Failed;
        }
    }

    public void Invalidate()
    {
        State = EnrichmentState.Pending;
        Attempts = 0;
    }

    public void Rearm()
    {
        if (State == EnrichmentState.Failed)
        {
            State = EnrichmentState.Pending;
            Attempts = 0;
        }
    }

    public void ForcePending()
    {
        State = EnrichmentState.Pending;
        Attempts = 0;
    }
}
