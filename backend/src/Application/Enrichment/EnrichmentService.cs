using System.Net;
using System.Text.RegularExpressions;
using JobOfferMatcher.Application.Abstractions;
using JobOfferMatcher.Application.Cv;
using JobOfferMatcher.Application.Offers;
using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Application.Settings;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Cv;
using JobOfferMatcher.Domain.Enrichment;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.Salary;
using JobOfferMatcher.Domain.Settings;

namespace JobOfferMatcher.Application.Enrichment;

/// <summary>
/// The kind-agnostic enrichment engine (data-model §7/§8): orders pending work (profile-first,
/// available-first, newest-first — FR-019), gates eligibility, and applies the worker's write-back
/// with a <b>recompute-from-live-inputs</b> stale guard + the satellite state machines. Claude is the
/// sole producer — the backend makes no AI call (FR-012/SC-005). Per-kind projection/validation for
/// summary/profile/fit live in the private builders/validators below.
/// </summary>
public sealed class EnrichmentService(
    ICvRepository cvs,
    IEnrichmentRepository enrichment,
    ISettingsRepository settingsRepo,
    ICvFileStore fileStore,
    ICvTextExtractor extractor,
    IUnitOfWork unitOfWork,
    MaintenanceGate maintenance,
    TimeProvider time)
{
    public async Task<PendingWork> GetPendingWorkAsync(int limit, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        var settings = await settingsRepo.GetAsync(ct);
        var allCvs = await cvs.GetAllAsync(ct);
        var producedCv = ProducedCv(allCvs);
        var hasProducedProfile = producedCv is not null;
        var effectiveProfileVersion = hasProducedProfile
            ? EffectiveProfile.Version(producedCv!.Profile!, settings.Preferences)
            : (InputHash?)null;

        var rows = await enrichment.GetOfferWorkRowsAsync(ct);

        // Affinity basis (006): all applied offers, weighted equally. The version is null below the
        // ≥3 cold-start gate → no affinity work is emitted/counted (mirrors "no produced profile → no fits").
        var appliedRows = rows.Where(r => r.Offer.Applied).ToList();
        var appliedBasis = appliedRows.Select(r => (r.Offer.Id, r.Offer.CurrentFingerprint.Hash)).ToList();
        var basisVersion = AppliedBasisInputs.Version(appliedBasis);
        var hasAffinityBasis = basisVersion is not null;
        var appliedViews = appliedRows.Select(r => (r.Offer.Id, View: ToAppliedOfferView(r.Offer, settings))).ToList();

        var items = new List<object>();

        // (1) CV profiles FIRST (FR-019 profile-before-fit sequencing).
        foreach (var cv in allCvs.Where(c => c.ProfileState == CvProcessingState.Pending))
        {
            items.Add(BuildCvProfileItem(cv, settings.Enrichment));
        }

        // (2)+(3)+(4) summaries + fits + affinity, merged + ordered: available first, published DESC
        // (date-less last), OfferId, then kind (summary, fit, affinity). The per-offer emission realizes that key.
        var orderedOffers = rows
            .OrderBy(r => r.Offer.Availability == AvailabilityStatus.Available ? 0 : 1)
            .ThenByDescending(r => r.Offer.PublishedAt ?? DateTimeOffset.MinValue)
            .ThenBy(r => r.Offer.Id.Value)
            .ToList();

        foreach (var row in orderedOffers)
        {
            if (row.Enrichment.State == EnrichmentState.Pending)
            {
                items.Add(BuildSummaryItem(row.Offer, row.Enrichment, settings));
            }

            if (hasProducedProfile && row.Fit.State == EnrichmentState.Pending)
            {
                items.Add(BuildFitItem(row.Offer, row.Fit, producedCv!.Profile!, effectiveProfileVersion!, settings));
            }

            if (hasAffinityBasis && row.Affinity.State == EnrichmentState.Pending)
            {
                items.Add(BuildAffinityItem(row.Offer, basisVersion!, appliedViews, settings));
            }
        }

        var page = items.Take(limit).ToList();

        var counts = await enrichment.GetCountsAsync(hasProducedProfile, hasAffinityBasis, ct);
        var pendingProfiles = allCvs.Count(c => c.ProfileState == CvProcessingState.Pending);
        var failedProfiles = allCvs.Count(c => c.ProfileState == CvProcessingState.Failed);
        var meta = new PendingMeta(
            PendingTotal: pendingProfiles + counts.PendingSummaries + counts.PendingFits + counts.PendingAffinity,
            PendingProfiles: pendingProfiles,
            PendingSummaries: counts.PendingSummaries,
            PendingFits: counts.PendingFits,
            FailedTotal: failedProfiles + counts.FailedSummaries + counts.FailedFits + counts.FailedAffinity,
            Returned: page.Count,
            HasProducedProfile: hasProducedProfile,
            Guidance: new WorkGuidance(
                settings.Enrichment.OfferSummaryMaxWords,
                settings.Enrichment.CvSummaryMaxWords,
                settings.Enrichment.MaxKeySkills,
                settings.Enrichment.FitRationaleMaxWords,
                settings.Enrichment.AffinityRationaleMaxWords),
            RetryLimit: Math.Max(1, settings.Enrichment.RetryLimit),
            PendingAffinity: counts.PendingAffinity,
            FailedAffinity: counts.FailedAffinity,
            AppliedCount: appliedBasis.Count,
            HasAffinityBasis: hasAffinityBasis);

        return new PendingWork(meta, page);
    }

    public async Task<EnrichmentStatusView> GetStatusAsync(CancellationToken ct = default)
    {
        var allCvs = await cvs.GetAllAsync(ct);
        var hasProducedProfile = allCvs.Any(c => c.HasProducedProfile);
        var appliedCount = await enrichment.GetAppliedCountAsync(ct);
        var hasAffinityBasis = appliedCount >= OfferAffinity.MinApplications;
        var counts = await enrichment.GetCountsAsync(hasProducedProfile, hasAffinityBasis, ct);
        var pendingProfiles = allCvs.Count(c => c.ProfileState == CvProcessingState.Pending);
        var failedProfiles = allCvs.Count(c => c.ProfileState == CvProcessingState.Failed);

        var lastSatellite = await enrichment.GetLastResultAtAsync(ct);
        var lastCv = allCvs.Select(c => c.ProfileProducedAt).DefaultIfEmpty(null).Max();

        return new EnrichmentStatusView(
            PendingTotal: pendingProfiles + counts.PendingSummaries + counts.PendingFits + counts.PendingAffinity,
            PendingProfiles: pendingProfiles,
            PendingSummaries: counts.PendingSummaries,
            PendingFits: counts.PendingFits,
            FailedTotal: failedProfiles + counts.FailedSummaries + counts.FailedFits + counts.FailedAffinity,
            HasProducedProfile: hasProducedProfile,
            LastResultAt: new[] { lastSatellite, lastCv }.Max(),
            PendingAffinity: counts.PendingAffinity,
            FailedAffinity: counts.FailedAffinity,
            AppliedCount: appliedCount,
            HasAffinityBasis: hasAffinityBasis);
    }

    public async Task<SubmitResultsResponse> SubmitResultsAsync(SubmitResultsRequest request, CancellationToken ct = default)
    {
        // Defer enrichment write-backs while a restore is wiping/reloading (FR-020). The /enrich worker
        // is re-runnable, but waiting out the brief restore window avoids interleaving writes with it.
        await maintenance.WaitWhileActiveAsync(ct);

        var results = request.Results ?? [];
        var settings = await settingsRepo.GetAsync(ct);
        var retryLimit = Math.Max(1, settings.Enrichment.RetryLimit);
        var now = time.GetUtcNow();

        var allCvs = await cvs.GetAllAsync(ct); // tracked
        var producedCv = ProducedCv(allCvs);
        var effectiveProfileVersion = producedCv is not null
            ? EffectiveProfile.Version(producedCv.Profile!, settings.Preferences)
            : (InputHash?)null;

        var rows = await enrichment.GetOfferWorkRowsAsync(ct);
        var offersById = rows.ToDictionary(r => r.Offer.Id, r => r.Offer);

        // Affinity basis version recomputed from live inputs (006): the write-back stale guard for the
        // affinity kind. Null below the ≥3 gate → an affinity write-back is rejected as stale.
        var appliedBasis = rows.Where(r => r.Offer.Applied).Select(r => (r.Offer.Id, r.Offer.CurrentFingerprint.Hash)).ToList();
        var basisVersion = AppliedBasisInputs.Version(appliedBasis);

        var outcomes = new List<ResultOutcomeView>();
        foreach (var item in results)
        {
            outcomes.Add(await HandleResultAsync(item, offersById, allCvs, effectiveProfileVersion, basisVersion, settings, retryLimit, now, ct));
        }

        await unitOfWork.SaveChangesAsync(ct);

        var accepted = outcomes.Count(o => o.Outcome is "produced" or "unreadable");
        return new SubmitResultsResponse(accepted, outcomes.Count - accepted, outcomes);
    }

    /// <summary>In-app re-run (FR-009). Does NOT call AI — only re-arms app state; the user then runs <c>/enrich</c>.</summary>
    public async Task<EnrichmentStatusView> TriggerRerunAsync(string? scope, CancellationToken ct = default)
    {
        await maintenance.WaitWhileActiveAsync(ct); // defer re-arm while a restore runs (FR-020)

        var allCvs = await cvs.GetAllAsync(ct); // tracked
        if (string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase))
        {
            await enrichment.ForceAllPendingAsync(ct);
            foreach (var cv in allCvs)
            {
                cv.ForceProfilePending();
            }
        }
        else
        {
            await enrichment.RearmFailedAsync(ct);
            foreach (var cv in allCvs)
            {
                cv.RearmProfile();
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
        return await GetStatusAsync(ct);
    }

    // ---- Per-kind projection (US2/US3/US4) -----------------------------------------------------

    private CvProfileWorkItem BuildCvProfileItem(CandidateCv cv, EnrichmentSettings guidance)
    {
        var path = fileStore.GetAbsolutePath(cv.FileName);
        var inputsHash = cv.EnrichmentInputHash ?? ComputeCvHash(path);

        // Keep PII off the wire: when readable, the worker reads the original PDF by path; only an
        // unreadable (image-only) CV gets PdfPig fallback text — and that is null when none extracts.
        string? fallbackText = null;
        if (!cv.IsReadable && File.Exists(path))
        {
            using var stream = File.OpenRead(path);
            var extracted = extractor.ExtractText(stream);
            fallbackText = extracted.IsSuccess ? extracted.Value : null;
        }

        return new CvProfileWorkItem(
            CvId: cv.Id.Value,
            InputsHash: inputsHash,
            Attempt: cv.ProfileAttempts + 1,
            Document: new CvDocumentView(path, cv.FileName, cv.IsReadable, fallbackText),
            Guidance: new { summaryWords = guidance.CvSummaryMaxWords, maxSkills = guidance.MaxKeySkills });
    }

    private OfferSummaryWorkItem BuildSummaryItem(Offer offer, OfferEnrichment _, AppSettings settings)
    {
        var inputsHash = OfferEnrichmentInputs
            .Hash(offer.CurrentFingerprint.Hash, offer.Company, offer.Location, offer.DescriptionHtml)
            .Serialized;

        return new OfferSummaryWorkItem(
            OfferId: offer.Id.Value,
            InputsHash: inputsHash,
            Attempt: 1,
            Offer: new SummaryOfferView(
                Title: offer.Title,
                Company: offer.Company,
                Location: offer.Location,
                WorkMode: offer.WorkMode == WorkMode.Unknown ? null : offer.WorkMode.ToString(),
                EmploymentType: offer.EmploymentType,
                Seniority: offer.Seniority,
                SalaryBands: [.. offer.SalaryBands.Select(ToBandView)],
                RequiredSkills: [.. offer.RequiredSkills],
                NiceToHaveSkills: [.. offer.NiceToHaveSkills],
                DescriptionText: ToPlainText(offer.DescriptionHtml)),
            Guidance: new { summaryWords = settings.Enrichment.OfferSummaryMaxWords, maxSkills = settings.Enrichment.MaxKeySkills });
    }

    private OfferFitWorkItem BuildFitItem(Offer offer, OfferFit _, CvProfile profile, InputHash effectiveProfileVersion, AppSettings settings)
    {
        var offerEnrichHash = OfferEnrichmentInputs.Hash(offer.CurrentFingerprint.Hash, offer.Company, offer.Location, offer.DescriptionHtml);
        var inputsHash = OfferFitInputs.Hash(offerEnrichHash, effectiveProfileVersion, settings.Weights).Serialized;
        var normalizedMonthly = NormalizedMonthly(offer, settings);
        var prefs = settings.Preferences;

        return new OfferFitWorkItem(
            OfferId: offer.Id.Value,
            InputsHash: inputsHash,
            Attempt: 1,
            Offer: new FitOfferView(
                Title: offer.Title,
                RequiredSkills: [.. offer.RequiredSkills],
                NiceToHaveSkills: [.. offer.NiceToHaveSkills],
                Seniority: offer.Seniority,
                WorkMode: offer.WorkMode == WorkMode.Unknown ? null : offer.WorkMode.ToString(),
                EmploymentType: offer.EmploymentType,
                NormalizedMonthlySalary: normalizedMonthly),
            Profile: new FitProfileView(profile.Skills, profile.Seniority, profile.Summary),
            Weights: new FitWeightsView(settings.Weights.Skills, settings.Weights.Seniority, settings.Weights.WorkMode, settings.Weights.Employment, settings.Weights.Salary),
            Preferences: new FitPreferencesView(
                prefs.SalaryFloor,
                prefs.SalaryTarget,
                prefs.PreferredWorkModes,
                [.. prefs.PreferredEmployment.Select(e => e.ToString())]),
            Guidance: new { rationaleWords = settings.Enrichment.FitRationaleMaxWords });
    }

    private OfferAffinityWorkItem BuildAffinityItem(
        Offer offer,
        InputHash basisVersion,
        IReadOnlyList<(OfferId Id, AppliedOfferView View)> appliedViews,
        AppSettings settings)
    {
        var offerEnrichHash = OfferEnrichmentInputs.Hash(offer.CurrentFingerprint.Hash, offer.Company, offer.Location, offer.DescriptionHtml);
        var inputsHash = OfferAffinityInputs.Hash(offerEnrichHash, basisVersion).Serialized;
        // Exclude the candidate from its OWN basis (no trivial self-match) — the version stays global (006 ADR-2).
        var basis = appliedViews.Where(a => a.Id != offer.Id).Select(a => a.View).ToList();

        return new OfferAffinityWorkItem(
            OfferId: offer.Id.Value,
            InputsHash: inputsHash,
            Attempt: 1,
            Offer: new AffinityOfferView(
                Title: offer.Title,
                RequiredSkills: [.. offer.RequiredSkills],
                NiceToHaveSkills: [.. offer.NiceToHaveSkills],
                Seniority: offer.Seniority,
                WorkMode: offer.WorkMode == WorkMode.Unknown ? null : offer.WorkMode.ToString(),
                EmploymentType: offer.EmploymentType,
                NormalizedMonthlySalary: NormalizedMonthly(offer, settings)),
            AppliedBasis: basis,
            Guidance: new { rationaleWords = settings.Enrichment.AffinityRationaleMaxWords });
    }

    private AppliedOfferView ToAppliedOfferView(Offer offer, AppSettings settings) => new(
        Title: offer.Title,
        RequiredSkills: [.. offer.RequiredSkills],
        NiceToHaveSkills: [.. offer.NiceToHaveSkills],
        Seniority: offer.Seniority,
        WorkMode: offer.WorkMode == WorkMode.Unknown ? null : offer.WorkMode.ToString(),
        EmploymentType: offer.EmploymentType,
        NormalizedMonthlySalary: NormalizedMonthly(offer, settings));

    // ---- Per-kind write-back (recompute guard + validation + state machine) --------------------

    private async Task<ResultOutcomeView> HandleResultAsync(
        EnrichmentResultItem item,
        IReadOnlyDictionary<OfferId, Offer> offersById,
        IReadOnlyList<CandidateCv> allCvs,
        InputHash? effectiveProfileVersion,
        InputHash? appliedBasisVersion,
        AppSettings settings,
        int retryLimit,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var workItemId = item.WorkItemId ?? "";
        if (!TryParseWorkItem(workItemId, out var entity, out var guid, out var sub))
        {
            return new ResultOutcomeView(workItemId, "unknown", 0, "");
        }

        // cvProfile -----------------------------------------------------------------------------
        if (entity == "cv" && sub == "profile")
        {
            var cv = allCvs.FirstOrDefault(c => c.Id.Value == guid);
            if (cv is null)
            {
                return new ResultOutcomeView(workItemId, "unknown", 0, "");
            }

            var current = cv.EnrichmentInputHash ?? ComputeCvHash(fileStore.GetAbsolutePath(cv.FileName));
            if (item.InputsHash != current)
            {
                return new ResultOutcomeView(workItemId, "stale", cv.ProfileAttempts, cv.ProfileState.ToString());
            }

            switch ((item.Status ?? "").ToLowerInvariant())
            {
                case "produced" when item.Skills is not null:
                    cv.ApplyProfile(new CvProfile([.. item.Skills], item.Seniority ?? "", item.Summary ?? ""), current, now);
                    return new ResultOutcomeView(workItemId, "produced", 0, cv.ProfileState.ToString());
                case "unreadable":
                    cv.MarkUnreadable(now);
                    return new ResultOutcomeView(workItemId, "unreadable", cv.ProfileAttempts, cv.ProfileState.ToString());
                default:
                    cv.RecordProfileFailure(retryLimit, now);
                    return new ResultOutcomeView(
                        workItemId,
                        cv.ProfileState == CvProcessingState.Failed ? "failed" : "pendingRetry",
                        cv.ProfileAttempts,
                        cv.ProfileState.ToString());
            }
        }

        if (entity != "offer" || !offersById.TryGetValue(OfferId.From(guid), out var offer))
        {
            return new ResultOutcomeView(workItemId, "unknown", 0, "");
        }

        var offerEnrichHash = OfferEnrichmentInputs.Hash(offer.CurrentFingerprint.Hash, offer.Company, offer.Location, offer.DescriptionHtml);

        // offerSummary --------------------------------------------------------------------------
        if (sub == "summary")
        {
            var e = await enrichment.GetEnrichmentAsync(offer.Id, ct);
            if (e is null)
            {
                return new ResultOutcomeView(workItemId, "unknown", 0, "");
            }

            if (item.InputsHash != offerEnrichHash.Serialized)
            {
                return new ResultOutcomeView(workItemId, "stale", e.Attempts, e.State.ToString());
            }

            var validProduced = string.Equals(item.Status, "produced", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(item.Summary)
                && item.KeySkills is not null;
            if (validProduced)
            {
                var skills = item.KeySkills!.Take(Math.Max(1, settings.Enrichment.MaxKeySkills)).ToList();
                e.MarkProduced(item.Summary!.Trim(), skills, offerEnrichHash.Serialized, now);
                return new ResultOutcomeView(workItemId, "produced", 0, e.State.ToString());
            }

            e.RecordFailure(item.Reason ?? "invalid summary payload", retryLimit);
            return new ResultOutcomeView(workItemId, e.State == EnrichmentState.Failed ? "failed" : "pendingRetry", e.Attempts, e.State.ToString());
        }

        // offerFit ------------------------------------------------------------------------------
        if (sub == "fit")
        {
            var f = await enrichment.GetFitAsync(offer.Id, ct);
            if (f is null)
            {
                return new ResultOutcomeView(workItemId, "unknown", 0, "");
            }

            // No current produced profile ⇒ the fit isn't eligible ⇒ the echoed hash can't match.
            if (effectiveProfileVersion is null)
            {
                return new ResultOutcomeView(workItemId, "stale", f.Attempts, f.State.ToString());
            }

            var current = OfferFitInputs.Hash(offerEnrichHash, effectiveProfileVersion, settings.Weights).Serialized;
            if (item.InputsHash != current)
            {
                return new ResultOutcomeView(workItemId, "stale", f.Attempts, f.State.ToString());
            }

            var validProduced = string.Equals(item.Status, "produced", StringComparison.OrdinalIgnoreCase)
                && item.Score is >= 0 and <= 100;
            if (validProduced)
            {
                f.MarkProduced(item.Score!.Value, item.Matched ?? [], item.Missing ?? [], item.Rationale, current, now);
                return new ResultOutcomeView(workItemId, "produced", 0, f.State.ToString());
            }

            f.RecordFailure(item.Reason ?? "invalid fit payload", retryLimit);
            return new ResultOutcomeView(workItemId, f.State == EnrichmentState.Failed ? "failed" : "pendingRetry", f.Attempts, f.State.ToString());
        }

        // offerAffinity -------------------------------------------------------------------------
        if (sub == "affinity")
        {
            var a = await enrichment.GetAffinityAsync(offer.Id, ct);
            if (a is null)
            {
                return new ResultOutcomeView(workItemId, "unknown", 0, "");
            }

            // Below the ≥3 gate ⇒ affinity isn't eligible ⇒ the echoed hash can't match.
            if (appliedBasisVersion is null)
            {
                return new ResultOutcomeView(workItemId, "stale", a.Attempts, a.State.ToString());
            }

            var current = OfferAffinityInputs.Hash(offerEnrichHash, appliedBasisVersion).Serialized;
            if (item.InputsHash != current)
            {
                return new ResultOutcomeView(workItemId, "stale", a.Attempts, a.State.ToString());
            }

            var validProduced = string.Equals(item.Status, "produced", StringComparison.OrdinalIgnoreCase)
                && item.Score is >= 0 and <= 100;
            if (validProduced)
            {
                a.MarkProduced(item.Score!.Value, item.Resembles ?? [], item.Rationale, current, now);
                return new ResultOutcomeView(workItemId, "produced", 0, a.State.ToString());
            }

            a.RecordFailure(item.Reason ?? "invalid affinity payload", retryLimit);
            return new ResultOutcomeView(workItemId, a.State == EnrichmentState.Failed ? "failed" : "pendingRetry", a.Attempts, a.State.ToString());
        }

        return new ResultOutcomeView(workItemId, "unknown", 0, "");
    }

    // ---- Helpers -------------------------------------------------------------------------------

    /// <summary>The CV whose AI profile is the current effective profile: a produced one, most recent.</summary>
    private static CandidateCv? ProducedCv(IReadOnlyList<CandidateCv> allCvs) =>
        allCvs.Where(c => c.HasProducedProfile)
            .OrderByDescending(c => c.ProfileProducedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();

    private decimal? NormalizedMonthly(Offer offer, AppSettings settings)
    {
        var preferredBasis = settings.Preferences.PreferredEmployment.Count > 0
            ? settings.Preferences.PreferredEmployment[0]
            : (EmploymentBasis?)null;
        return OfferSalaryReducer.BestComparable(offer.SalaryBands, settings.Normalization, preferredBasis)?.ComparableMonthly.Amount;
    }

    private string ComputeCvHash(string path)
    {
        if (!File.Exists(path))
        {
            return CvProfileInputs.Hash(ReadOnlySpan<byte>.Empty).Serialized;
        }

        return CvProfileInputs.Hash(File.ReadAllBytes(path)).Serialized;
    }

    /// <summary>
    /// Strip the offer description to readable plain text for the worker (it is sent as data, never
    /// rendered as HTML — so no XSS surface). Drops script/style contents, removes tags, decodes
    /// entities, and collapses whitespace.
    /// </summary>
    private static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return "";
        }

        var noScripts = Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var noTags = Regex.Replace(noScripts, "<[^>]+>", " ");
        var decoded = WebUtility.HtmlDecode(noTags);
        return Regex.Replace(decoded, "\\s+", " ").Trim();
    }

    private static SalaryBandView ToBandView(SalaryBand b) => new(
        Min: b.AmountMin,
        Max: b.AmountMax,
        Currency: b.Currency?.Code,
        Period: b.Period?.ToString().ToLowerInvariant(),
        Basis: b.Basis.ToString().ToLowerInvariant(),
        Tax: b.Tax.ToString().ToLowerInvariant());

    private static bool TryParseWorkItem(string workItemId, out string entity, out Guid id, out string sub)
    {
        entity = "";
        sub = "";
        id = Guid.Empty;
        var parts = workItemId.Split(':');
        if (parts.Length != 3 || !Guid.TryParse(parts[1], out id))
        {
            return false;
        }

        entity = parts[0];
        sub = parts[2];
        return true;
    }
}
