using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JobOfferMatcher.Domain.Offers;

/// <summary>
/// SHA-256 fingerprint over the canonical (sorted-key) JSON of an offer's <b>Major-tier</b> fields
/// (research §6): title, as-published salary (min|max|currency|period|basis — <b>FX-free</b>),
/// sorted required/nice skills, work mode, employment type, seniority. Description is excluded
/// (Minor tier). Identity and content are deliberately SEPARATE hashes so a routine salary edit
/// flags "updated" without minting a new identity (FR-013/014, SC-002). Pure Domain function.
/// </summary>
public sealed record ContentFingerprint(string Algorithm, int Version, string Hash)
{
    public const string Sha256 = "SHA256";

    /// <summary>Bump to suppress the "updated" flag for an algorithm-only change (data-model §Identity).</summary>
    public const int CurrentVersion = 1;

    public static ContentFingerprint Compute(OfferContent content)
    {
        var canonical = Canonicalize(content);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        var hex = Convert.ToHexStringLower(bytes);
        return new ContentFingerprint(Sha256, CurrentVersion, hex);
    }

    private static string Canonicalize(OfferContent c)
    {
        // Keys are emitted in alphabetical order; arrays are pre-sorted — so equal content always
        // produces byte-identical JSON regardless of source ordering.
        var bands = c.SalaryBands
            .Select(static b => new
            {
                basis = b.Basis.ToString(),
                currency = b.Currency?.Code,
                max = b.AmountMax,
                min = b.AmountMin,
                period = b.Period?.ToString(),
            })
            .OrderBy(static b => b.basis, StringComparer.Ordinal)
            .ThenBy(static b => b.currency, StringComparer.Ordinal)
            .ThenBy(static b => b.min)
            .ThenBy(static b => b.max);

        var root = new JsonObject
        {
            ["employmentType"] = Normalize(c.EmploymentType),
            ["niceSkills"] = SkillArray(c.NiceToHaveSkills),
            ["requiredSkills"] = SkillArray(c.RequiredSkills),
            ["salary"] = BandArray(bands),
            ["seniority"] = Normalize(c.Seniority),
            ["title"] = Normalize(c.Title),
            ["workMode"] = c.WorkMode.ToString(),
        };

        return root.ToJsonString();
    }

    private static JsonArray SkillArray(IReadOnlyList<string> skills)
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

    private static JsonArray BandArray<T>(IEnumerable<T> bands)
    {
        var array = new JsonArray();
        foreach (var band in bands)
        {
            array.Add(JsonSerializer.SerializeToNode(band));
        }

        return array;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
