using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Domain.Applications;

/// <summary>
/// Metadata for a file attached to an application (data-model §3/§6, FR-010). The bytes live flat in
/// the <c>cv-data</c> root as <c>appdoc-{Id:N}{ext}</c> (003 backs them up unchanged); this row records
/// the stored name, the user's original name, content type, size, and when it was added. The size cap
/// and file IO live in the service/file store, not the Domain. Carries <see cref="OfferId"/>.
/// </summary>
public sealed class ApplicationDocument
{
    public ApplicationDocumentId Id { get; private set; }
    public OfferId OfferId { get; private set; }
    public string StoredFileName { get; private set; } = string.Empty;
    public string OriginalFileName { get; private set; } = string.Empty;
    public string? ContentType { get; private set; }
    public long SizeBytes { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }

    private ApplicationDocument()
    {
        // EF Core materialization.
    }

    /// <summary>
    /// Create the metadata row for a stored attachment. The <paramref name="id"/> is supplied by the
    /// caller so the file store can name the flat file (<c>appdoc-{id:N}{ext}</c>) before the row exists.
    /// </summary>
    public static ApplicationDocument Create(
        ApplicationDocumentId id,
        OfferId offerId,
        string storedFileName,
        string originalFileName,
        string? contentType,
        long sizeBytes,
        DateTimeOffset addedAt) =>
        new()
        {
            Id = id,
            OfferId = offerId,
            StoredFileName = storedFileName,
            OriginalFileName = originalFileName,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? null : contentType,
            SizeBytes = sizeBytes,
            AddedAt = addedAt,
        };
}
