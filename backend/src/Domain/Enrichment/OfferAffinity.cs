using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Domain.Enrichment;

/// <summary>
/// The AI affinity satellite for one offer (data-model §1, feature 006) — an <see cref="OfferFit"/>
/// twin, 1:1 with <see cref="Offer"/> (PK = <see cref="OfferId"/>). Standalone so the worker writes it
/// without loading the offer aggregate. Affinity scores how closely an offer resembles the offers the
/// user has already <b>applied to</b> (the basis), a DISTINCT second signal beside CV fit (FR-003).
/// <b><see cref="Score"/> is only ever displayed when <see cref="State"/>==Produced, the stored
/// <see cref="InputsHash"/> equals the current one, AND at least <see cref="MinApplications"/>
/// applications exist</b> — otherwise the read path shows pending/failed/insufficient; never a
/// fabricated fallback (FR-009).
/// </summary>
public sealed class OfferAffinity
{
    /// <summary>Cold-start gate (clarification #4): affinity is only produced/shown at or above this applied-offer count.</summary>
    public const int MinApplications = 3;

    private IReadOnlyList<string> _resembles = [];

    public OfferId OfferId { get; private set; }
    public EnrichmentState State { get; private set; }
    public int Attempts { get; private set; }
    public int? Score { get; private set; }

    /// <summary>Which applied roles/attributes this offer is close to (the fit <c>Matched</c> analogue).</summary>
    public IReadOnlyList<string> Resembles => _resembles;
    public string? Rationale { get; private set; }
    public string? InputsHash { get; private set; }
    public DateTimeOffset? ProducedAt { get; private set; }
    public string? LastError { get; private set; }

    private OfferAffinity()
    {
        // EF Core materialization.
    }

    public static OfferAffinity CreatePending(OfferId offerId) => new()
    {
        OfferId = offerId,
        State = EnrichmentState.Pending,
        Attempts = 0,
    };

    public void MarkProduced(
        int score, IReadOnlyList<string> resembles, string? rationale, string inputsHash, DateTimeOffset at)
    {
        Score = score;
        _resembles = [.. resembles];
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
