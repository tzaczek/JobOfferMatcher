using JobOfferMatcher.Domain.Common.Ids;

namespace JobOfferMatcher.Application.TailoredCvs;

/// <summary>The stored file names of a produced tailored CV (flat in the <c>cv-data</c> root — ADR-3).</summary>
public sealed record TailoredCvFiles(string HtmlFileName, string PdfFileName);

/// <summary>
/// Stores a produced tailored CV as <b>flat</b> files (<c>tailored-{OfferId:N}.html</c> +
/// <c>tailored-{OfferId:N}.pdf</c>) in the <b>same</b> <c>cv-data</c> root as the uploaded CVs
/// (<c>Cv:StoragePath ?? {BaseDirectory}/cv-data</c>), so 003's top-level backup enumeration captures
/// them unchanged (ADR-3). The bytes never leave the machine; the PDF is served only over the
/// loopback-restricted download endpoint to the local user (Principle IV).
/// </summary>
public interface ITailoredCvFileStore
{
    Task<TailoredCvFiles> SaveAsync(OfferId offerId, string html, byte[] pdf, CancellationToken ct = default);

    /// <summary>The absolute path of the produced PDF (for the download endpoint's <c>Results.File</c>). Empty if absent.</summary>
    string GetPdfAbsolutePath(OfferId offerId);

    /// <summary>The stored HTML (for the in-app <c>/preview</c>), or null if not produced.</summary>
    string? GetHtml(OfferId offerId);

    /// <summary>Remove both files for an offer (idempotent).</summary>
    void Delete(OfferId offerId);
}
