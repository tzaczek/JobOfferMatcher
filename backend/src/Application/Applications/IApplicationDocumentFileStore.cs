using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Application.Applications;

/// <summary>
/// Stores application attachments as flat files in the same <c>cv-data</c> root the CV / tailored-CV
/// stores use (data-model §6, ADR-4) — <c>appdoc-{ApplicationDocumentId:N}{ext}</c> — so 003's top-level
/// backup enumeration + atomic swap cover them with zero changes. Singleton (like <c>LocalCvFileStore</c>).
/// </summary>
public interface IApplicationDocumentFileStore
{
    /// <summary>Write the bytes under a flat <c>appdoc-{id:N}{ext}</c> name and return that stored name.</summary>
    Task<string> SaveAsync(ApplicationDocumentId id, string originalFileName, byte[] content, CancellationToken ct = default);

    /// <summary>Absolute path of a stored file (for streaming a download). Not guaranteed to exist.</summary>
    string GetAbsolutePath(string storedFileName);

    /// <summary>Delete the stored file if present (idempotent — removing a document also removes its bytes).</summary>
    void Delete(string storedFileName);
}
