using JobOfferMatcher.Application.TailoredCvs;
using Microsoft.Playwright;

namespace JobOfferMatcher.Infrastructure.TailoredCvs;

/// <summary>
/// Renders self-contained HTML to an A4 PDF via the already-present headless Chromium (ADR-1) — the
/// same engine the <c>cv_versions</c> recipe uses (<c>chrome --headless --print-to-pdf</c>), so it
/// reproduces that two-column layout faithfully. <b>No new NuGet dependency</b>: Microsoft.Playwright is
/// already referenced for the 001 theprotocol scraper. The browser is launched lazily and reused; a gate
/// serialises page creation (single-user, sub-second renders). Mirrors <c>PlaywrightTheProtocolClient</c>.
/// Registered as a DI singleton; <see cref="DisposeAsync"/> tears the browser down on shutdown.
/// </summary>
public sealed class PlaywrightPdfRenderer : IPdfRenderer, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public async Task<byte[]> RenderA4Async(string html, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var browser = await EnsureBrowserAsync();
            await using var context = await browser.NewContextAsync();
            var page = await context.NewPageAsync();
            await page.SetContentAsync(html);
            return await page.PdfAsync(new PagePdfOptions { Format = "A4", PrintBackground = true });
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IBrowser> EnsureBrowserAsync()
    {
        if (_browser is { IsConnected: true })
        {
            return _browser;
        }

        _playwright ??= await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        return _browser;
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();
        _gate.Dispose();
    }
}
