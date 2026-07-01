namespace JobOfferMatcher.Domain.TailoredCvs;

/// <summary>
/// Lifecycle of a per-offer tailored CV (data-model §1). <see cref="Pending"/> is the state at create
/// and after every (re)generate; <see cref="Produced"/> means <b>both</b> the HTML and the rendered PDF
/// exist on disk; <see cref="Failed"/> is terminal until the user regenerates (which re-arms it). The
/// local Claude-Code worker is the sole producer — un-produced items show "pending", never a fabricated
/// CV (FR-005/FR-006). A dedicated enum (not a reuse of <c>EnrichmentState</c>) keeps Domain/TailoredCv
/// independent of Domain/Enrichment; the shapes coincide but the semantics are owned here.
/// </summary>
public enum TailoredCvState
{
    Pending,
    Produced,
    Failed,
}
