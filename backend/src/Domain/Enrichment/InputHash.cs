namespace JobOfferMatcher.Domain.Enrichment;

/// <summary>
/// Keys an AI-derived output to its own inputs (data-model §2), mirroring
/// <see cref="JobOfferMatcher.Domain.Offers.ContentFingerprint"/>'s shape. A <see cref="Version"/>
/// bump forces a global recompute. Serialized as a single <c>algo:version:hash</c> string for the
/// satellite columns and the worker echo (so the read-path/write-back stale guard is a string compare).
/// </summary>
public sealed record InputHash(string Algorithm, int Version, string Hash)
{
    public const string Sha256 = "SHA256";

    /// <summary>The canonical <c>algo:version:hash</c> wire/storage form (echoed by the worker).</summary>
    public string Serialized => $"{Algorithm}:{Version}:{Hash}";

    public override string ToString() => Serialized;

    /// <summary>Parse the <c>algo:version:hash</c> form back; null on a malformed string.</summary>
    public static InputHash? Parse(string? serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return null;
        }

        var parts = serialized.Split(':');
        if (parts.Length != 3 || !int.TryParse(parts[1], out var version))
        {
            return null;
        }

        return new InputHash(parts[0], version, parts[2]);
    }
}
