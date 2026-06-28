using System.Text;
using JobOfferMatcher.Application.Cv;
using JobOfferMatcher.Infrastructure.Cv;

namespace JobOfferMatcher.Infrastructure.Tests;

/// <summary>
/// Integration test (T049): PdfPig CV extraction + graceful degradation. The readable-CV assertion
/// uses the user's LOCAL gitignored CV only if present (no PII committed — Principle IV); the
/// degradation path always runs.
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
    public void Readable_cv_yields_text_and_a_skills_profile_when_a_local_cv_is_present()
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

        var builder = new CvProfileBuilder(SkillCatalogLoader.Load());
        var profile = builder.BuildFromText(result.Value);
        // A .NET CV should surface at least one recognized skill.
        profile.Skills.ShouldNotBeEmpty();
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
