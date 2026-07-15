using System.Diagnostics;
using System.Text.Json;
using JobOfferMatcher.Application.Scanning;
using JobOfferMatcher.Domain.Common;
using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Offers;
using JobOfferMatcher.Domain.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace JobOfferMatcher.Infrastructure.Sources.LinkedIn;

/// <summary>
/// Headed, <b>persistent</b> Playwright transport for LinkedIn (feature 008, ADR-2). Unlike the two
/// ephemeral browser consumers, this launches <see cref="IBrowserType.LaunchPersistentContextAsync"/>
/// against an OS-app-data profile dir so the user's login (cookies + localStorage) persists and is reused
/// across scans (SC-003). The user types credentials / clears 2FA in the visible window; the backend
/// never reads, stores, transmits, or logs the password (Principle IV / FR-008/009/012). The context is
/// launched lazily and reused across single-flight scans (a <see cref="SemaphoreSlim"/> also serializes
/// access); reads are bounded (<c>MaxResultsPerSearch</c>) and paced (~&lt;1 req/s) per the 001 ADR-2
/// polite-access risk. The <b>real</b> path is verified by hand (quickstart.md) — never in the automated
/// suite, which uses a fake <see cref="ILinkedInClient"/>.
/// </summary>
public sealed class PlaywrightLinkedInClient(
    IOptions<LinkedInOptions> options,
    ILogger<PlaywrightLinkedInClient> logger) : ILinkedInClient, IAsyncDisposable
{
    private readonly LinkedInOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IPlaywright? _playwright;
    private IBrowserContext? _context;
    private bool _loggedIn;
    private bool _firstRequest = true;
    private volatile bool _contextClosed; // set from the context Close event (window shut / crash)
    private bool _disposed;

    /// <summary>Resolved once — the persistent-context profile directory (OS-app-data, gitignored, backup-excluded).</summary>
    private string ProfilePath =>
        string.IsNullOrWhiteSpace(_options.ProfilePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "JobOfferMatcher", "browser-profiles", "linkedin")
            : _options.ProfilePath;

    public async Task<Result<SessionReady>> EnsureLoggedInAsync(SourceId source, bool interactive, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            // Session reuse (SC-003): an already-open, still-valid session collects with no re-login.
            if (_context is not null && _loggedIn && await IsLoggedInAsync(_context, ct))
            {
                return new SessionReady(source, DateTimeOffset.UtcNow);
            }

            // Unattended (scheduled/catch-up/initial): never open a login window or wait for a human —
            // only reuse an already-open logged-in context (handled above). Otherwise: login required (ADR-3).
            if (!interactive)
            {
                logger.LogInformation("LinkedIn: no reusable session and scan is unattended — reporting login required (no window).");
                return Result<SessionReady>.Failure(LinkedInErrors.LoginRequired);
            }

            // Attended: launch the headed persistent context and wait (bounded) for the user to sign in.
            var context = await EnsureContextAsync();
            var page = context.Pages.Count > 0 ? context.Pages[0] : await context.NewPageAsync();
            await GotoAsync(page, _options.RecommendedUrl, ct);

            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < _options.LoginTimeoutMs)
            {
                ct.ThrowIfCancellationRequested();
                if (await IsLoggedInAsync(context, ct))
                {
                    _loggedIn = true;
                    logger.LogInformation("LinkedIn: interactive login completed; session established.");
                    return new SessionReady(source, DateTimeOffset.UtcNow);
                }

                await Task.Delay(1500, ct); // give the user time to type credentials / clear 2FA
            }

            logger.LogWarning("LinkedIn: interactive login not completed within {Timeout}ms.", _options.LoginTimeoutMs);
            return Result<SessionReady>.Failure(LinkedInErrors.LoginRequired);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "LinkedIn: login attempt failed.");
            return Result<SessionReady>.Failure(LinkedInErrors.LoginRequired);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<LinkedInListResult> FetchListAsync(LinkedInListRequest request, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_context is null || !_loggedIn)
            {
                return new LinkedInListResult(SourceFetchStatus.Failed, []);
            }

            await PaceAsync(ct);
            var url = request.Recommended
                ? _options.RecommendedUrl
                : LinkedInUrlBuilder.BuildSearchUrl(_options.SearchUrlTemplate, request.Search!);

            var page = _context.Pages.Count > 0 ? _context.Pages[0] : await _context.NewPageAsync();
            await GotoAsync(page, url, ct);

            // An anti-automation / checkpoint wall mid-collection is a "blocked" signal, not a crash.
            if (await IsBlockedAsync(page))
            {
                logger.LogWarning("LinkedIn: a checkpoint/challenge wall was hit while collecting {Url}.", url);
                return new LinkedInListResult(SourceFetchStatus.Blocked, []);
            }

            var cards = await ExtractCardsAsync(page, request.MaxResults, ct);
            return new LinkedInListResult(SourceFetchStatus.Ok, cards);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "LinkedIn: list fetch failed (recommended={Recommended}).", request.Recommended);
            return new LinkedInListResult(SourceFetchStatus.Failed, []);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> FetchBodyAsync(string jobId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_context is null || !_loggedIn)
            {
                return null;
            }

            await PaceAsync(ct);
            var page = _context.Pages.Count > 0 ? _context.Pages[0] : await _context.NewPageAsync();
            await GotoAsync(page, $"https://www.linkedin.com/jobs/view/{jobId}/", ct);

            var body = await page.EvaluateAsync<string?>(
                "() => { const el = document.querySelector('.jobs-description__content, .jobs-box__html-content, #job-details'); return el ? el.innerHTML : null; }");
            return string.IsNullOrWhiteSpace(body) ? null : body;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "LinkedIn: body fetch failed for job {JobId}.", jobId);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Extract up to <paramref name="max"/> job cards, scrolling the results list to lazy-load them.</summary>
    private async Task<IReadOnlyList<LinkedInJobCard>> ExtractCardsAsync(IPage page, int max, CancellationToken ct)
    {
        // Nudge LinkedIn's virtualized list to load more cards, bounded so we never scroll forever.
        for (var i = 0; i < 8; i++)
        {
            ct.ThrowIfCancellationRequested();
            var count = await page.EvaluateAsync<int>(
                "() => document.querySelectorAll('[data-job-id], [data-occludable-job-id]').length");
            if (count >= max)
            {
                break;
            }

            await page.EvaluateAsync("() => window.scrollBy(0, document.body.scrollHeight)");
            await Task.Delay(800, ct);
        }

        var json = await page.EvaluateAsync<string>(
            """
            () => {
              const out = [];
              const seen = new Set();
              const nodes = document.querySelectorAll('[data-job-id], [data-occludable-job-id]');
              for (const n of nodes) {
                const jobId = n.getAttribute('data-job-id') || n.getAttribute('data-occludable-job-id');
                if (!jobId || seen.has(jobId)) continue;
                seen.add(jobId);
                const pick = (sels) => { for (const s of sels) { const e = n.querySelector(s); if (e && e.innerText.trim()) return e.innerText.trim(); } return null; };
                out.push({
                  jobId,
                  title: pick(['.job-card-list__title', '.job-card-container__link', 'a.job-card-list__title--link', 'strong']),
                  company: pick(['.job-card-container__primary-description', '.artdeco-entity-lockup__subtitle', '.job-card-container__company-name']),
                  location: pick(['.job-card-container__metadata-item', '.artdeco-entity-lockup__caption']),
                });
              }
              return JSON.stringify(out);
            }
            """);

        using var doc = JsonDocument.Parse(json);
        var cards = new List<LinkedInJobCard>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var jobId = el.GetProperty("jobId").GetString();
            if (string.IsNullOrWhiteSpace(jobId))
            {
                continue;
            }

            var location = el.GetProperty("location").GetString();
            cards.Add(new LinkedInJobCard(
                jobId,
                Title: el.GetProperty("title").GetString() ?? "(untitled)",
                Company: el.GetProperty("company").GetString() ?? "(unknown)",
                Location: string.IsNullOrWhiteSpace(location) ? null : location,
                WorkMode: LinkedInUrlBuilder.WorkModeFrom(location),
                CanonicalUrl: $"https://www.linkedin.com/jobs/view/{jobId}/"));

            if (cards.Count >= max)
            {
                break;
            }
        }

        return cards;
    }

    /// <summary>Logged in when the authenticated global nav is present and we're not on a login/authwall.</summary>
    private static async Task<bool> IsLoggedInAsync(IBrowserContext context, CancellationToken ct)
    {
        if (context.Pages.Count == 0)
        {
            return false;
        }

        var page = context.Pages[0];
        var url = page.Url ?? string.Empty;
        if (url.Contains("/login", StringComparison.OrdinalIgnoreCase)
            || url.Contains("/authwall", StringComparison.OrdinalIgnoreCase)
            || url.Contains("/checkpoint", StringComparison.OrdinalIgnoreCase)
            || url.Contains("/uas/login", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        await Task.CompletedTask;
        return await page.EvaluateAsync<bool>(
            "() => !!document.querySelector('.global-nav__me, img.global-nav__me-photo, [data-control-name=\"nav.settings\"]')");
    }

    private static async Task<bool> IsBlockedAsync(IPage page)
    {
        var url = page.Url ?? string.Empty;
        if (url.Contains("/checkpoint", StringComparison.OrdinalIgnoreCase)
            || url.Contains("/authwall", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return await page.EvaluateAsync<bool>(
            "() => !!document.querySelector('.challenge, #captcha-internal, [data-test-id=\"challenge\"]')");
    }

    private async Task<IBrowserContext> EnsureContextAsync()
    {
        // Reuse a live persistent context; but a context the user closed (they shut the login window once
        // they think they're signed in — a very common action) or that crashed raises Close, which flips
        // _contextClosed. Without this check we would hand back the dead reference and every subsequent
        // scan would fail (NewPageAsync → TargetClosedError) until a process restart, despite valid cookies
        // still on disk. On a closed context we drop it and relaunch a fresh one from the same profile.
        if (_context is not null && !_contextClosed)
        {
            return _context;
        }

        await DropContextAsync();

        Directory.CreateDirectory(ProfilePath);
        _playwright ??= await Playwright.CreateAsync();
        var context = await _playwright.Chromium.LaunchPersistentContextAsync(ProfilePath, new BrowserTypeLaunchPersistentContextOptions
        {
            Headless = _options.Headless,
            UserAgent = _options.UserAgent,
            Locale = _options.Locale,
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
            Args = ["--disable-blink-features=AutomationControlled"],
        });
        context.Close += OnContextClosed;
        _contextClosed = false;
        _context = context;
        return _context;
    }

    /// <summary>The persistent context closed (window shut / crash) — mark it so the next scan relaunches.</summary>
    private void OnContextClosed(object? sender, IBrowserContext closed) => _contextClosed = true;

    /// <summary>Detach the Close handler and dispose the current context (if any), tolerating an already-closed one.</summary>
    private async Task DropContextAsync()
    {
        if (_context is null)
        {
            return;
        }

        _context.Close -= OnContextClosed;
        try
        {
            await _context.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "LinkedIn: disposing an already-closed persistent context (ignored).");
        }

        _context = null;
        _loggedIn = false;
    }

    private async Task GotoAsync(IPage page, string url, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await page.GotoAsync(url, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = _options.NavigationTimeoutMs,
        });
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
        // Idempotent: the one singleton is captured as a disposable under three DI call sites (concrete +
        // ILinkedInClient + IInteractiveBrowserSession), so container shutdown can dispose it 2–3 times.
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await DropContextAsync();
        _playwright?.Dispose();
        _playwright = null;
        _gate.Dispose();
    }
}

/// <summary>Stable errors surfaced by the LinkedIn client (feature 008).</summary>
internal static class LinkedInErrors
{
    public static readonly Error LoginRequired = new(
        "LinkedInLoginRequired",
        "A LinkedIn login is required. Run a manual scan and sign in to the window that opens.");
}
