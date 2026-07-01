using JobOfferMatcher.Application.TailoredCvs;
using JobOfferMatcher.Domain.Common.Ids;
using Microsoft.Extensions.Configuration;

namespace JobOfferMatcher.Infrastructure.TailoredCvs;

/// <summary>
/// Saves produced tailored CVs as <b>flat</b> files (<c>tailored-{OfferId:N}.html</c> /
/// <c>.pdf</c>) in the <b>same</b> gitignored <c>cv-data</c> root as the uploaded CVs
/// (<c>Cv:StoragePath ?? {BaseDirectory}/cv-data</c>) — so 003's top-level, non-recursive backup
/// enumeration captures them unchanged (ADR-3). Regenerate overwrites the same two files (latest-only).
/// </summary>
public sealed class LocalTailoredCvFileStore : ITailoredCvFileStore
{
    private readonly string _directory;

    public LocalTailoredCvFileStore(IConfiguration configuration)
    {
        _directory = configuration["Cv:StoragePath"] ?? Path.Combine(AppContext.BaseDirectory, "cv-data");
        Directory.CreateDirectory(_directory);
    }

    private static string HtmlName(OfferId offerId) => $"tailored-{offerId.Value:N}.html";
    private static string PdfName(OfferId offerId) => $"tailored-{offerId.Value:N}.pdf";

    public async Task<TailoredCvFiles> SaveAsync(OfferId offerId, string html, byte[] pdf, CancellationToken ct = default)
    {
        var htmlName = HtmlName(offerId);
        var pdfName = PdfName(offerId);
        await File.WriteAllTextAsync(Path.Combine(_directory, htmlName), html, ct);
        await File.WriteAllBytesAsync(Path.Combine(_directory, pdfName), pdf, ct);
        return new TailoredCvFiles(htmlName, pdfName);
    }

    public string GetPdfAbsolutePath(OfferId offerId) =>
        Path.GetFullPath(Path.Combine(_directory, PdfName(offerId)));

    public string? GetHtml(OfferId offerId)
    {
        var path = Path.Combine(_directory, HtmlName(offerId));
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public void Delete(OfferId offerId)
    {
        foreach (var name in new[] { HtmlName(offerId), PdfName(offerId) })
        {
            var path = Path.Combine(_directory, name);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
