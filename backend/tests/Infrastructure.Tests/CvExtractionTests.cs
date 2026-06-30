using System.Text;
using JobOfferMatcher.Application.Cv;
using JobOfferMatcher.Infrastructure.Cv;

namespace JobOfferMatcher.Infrastructure.Tests;

/// <summary>
/// PdfPig CV extraction + graceful degradation (the retained readability gauge + text fallback,
/// ADR-2). The keyword <c>CvProfileBuilder</c> assertion was dropped (FR-005 — the AI worker is the
/// sole profiler now); only the readability gauge is exercised here. The readable-CV assertion uses
/// the user's LOCAL gitignored CV only if present (no PII committed — Principle IV); the degradation
/// path always runs.
/// </summary>
public sealed class CvExtractionTests
{
    private readonly PdfPigCvTextExtractor _extractor = new();

    [Fact]
    public void Unreadable_or_corrupt_pdf_returns_no_readable_text_not_an_exception()
    {
        using var garbage = new MemoryStream(Encoding.UTF8.GetBytes("this is not a pdf at all, just bytes"));

        var result = _extractor.ExtractText(garbage);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(CvExtraction.NoReadableCvText);
    }

    [Fact]
    public void Readable_cv_yields_text_when_a_local_cv_is_present()
    {
        var cvPath = FindLocalCv();
        if (cvPath is null)
        {
            return; // no local CV available — skip (the degradation path is covered above)
        }

        using var stream = File.OpenRead(cvPath);
        var result = _extractor.ExtractText(stream);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Length.ShouldBeGreaterThan(200);
    }

    private static string? FindLocalCv()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var cvDir = Path.Combine(dir.FullName, "cv");
            if (Directory.Exists(cvDir))
            {
                var pdf = Directory.EnumerateFiles(cvDir, "*.pdf").FirstOrDefault();
                if (pdf is not null)
                {
                    return pdf;
                }
            }

            dir = dir.Parent;
        }

        return null;
    }
}
