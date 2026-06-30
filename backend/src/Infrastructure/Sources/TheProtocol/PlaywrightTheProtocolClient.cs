using System.Diagnostics;
using JobOfferMatcher.Domain.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace JobOfferMatcher.Infrastructure.Sources.TheProtocol;

/// <summary>
/// Browser-based transport for theprotocol.it (FR-040 escalate-on-block). theprotocol fronts its
/// listing with a Cloudflare JS challenge that 403s a plain HttpClient; a real (headless) Chromium
/// executes the challenge JS, gets redirected to the actual page, and exposes the same
/// <c>__NEXT_DATA__</c> offers JSON the <see cref="HttpTheProtocolClient"/> parses. The browser is
/// launched lazily and reused across scans (scans are single-flight; a gate also serializes access);
/// requests are paced ~1 req/s. No PII, no AI — just an automated page load (Principle IV / FR-012).
/// </summary>
public sealed class PlaywrightTheProtocolClient(
    IOptions<TheProtocolOptions> options,
    ILogger<PlaywrightTheProtocolClient> logger) : ITheProtocolClient, IAsyncDisposable
{
    private readonly TheProtocolOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private bool _firstRequest = true;

    public async Task<(SourceFetchStatus Status, SourceListPage? Page)> FetchListAsync(
        JobSourceSearch search, int page, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await PaceAsync(ct);
            var url = _options.SiteBaseUrl.TrimEnd('/') + TheProtocolRequest.BuildListPathAndQuery(_options, search, page);
            var browser = await EnsureBrowserAsync();

            await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = _options.UserAgent,
                Locale = _options.AcceptLanguage,
                ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
            });

            var pwPage = await context.NewPageAsync();
            var html = await NavigateAndReadAsync(pwPage, url, page, ct);
            if (html is null)
            {
                return (SourceFetchStatus.Blocked, null);
            }

            if (NextDataExtractor.ExtractOffersResponse(html) is not { } offersResponse)
            {
                logger.LogWarning("theprotocol (browser) returned no __NEXT_DATA__ offers at page {Page} (challenge not cleared).", page);
                return (SourceFetchStatus.Blocked, null);
            }

            return (SourceFetchStatus.Ok, HttpTheProtocolClient.ParsePage(offersResponse));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // genuine caller cancellation
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "theprotocol browser fetch failed at page {Page}.", page);
            return (SourceFetchStatus.Failed, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Navigate, then poll until the Cloudflare interstitial clears and the offers JSON is present.</summary>
    private async Task<string?> NavigateAndReadAsync(IPage pwPage, string url, int page, CancellationToken ct)
    {
        await pwPage.GotoAsync(url, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = _options.NavigationTimeoutMs,
        });

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < _options.NavigationTimeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            var html = await pwPage.ContentAsync();
            if (NextDataExtractor.ExtractOffersResponse(html) is not null)
            {
                return html; // challenge cleared, real page loaded
            }

            await Task.Delay(1000, ct); // give Cloudflare time to solve + redirect
        }

        logger.LogWarning("theprotocol (browser) did not clear the challenge within {Timeout}ms at page {Page}.", _options.NavigationTimeoutMs, page);
        return null;
    }

    private async Task<IBrowser> EnsureBrowserAsync()
    {
        if (_browser is { IsConnected: true })
        {
            return _browser;
        }

        _playwright ??= await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = _options.Headless,
            // Reduce the headless automation fingerprint Cloudflare keys on.
            Args = ["--disable-blink-features=AutomationControlled"],
        });
        return _browser;
    }

    private async Task PaceAsync(CancellationToken ct)
    {
        if (_firstRequest)
        {
            _firstRequest = false;
            return;
        }

        if (_options.RequestDelayMs > 0)
        {
            await Task.Delay(_options.RequestDelayMs, ct);
        }
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
