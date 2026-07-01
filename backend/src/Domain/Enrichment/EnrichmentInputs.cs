using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Cv;
using JobOfferMatcher.Domain.Matching;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.Settings;

namespace JobOfferMatcher.Domain.Enrichment;

/// <summary>
/// Pure input-hash composers (data-model §3) — SHA-256 over canonical sorted-key JSON, the same
/// style as <see cref="ContentFingerprint.Compute"/>. Each AI output is keyed to its own inputs so
/// the server can recompute the current hash from live inputs and reject a stale write-back, and the
/// read path can render a superseded value as "pending" (FR-006/FR-007/SC-004). Versioned so a
/// formula change forces a global recompute. Framework-free; no IO (bytes are hashed by the caller).
/// </summary>
internal static class EnrichmentHashing
{
    internal static InputHash FromJson(JsonObject root, int version)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(root.ToJsonString()));
        return new InputHash(InputHash.Sha256, version, Convert.ToHexStringLower(bytes));
    }

    internal static JsonArray SortedSkills(IReadOnlyList<string> skills)
    {
        var array = new JsonArray();
        foreach (var skill in skills
                     .Select(static s => s.Trim().ToLowerInvariant())
                     .Where(static s => s.Length > 0)
                     .Distinct()
                     .OrderBy(static s => s, StringComparer.Ordinal))
        {
            array.Add(skill);
        }

        return array;
    }

    internal static JsonArray SortedStrings(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var v in values.Select(static s => s.Trim()).Where(static s => s.Length > 0).OrderBy(static s => s, StringComparer.Ordinal))
        {
            array.Add(v);
        }

        return array;
    }

    internal static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}

/// <summary>
/// Inputs for an offer's AI summary/key-skills. INCLUDES the description, company, and location — so
/// it is deliberately distinct from <see cref="ContentFingerprint"/> (which excludes the Minor-tier
/// description). A description-only edit therefore re-flips the summary (FR-006) without minting a new
/// offer identity.
/// </summary>
public static class OfferEnrichmentInputs
{
    public const int CurrentVersion = 1;

    /// <summary>From a collected content snapshot (scan path).</summary>
    public static InputHash Hash(OfferContent content) =>
        Hash(ContentFingerprint.Compute(content).Hash, content.Company, content.Location, content.DescriptionHtml);

    /// <summary>
    /// From the stored Major-tier fingerprint hash + the denormalized Minor-tier fields. Lets the
    /// read/write-back path recompute from the live <see cref="Offer"/> (whose fingerprint already
    /// excludes the description) without rebuilding an <see cref="OfferContent"/>.
    /// </summary>
    public static InputHash Hash(string contentFingerprintHash, string company, string? location, string? descriptionHtml)
    {
        var root = new JsonObject
        {
            ["company"] = EnrichmentHashing.Normalize(company),
            ["content"] = contentFingerprintHash,
            ["description"] = EnrichmentHashing.Normalize(descriptionHtml),
            ["location"] = EnrichmentHashing.Normalize(location),
        };
        return EnrichmentHashing.FromJson(root, CurrentVersion);
    }
}

/// <summary>Inputs for the CV profile: the CV file bytes (hashed in Infrastructure at upload — IO stays out of Domain).</summary>
public static class CvProfileInputs
{
    public const int CurrentVersion = 1;

    public static InputHash Hash(ReadOnlySpan<byte> documentBytes)
    {
        var hash = SHA256.HashData(documentBytes);
        return new InputHash(InputHash.Sha256, CurrentVersion, Convert.ToHexStringLower(hash));
    }
}

/// <summary>
/// The version of the effective profile = the produced AI <see cref="CvProfile"/> (ordered skills +
/// seniority + summary) grafted with the user's <see cref="ProfilePreferences"/>. Null until a
/// profile is <c>Produced</c> — the caller passes the produced profile only. Any change here re-flips
/// every fit (FR-007/SC-004).
/// </summary>
public static class EffectiveProfile
{
    public const int CurrentVersion = 1;

    public static InputHash Version(CvProfile profile, ProfilePreferences preferences)
    {
        var root = new JsonObject
        {
            ["prefEmployment"] = EnrichmentHashing.SortedStrings(preferences.PreferredEmployment.Select(e => e.ToString())),
            ["prefWorkModes"] = EnrichmentHashing.SortedStrings(preferences.PreferredWorkModes),
            ["salaryFloor"] = preferences.SalaryFloor,
            ["salaryTarget"] = preferences.SalaryTarget,
            ["seniority"] = EnrichmentHashing.Normalize(profile.Seniority),
            ["skills"] = EnrichmentHashing.SortedSkills(profile.Skills),
            ["summary"] = EnrichmentHashing.Normalize(profile.Summary),
        };
        return EnrichmentHashing.FromJson(root, CurrentVersion);
    }
}

/// <summary>
/// Inputs for an offer's AI fit: the offer's own enrichment hash (offer content incl. description) +
/// the effective-profile version (profile + preferences) + the scoring weights (Claude guidance).
/// CV/weights/preferences change → all fits pending; one offer's content change → that fit pending
/// (FR-004/006/007).
/// </summary>
public static class OfferFitInputs
{
    public const int CurrentVersion = 1;

    public static InputHash Hash(InputHash offerEnrichmentHash, InputHash effectiveProfileVersion, ScoringWeights weights)
    {
        var root = new JsonObject
        {
            ["offer"] = offerEnrichmentHash.Serialized,
            ["profile"] = effectiveProfileVersion.Serialized,
            ["weights"] = $"{weights.Skills}|{weights.Seniority}|{weights.WorkMode}|{weights.Employment}|{weights.Salary}",
        };
        return EnrichmentHashing.FromJson(root, CurrentVersion);
    }
}

/// <summary>
/// The version of the affinity basis = the set of offers the user has applied to, each identified by
/// its Major-tier fingerprint hash (feature 006 data-model §2). All applied offers weigh equally
/// (outcome-agnostic — ADR-2). <b>Null</b> below <see cref="OfferAffinity.MinApplications"/> (the
/// cold-start "insufficient basis" state). Any change to the applied SET (apply/un-apply) or to an
/// applied offer's CONTENT (its fingerprint) changes this version → every affinity input hash differs
/// → all affinity pending (mirrors "weights change → all fits pending"). The candidate is NOT excluded
/// here — the version is one global value shared by all offers; self-exclusion happens later when a
/// specific offer's work payload is built.
/// </summary>
public static class AppliedBasisInputs
{
    public const int CurrentVersion = 1;

    public static InputHash? Version(IReadOnlyList<(OfferId Id, string FingerprintHash)> applied)
    {
        if (applied.Count < OfferAffinity.MinApplications)
        {
            return null;
        }

        var array = new JsonArray();
        foreach (var (id, fingerprintHash) in applied.OrderBy(a => a.Id.Value))
        {
            array.Add(new JsonObject
            {
                ["fp"] = fingerprintHash,
                ["id"] = id.Value.ToString(),
            });
        }

        return EnrichmentHashing.FromJson(new JsonObject { ["applied"] = array }, CurrentVersion);
    }
}

/// <summary>
/// Inputs for an offer's AI affinity: the candidate offer's own enrichment hash (offer content incl.
/// description) + the applied-basis version. A candidate content/description change re-flips its
/// affinity (the input naturally follows a newly-captured body); an apply/un-apply re-flips every
/// affinity via the basis version (FR-002/FR-007). Orthogonal to fit and the CV profile (ADR-5).
/// </summary>
public static class OfferAffinityInputs
{
    public const int CurrentVersion = 1;

    public static InputHash Hash(InputHash offerEnrichmentHash, InputHash appliedBasisVersion)
    {
        var root = new JsonObject
        {
            ["basis"] = appliedBasisVersion.Serialized,
            ["offer"] = offerEnrichmentHash.Serialized,
        };
        return EnrichmentHashing.FromJson(root, CurrentVersion);
    }
}
