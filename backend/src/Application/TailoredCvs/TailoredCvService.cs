using JobOfferMatcher.Application.Abstractions;
using JobOfferMatcher.Application.Cv;
using JobOfferMatcher.Application.Enrichment;
using JobOfferMatcher.Application.Offers;
using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Application.Settings;
using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Cv;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.TailoredCvs;

namespace JobOfferMatcher.Application.TailoredCvs;

/// <summary>Expected (non-exceptional) failures surfaced to the UI as <c>{ error: { code, message } }</c>.</summary>
public static class TailoredCvErrors
{
    public static readonly Error OfferNotFound = new("OfferNotFound", "The offer was not found.");
    public static readonly Error NoCvOnFile = new("NoCvOnFile", "Add a CV first — there is nothing to tailor from.");
    public static readonly Error InvalidTailoredCvRequest = new("InvalidTailoredCvRequest", "The tailoring prompt must not be empty.");
    public static readonly Error TailoredCvNotFound = new("TailoredCvNotFound", "No tailored CV exists for this offer.");
    public static readonly Error TailoredCvNotReady = new("TailoredCvNotReady", "The tailored CV is not produced yet.");
}

/// <summary>
/// The tailored-CV engine (data-model §6/§7), a fourth Claude-Code worker output kind layered onto the
/// exact 002 pattern: the backend is a passive store + loopback queue and never calls AI (FR-005). It
/// composes the transparent default prompt (the worker uses the exact stored text — SC-003), serves the
/// pending queue with the source-CV path (the binary never traverses HTTP — Principle IV), and applies
/// the worker's HTML write-back: it renders the HTML to an A4 PDF (<see cref="IPdfRenderer"/>), stores
/// both files, and marks Produced — guarded by the user-driven <see cref="TailoredCv.GenerationVersion"/>
/// supersede check (ADR-4). The two write paths (<see cref="GenerateAsync"/>, <see cref="SubmitResultsAsync"/>)
/// and <see cref="DeleteAsync"/> defer through a 003 restore via <see cref="MaintenanceGate"/>.
/// </summary>
public sealed class TailoredCvService(
    ITailoredCvRepository tailored,
    ITailoredCvFileStore fileStore,
    IPdfRenderer renderer,
    IOfferReadService offerReads,
    IOfferRepository offers,
    ICvRepository cvs,
    ICvFileStore cvFiles,
    ICvTextExtractor extractor,
    ISettingsRepository settingsRepo,
    IUnitOfWork unitOfWork,
    MaintenanceGate maintenance,
    TimeProvider time)
{
    // ---- Draft (modal prefill) -----------------------------------------------------------------

    /// <summary>
    /// The prefilled, non-persisted modal contents (FR-013/FR-003). When <paramref name="skills"/> is
    /// supplied (a comma-separated subset of the offer's skills) the default prompt is recomposed for
    /// that selection (the modal's skill-toggle ⇄ prompt behaviour). Otherwise an existing tailored CV's
    /// stored prompt/skills seed the draft, else the server-composed default does.
    /// </summary>
    public async Task<Result<TailoredCvDraftView>> GetDraftAsync(OfferId offerId, string? skills = null, CancellationToken ct = default)
    {
        var detail = await offerReads.GetAsync(offerId, ct);
        if (detail is null)
        {
            return TailoredCvErrors.OfferNotFound;
        }

        var sourceCv = await ResolveSourceCvAsync(null, ct);
        if (sourceCv is null)
        {
            return TailoredCvErrors.NoCvOnFile;
        }

        var item = detail.Offer;
        var offerView = new TailoredCvOfferView(item.Title, item.Company, item.Seniority);
        var allSkills = AllOfferSkills(item);

        IReadOnlyList<string> emphasised;
        string prompt;
        if (skills is not null)
        {
            emphasised = ParseSkillSubset(skills, allSkills);
            prompt = TailoredCvPrompt.BuildDefault(offerView, emphasised);
        }
        else if (await tailored.GetByOfferAsync(offerId, ct) is { } existing)
        {
            emphasised = existing.EmphasisedSkills;
            prompt = existing.Prompt;
        }
        else
        {
            emphasised = DefaultEmphasisedSkills(item);
            prompt = TailoredCvPrompt.BuildDefault(offerView, emphasised);
        }

        return new TailoredCvDraftView(
            offerId.Value, item.Title, item.Company, prompt, emphasised, allSkills,
            new TailoredCvSourceCvView(sourceCv.Id.Value, sourceCv.FileName));
    }

    // ---- Generate / regenerate -----------------------------------------------------------------

    public async Task<Result<TailoredCvView>> GenerateAsync(OfferId offerId, GenerateTailoredCvRequest? request, CancellationToken ct = default)
    {
        // It is a DB write that must defer through a restore (FR-020, mirrors EnrichmentService).
        await maintenance.WaitWhileActiveAsync(ct);

        var offer = await offers.GetByIdAsync(offerId, ct);
        if (offer is null)
        {
            return TailoredCvErrors.OfferNotFound;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Prompt))
        {
            return TailoredCvErrors.InvalidTailoredCvRequest;
        }

        var sourceCv = await ResolveSourceCvAsync(request.SourceCvId, ct);
        if (sourceCv is null)
        {
            return TailoredCvErrors.NoCvOnFile;
        }

        var emphasised = request.EmphasisedSkills ?? [];
        var now = time.GetUtcNow();

        var row = await tailored.GetByOfferAsync(offerId, ct);
        if (row is null)
        {
            row = TailoredCv.CreateRequest(offerId, sourceCv.Id, request.Prompt, emphasised, now);
            await tailored.AddAsync(row, ct);
        }
        else
        {
            row.RequestRegeneration(sourceCv.Id, request.Prompt, emphasised, now);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return ToView(row, offer.Title, offer.Company);
    }

    // ---- Worker queue ---------------------------------------------------------------------------

    public async Task<TailoredCvPendingWork> GetPendingWorkAsync(int limit, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 50);
        var settings = await settingsRepo.GetAsync(ct);
        var retryLimit = Math.Max(1, settings.Enrichment.RetryLimit);

        var pending = await tailored.GetPendingAsync(limit, ct);
        var items = new List<TailoredCvWorkItem>(pending.Count);
        foreach (var row in pending)
        {
            var offer = await offers.GetByIdAsync(row.OfferId, ct);
            var sourceCv = await cvs.GetByIdAsync(row.SourceCvId, ct);
            if (offer is null || sourceCv is null)
            {
                continue; // offer hard-deleted / source CV removed — defensively skip (stays pending).
            }

            items.Add(new TailoredCvWorkItem(
                WorkItemId: WorkItemId(row.OfferId),
                OfferId: row.OfferId.Value,
                GenerationVersion: row.GenerationVersion,
                Prompt: row.Prompt,
                EmphasisedSkills: row.EmphasisedSkills,
                Offer: new TailoredCvOfferWire(
                    offer.Title, offer.Company, offer.Seniority, [.. offer.RequiredSkills], [.. offer.NiceToHaveSkills]),
                SourceCv: BuildSourceCvView(sourceCv)));
        }

        var counts = await tailored.GetCountsAsync(ct);
        var meta = new TailoredCvPendingMeta(counts.Pending, counts.Failed, items.Count, retryLimit);
        return new TailoredCvPendingWork(meta, items);
    }

    public async Task<TailoredCvSubmitResponse> SubmitResultsAsync(TailoredCvSubmitRequest request, CancellationToken ct = default)
    {
        // The render+persist write-back defers through a restore (FR-020, mirrors EnrichmentService).
        await maintenance.WaitWhileActiveAsync(ct);

        var results = request.Results ?? [];
        var settings = await settingsRepo.GetAsync(ct);
        var retryLimit = Math.Max(1, settings.Enrichment.RetryLimit);
        var now = time.GetUtcNow();

        var outcomes = new List<TailoredCvResultOutcome>(results.Count);
        foreach (var item in results)
        {
            outcomes.Add(await HandleResultAsync(item, retryLimit, now, ct));
        }

        await unitOfWork.SaveChangesAsync(ct);

        var accepted = outcomes.Count(o => o.Outcome == "produced");
        return new TailoredCvSubmitResponse(accepted, outcomes.Count - accepted, outcomes);
    }

    // ---- Read / list / delete -------------------------------------------------------------------

    public async Task<Result<TailoredCvView>> GetAsync(OfferId offerId, CancellationToken ct = default)
    {
        var row = await tailored.GetByOfferAsync(offerId, ct);
        if (row is null)
        {
            return TailoredCvErrors.TailoredCvNotFound;
        }

        var offer = await offers.GetByIdAsync(row.OfferId, ct);
        return ToView(row, offer?.Title ?? "", offer?.Company ?? "");
    }

    /// <summary>All tailored CVs for the dedicated page, newest produced first (then created).</summary>
    public async Task<IReadOnlyList<TailoredCvView>> ListAsync(CancellationToken ct = default)
    {
        var rows = await tailored.GetAllAsync(ct);
        var ordered = rows
            .OrderByDescending(r => r.GeneratedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(r => r.CreatedAt)
            .ToList();

        var views = new List<TailoredCvView>(ordered.Count);
        foreach (var row in ordered)
        {
            var offer = await offers.GetByIdAsync(row.OfferId, ct);
            views.Add(ToView(row, offer?.Title ?? "", offer?.Company ?? ""));
        }

        return views;
    }

    /// <summary>The produced HTML for the in-app <c>/preview</c> iframe (US1). 404 if absent; 409 if not produced.</summary>
    public async Task<Result<string>> GetPreviewHtmlAsync(OfferId offerId, CancellationToken ct = default)
    {
        var row = await tailored.GetByOfferAsync(offerId, ct);
        if (row is null)
        {
            return TailoredCvErrors.TailoredCvNotFound;
        }

        if (row.State != TailoredCvState.Produced || fileStore.GetHtml(offerId) is not { } html)
        {
            return TailoredCvErrors.TailoredCvNotReady;
        }

        return html;
    }

    /// <summary>The produced PDF's absolute path + a human-friendly download name (US3). 404 if absent; 409 if not produced.</summary>
    public async Task<Result<TailoredCvDownload>> GetDownloadAsync(OfferId offerId, CancellationToken ct = default)
    {
        var row = await tailored.GetByOfferAsync(offerId, ct);
        if (row is null)
        {
            return TailoredCvErrors.TailoredCvNotFound;
        }

        var path = fileStore.GetPdfAbsolutePath(offerId);
        if (row.State != TailoredCvState.Produced || !File.Exists(path))
        {
            return TailoredCvErrors.TailoredCvNotReady;
        }

        var offer = await offers.GetByIdAsync(row.OfferId, ct);
        var downloadName = BuildDownloadName(offer?.Company, offer?.Title);
        return new TailoredCvDownload(path, downloadName);
    }

    /// <summary>Remove the tailored CV (row + both files). Idempotent (a missing row is treated as already-gone).</summary>
    public async Task<Result> DeleteAsync(OfferId offerId, CancellationToken ct = default)
    {
        await maintenance.WaitWhileActiveAsync(ct);

        var row = await tailored.GetByOfferAsync(offerId, ct);
        if (row is null)
        {
            return Result.Success();
        }

        tailored.Remove(row);
        fileStore.Delete(offerId);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ---- Write-back per item --------------------------------------------------------------------

    private async Task<TailoredCvResultOutcome> HandleResultAsync(TailoredCvResultItem item, int retryLimit, DateTimeOffset now, CancellationToken ct)
    {
        var workItemId = item.WorkItemId ?? "";
        if (!TryParseWorkItem(workItemId, out var offerGuid))
        {
            return new TailoredCvResultOutcome(workItemId, "unknown", 0, "");
        }

        var row = await tailored.GetByOfferAsync(OfferId.From(offerGuid), ct); // tracked
        if (row is null)
        {
            return new TailoredCvResultOutcome(workItemId, "unknown", 0, "");
        }

        var version = item.GenerationVersion ?? -1;
        if (!row.Accepts(version))
        {
            // A newer regenerate happened while the worker produced this — discard (002's `stale` analogue).
            return new TailoredCvResultOutcome(workItemId, "superseded", row.Attempts, StateString(row));
        }

        if (string.Equals(item.Status, "produced", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(item.Html))
        {
            try
            {
                var pdf = await renderer.RenderA4Async(item.Html!, ct);
                var files = await fileStore.SaveAsync(row.OfferId, item.Html!, pdf, ct);
                row.MarkProduced(version, files.HtmlFileName, files.PdfFileName, now);
                return new TailoredCvResultOutcome(workItemId, "produced", row.Attempts, StateString(row));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                row.RecordFailure(version, $"render failed: {ex.Message}", retryLimit);
                return new TailoredCvResultOutcome(workItemId, "renderFailed", row.Attempts, StateString(row));
            }
        }

        row.RecordFailure(version, item.Reason ?? "worker reported failure", retryLimit);
        return new TailoredCvResultOutcome(workItemId, "failed", row.Attempts, StateString(row));
    }

    // ---- Helpers --------------------------------------------------------------------------------

    /// <summary>
    /// Source CV (data-model §5): the explicit, valid <paramref name="explicitId"/>; else the
    /// most-recently-uploaded readable CV; else the most-recent CV; else none (⇒ NoCvOnFile).
    /// </summary>
    private async Task<CandidateCv?> ResolveSourceCvAsync(Guid? explicitId, CancellationToken ct)
    {
        if (explicitId is { } id && await cvs.GetByIdAsync(CvId.From(id), ct) is { } chosen)
        {
            return chosen;
        }

        var readable = await cvs.GetReadableAsync(ct);
        if (Newest(readable) is { } readableCv)
        {
            return readableCv;
        }

        return Newest(await cvs.GetAllAsync(ct));
    }

    private static CandidateCv? Newest(IReadOnlyList<CandidateCv> cvs) =>
        cvs.OrderByDescending(c => c.ExtractedAt ?? DateTimeOffset.MinValue).FirstOrDefault();

    private CvDocumentView BuildSourceCvView(CandidateCv sourceCv)
    {
        var path = cvFiles.GetAbsolutePath(sourceCv.FileName);

        // Keep PII off the wire: when readable the worker reads the original PDF by path; only an
        // unreadable (image-only) CV gets PdfPig fallback text — null when none extracts.
        string? fallbackText = null;
        if (!sourceCv.IsReadable && File.Exists(path))
        {
            using var stream = File.OpenRead(path);
            var extracted = extractor.ExtractText(stream);
            fallbackText = extracted.IsSuccess ? extracted.Value : null;
        }

        return new CvDocumentView(path, sourceCv.FileName, sourceCv.IsReadable, fallbackText);
    }

    /// <summary>Default emphasised skills (data-model §4): produced KeySkills ∪ fit Matched/Missing; else required ∪ nice-to-have.</summary>
    private static IReadOnlyList<string> DefaultEmphasisedSkills(OfferListItem item)
    {
        var enriched = new List<string>();
        if (item.EnrichmentState == "produced")
        {
            enriched.AddRange(item.KeySkills);
        }

        if (item.Fit is { State: "produced" } fit)
        {
            enriched.AddRange(fit.Matched);
            enriched.AddRange(fit.Missing);
        }

        var dedupedEnriched = Dedup(enriched);
        return dedupedEnriched.Count > 0 ? dedupedEnriched : Dedup(item.RequiredSkills.Concat(item.NiceToHaveSkills));
    }

    /// <summary>The full toggle pool: every skill the offer/enrichment/fit surfaces, de-duplicated.</summary>
    private static IReadOnlyList<string> AllOfferSkills(OfferListItem item)
    {
        var all = item.RequiredSkills
            .Concat(item.NiceToHaveSkills)
            .Concat(item.KeySkills)
            .Concat(item.Fit?.Matched ?? [])
            .Concat(item.Fit?.Missing ?? []);
        return Dedup(all);
    }

    private static IReadOnlyList<string> ParseSkillSubset(string csv, IReadOnlyList<string> allSkills)
    {
        var canonical = allSkills.ToDictionary(s => s, s => s, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var picked = new List<string>();
        foreach (var raw in csv.Split(','))
        {
            var token = raw.Trim();
            if (token.Length > 0 && canonical.TryGetValue(token, out var canon) && seen.Add(canon))
            {
                picked.Add(canon);
            }
        }

        return picked;
    }

    private static IReadOnlyList<string> Dedup(IEnumerable<string> skills)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var skill in skills)
        {
            var trimmed = skill?.Trim();
            if (!string.IsNullOrEmpty(trimmed) && seen.Add(trimmed))
            {
                result.Add(trimmed);
            }
        }

        return result;
    }

    private static TailoredCvView ToView(TailoredCv row, string offerTitle, string company) => new(
        OfferId: row.OfferId.Value,
        OfferTitle: offerTitle,
        Company: company,
        SourceCvId: row.SourceCvId.Value,
        State: StateString(row),
        GenerationVersion: row.GenerationVersion,
        EmphasisedSkills: row.EmphasisedSkills,
        Prompt: row.Prompt,
        HasPdf: row.State == TailoredCvState.Produced && row.PdfFileName is not null,
        GeneratedAt: row.GeneratedAt,
        LastError: row.LastError);

    private static string StateString(TailoredCv row) => row.State.ToString().ToLowerInvariant();

    private static string WorkItemId(OfferId offerId) => $"tailored:{offerId.Value}";

    private static string BuildDownloadName(string? company, string? title)
    {
        var parts = new[] { "CV", company, title }.Where(p => !string.IsNullOrWhiteSpace(p));
        var raw = string.Join(" - ", parts);
        var cleaned = new string(raw.Select(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '-' : c).ToArray());
        return $"{cleaned}.pdf";
    }

    private static bool TryParseWorkItem(string workItemId, out Guid offerId)
    {
        offerId = Guid.Empty;
        var parts = workItemId.Split(':');
        return parts.Length == 2 && parts[0] == "tailored" && Guid.TryParse(parts[1], out offerId);
    }
}
