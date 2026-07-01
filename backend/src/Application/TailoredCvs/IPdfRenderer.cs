namespace JobOfferMatcher.Application.TailoredCvs;

/// <summary>
/// Renders a self-contained HTML document to an A4 PDF (ADR-1). The sole implementation
/// (<c>PlaywrightPdfRenderer</c>) drives the already-present headless Chromium — <b>no new
/// dependency</b>. Faked in the DB integration suite so it stays offline/deterministic; the real
/// adapter gets one focused render-smoke test (T014).
/// </summary>
public interface IPdfRenderer
{
    Task<byte[]> RenderA4Async(string html, CancellationToken ct = default);
}
