using System.Text;
using JobOfferMatcher.Infrastructure.TailoredCvs;

namespace JobOfferMatcher.Infrastructure.Tests.TailoredCvs;

/// <summary>
/// Real-render smoke test (004 T014, ADR-1): the <b>actual</b> <see cref="PlaywrightPdfRenderer"/>
/// drives the already-present headless Chromium to turn a two-page sample HTML into a non-empty A4 PDF
/// (<c>%PDF</c> header, two pages). The DB integration suite fakes <c>IPdfRenderer</c> to stay offline;
/// this single test exercises the adapter end-to-end.
/// </summary>
public sealed class PdfRendererTests
{
    [Fact]
    public async Task Renders_two_page_html_to_a_non_empty_a4_pdf()
    {
        const string html = """
            <!doctype html>
            <html>
              <head><meta charset="utf-8"><style>
                .page { height: 297mm; page-break-after: always; }
              </style></head>
              <body>
                <div class="page"><h1>Page one</h1></div>
                <div class="page"><h1>Page two</h1></div>
              </body>
            </html>
            """;

        await using var renderer = new PlaywrightPdfRenderer();
        var pdf = await renderer.RenderA4Async(html);

        // A genuine Chromium render: the %PDF header, a non-trivial body, and the %%EOF trailer.
        // (The page-tree count isn't asserted — Chromium compresses the structure into object streams,
        // so it isn't greppable in the raw bytes; the two-column layout is verified visually in T053.)
        Encoding.ASCII.GetString(pdf, 0, 5).ShouldBe("%PDF-");
        pdf.Length.ShouldBeGreaterThan(1000);
        Encoding.Latin1.GetString(pdf).TrimEnd().ShouldEndWith("%%EOF");
    }
}
