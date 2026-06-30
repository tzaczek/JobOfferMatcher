using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Domain.Enrichment;

/// <summary>
/// The AI summary/key-skills satellite for one offer (data-model §4) — 1:1 with <see cref="Offer"/>
/// (PK = <see cref="OfferId"/>). A mutable derived cache whose <b>payload</b> is written only by the
/// worker; the scan path touches only its lifecycle <see cref="State"/> (create Pending / Invalidate
/// on content change). Un-produced ⇒ the UI shows "pending", never a non-AI fallback (FR-005).
/// </summary>
public sealed class OfferEnrichment
{
    private IReadOnlyList<string> _keySkills = [];

    public OfferId OfferId { get; private set; }
    public EnrichmentState State { get; private set; }
    public int Attempts { get; private set; }
    public string? Summary { get; private set; }
    public IReadOnlyList<string> KeySkills => _keySkills;
    public string? InputsHash { get; private set; }
    public DateTimeOffset? ProducedAt { get; private set; }
    public string? LastError { get; private set; }

    private OfferEnrichment()
    {
        // EF Core materialization.
    }

    public static OfferEnrichment CreatePending(OfferId offerId) => new()
    {
        OfferId = offerId,
        State = EnrichmentState.Pending,
        Attempts = 0,
    };

    public void MarkProduced(string summary, IReadOnlyList<string> keySkills, string inputsHash, DateTimeOffset at)
    {
        Summary = summary;
        _keySkills = [.. keySkills];
        InputsHash = inputsHash;
        ProducedAt = at;
        State = EnrichmentState.Produced;
        Attempts = 0;
        LastError = null;
    }

    /// <summary>A failed attempt. Flips to terminal <see cref="EnrichmentState.Failed"/> at the retry limit; otherwise stays Pending.</summary>
    public void RecordFailure(string? error, int retryLimit)
    {
        Attempts++;
        LastError = error;
        if (Attempts >= retryLimit)
        {
            State = EnrichmentState.Failed;
        }
    }

    /// <summary>Inputs changed → re-arm to Pending (attempts reset). The last payload is kept internally as a read-path backstop but is no longer current.</summary>
    public void Invalidate()
    {
        State = EnrichmentState.Pending;
        Attempts = 0;
    }

    /// <summary>Manual re-run of a terminal Failed row (FR-009): Failed → Pending, attempts reset.</summary>
    public void Rearm()
    {
        if (State == EnrichmentState.Failed)
        {
            State = EnrichmentState.Pending;
            Attempts = 0;
        }
    }

    /// <summary>Force a full re-run (rerun scope=all): Produced → Pending, attempts reset.</summary>
    public void ForcePending()
    {
        State = EnrichmentState.Pending;
        Attempts = 0;
    }
}
