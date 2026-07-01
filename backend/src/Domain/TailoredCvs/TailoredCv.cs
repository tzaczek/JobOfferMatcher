using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Domain.TailoredCvs;

/// <summary>
/// The opt-in, latest-only tailored-CV satellite for one offer (data-model §2) — 1:1 with
/// <see cref="Offer"/> (PK = <see cref="OfferId"/>). Unlike the 002 satellites a row exists <b>only</b>
/// once the user has requested a tailored CV (no eager materialisation, no backfill). It holds the exact
/// editable <see cref="Prompt"/> used (FR-003/SC-003), the selected <see cref="EmphasisedSkills"/>, the
/// provenance <see cref="SourceCvId"/>, and a monotonic <see cref="GenerationVersion"/> supersede guard
/// (ADR-4) instead of 002's input-hash — generation is user-driven, not auto-invalidated. The worker
/// produces tailored HTML; the backend renders it to a PDF and only then is the CV <see cref="State"/>
/// <c>Produced</c> (both <see cref="HtmlFileName"/> and <see cref="PdfFileName"/> set).
/// </summary>
public sealed class TailoredCv
{
    private IReadOnlyList<string> _emphasisedSkills = [];

    public OfferId OfferId { get; private set; }
    public CvId SourceCvId { get; private set; }
    public TailoredCvState State { get; private set; }
    public int Attempts { get; private set; }

    /// <summary>Bumped on every (re)generate; the worker echoes it; a write-back is accepted only when it matches (ADR-4).</summary>
    public int GenerationVersion { get; private set; }

    /// <summary>The exact instruction text used (editable; stored verbatim as used — FR-003/SC-003).</summary>
    public string Prompt { get; private set; } = string.Empty;
    public IReadOnlyList<string> EmphasisedSkills => _emphasisedSkills;
    public string? HtmlFileName { get; private set; }
    public string? PdfFileName { get; private set; }
    public DateTimeOffset? GeneratedAt { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private TailoredCv()
    {
        // EF Core materialization.
    }

    public static TailoredCv CreateRequest(
        OfferId offer, CvId sourceCv, string prompt, IReadOnlyList<string> emphasisedSkills, DateTimeOffset at) => new()
    {
        OfferId = offer,
        SourceCvId = sourceCv,
        Prompt = prompt,
        _emphasisedSkills = [.. emphasisedSkills],
        State = TailoredCvState.Pending,
        Attempts = 0,
        GenerationVersion = 1,
        CreatedAt = at,
    };

    /// <summary>Supersede: re-arm to Pending, reset attempts, bump the version, and replace prompt/skills/source CV.</summary>
    public void RequestRegeneration(
        CvId sourceCv, string prompt, IReadOnlyList<string> emphasisedSkills, DateTimeOffset at)
    {
        SourceCvId = sourceCv;
        Prompt = prompt;
        _emphasisedSkills = [.. emphasisedSkills];
        State = TailoredCvState.Pending;
        Attempts = 0;
        GenerationVersion++;
        LastError = null;
        // The previously produced files (if any) stay on disk until a new produce overwrites them, but
        // they are no longer current: State==Pending means they are not shown/downloadable.
        _ = at;
    }

    /// <summary>The supersede guard: a write-back is honoured only for the current version of a still-pending row.</summary>
    public bool Accepts(int generationVersion) =>
        State == TailoredCvState.Pending && generationVersion == GenerationVersion;

    /// <summary>Both files now exist on disk → Produced. Guarded against a stale/non-pending write (programmer error).</summary>
    public void MarkProduced(int generationVersion, string htmlFileName, string pdfFileName, DateTimeOffset at)
    {
        EnsureAccepts(generationVersion);
        HtmlFileName = htmlFileName;
        PdfFileName = pdfFileName;
        GeneratedAt = at;
        State = TailoredCvState.Produced;
        Attempts = 0;
        LastError = null;
    }

    /// <summary>A failed attempt (worker-failed or render error). Flips to terminal Failed at the retry limit; else stays Pending.</summary>
    public void RecordFailure(int generationVersion, string? error, int retryLimit)
    {
        EnsureAccepts(generationVersion);
        Attempts++;
        LastError = error;
        if (Attempts >= retryLimit)
        {
            State = TailoredCvState.Failed;
        }
    }

    private void EnsureAccepts(int generationVersion)
    {
        if (!Accepts(generationVersion))
        {
            throw new InvalidOperationException(
                $"Write-back for a stale or non-pending tailored CV (echoed version {generationVersion} vs current {GenerationVersion}, state {State}). Check Accepts() first.");
        }
    }
}
