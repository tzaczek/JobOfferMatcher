using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Domain.Offers;

/// <summary>
/// Stable cross-scan identity of an offer at a source (FR-011). <see cref="NativeKey"/> is the
/// source's durable id (justjoin.it <c>guid</c>) — never the volatile slug. Unique per source
/// (<c>UNIQUE(source_id, native_key)</c>). A value object with structural equality.
/// </summary>
public sealed record ExternalRef
{
    public SourceId SourceId { get; }
    public string NativeKey { get; }
    public IdentityKind Kind { get; }

    private ExternalRef(SourceId sourceId, string nativeKey, IdentityKind kind)
    {
        SourceId = sourceId;
        NativeKey = nativeKey;
        Kind = kind;
    }

    public static Result<ExternalRef> Create(SourceId sourceId, string? nativeKey, IdentityKind kind)
    {
        if (string.IsNullOrWhiteSpace(nativeKey))
        {
            return new Error("InvalidExternalRef", "A source-native identity key is required.");
        }

        return new ExternalRef(sourceId, nativeKey.Trim(), kind);
    }
}
