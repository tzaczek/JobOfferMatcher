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
            await GotoAsync(page, _options.BodyUrlTemplate.Replace("{jobId}", jobId, StringComparison.Ordinal), ct);
            return await ReadBodyAsync(page, ct);
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

    /// <summary>
    /// Read the job description from the two-pane detail pane. It hydrates async and briefly shows just an
    /// "About the job" stub before the full text streams in, so poll for substantial content (rejecting the
    /// stub) for a few seconds. Returns null (tolerated → "not available", feature 006) if it never fills.
    /// </summary>
    private static async Task<string?> ReadBodyAsync(IPage page, CancellationToken ct)
    {
        // The pane first shows an "About the job" skeleton — lots of empty markup but only ~a dozen chars of
        // TEXT — before the real description streams in. Gate on the text length (not the html length, which
        // the skeleton inflates); return the html only once real text is present, else null ("not available").
        const string js =
            "() => { const el = document.querySelector('.jobs-description__content, .jobs-box__html-content, #job-details'); return el ? JSON.stringify({ html: el.innerHTML, text: (el.innerText || '').trim().length }) : null; }";

        string? bestHtml = null;
        var bestText = 0;
        for (var i = 0; i < 14; i++)
        {
            ct.ThrowIfCancellationRequested();
            var raw = await page.EvaluateAsync<string?>(js);
            if (raw is not null)
            {
                using var doc = JsonDocument.Parse(raw);
                var text = doc.RootElement.GetProperty("text").GetInt32();
                if (text > bestText)
                {
                    bestText = text;
                    bestHtml = doc.RootElement.GetProperty("html").GetString();
                }
            }

            if (bestText > 300)
            {
                break; // real description present (well past the "About the job" skeleton)
            }

            await Task.Delay(600, ct);
        }

        return bestText > 200 ? bestHtml : null;
    }

    /// <summary>
    /// Extract up to <paramref name="max"/> job cards. LinkedIn virtualizes the results list — scrolling
    /// past a card strips its inner content, leaving only the <c>data-occludable-job-id</c> shell (so the
    /// job id + canonical URL survive but title/company/location are gone). A one-shot extract after
    /// scrolling therefore loses every card already occluded; instead we extract at each scroll step and
    /// merge by job id, capturing each card's fields while it is still rendered.
    /// </summary>
    private async Task<IReadOnlyList<LinkedInJobCard>> ExtractCardsAsync(IPage page, int max, CancellationToken ct)
    {
        // Initial settle: let the first result batch render before the first extraction.
        await Task.Delay(2000, ct);

        var byId = new Dictionary<string, LinkedInJobCard>(StringComparer.Ordinal);
        var order = new List<string>(); // preserve first-seen (feed) order
        var cursor = 0;
        var previousTitled = -1;
        var stalls = 0;

        for (var step = 0; step < 60 && CountTitled(byId) < max; step++)
        {
            ct.ThrowIfCancellationRequested();

            var json = await page.EvaluateAsync<string>(ExtractStepJs);
            using (var doc = JsonDocument.Parse(json))
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var jobId = el.GetProperty("jobId").GetString();
                    if (string.IsNullOrWhiteSpace(jobId))
                    {
                        continue;
                    }

                    // Keep the first titled reading; upgrade an untitled placeholder once the card renders.
                    if (byId.TryGetValue(jobId, out var existing) && existing.Title != "(untitled)")
                    {
                        continue;
                    }

                    if (!byId.ContainsKey(jobId))
                    {
                        order.Add(jobId);
                    }

                    byId[jobId] = MakeCard(
                        jobId,
                        el.GetProperty("title").GetString(),
                        el.GetProperty("company").GetString(),
                        el.GetProperty("location").GetString());
                }
            }

            var titled = CountTitled(byId);
            if (titled >= max)
            {
                break;
            }

            // Walk a cursor down the shell list so each card is centred (and renders its content) once — a
            // jump-to-bottom scroll skips the middle cards, which stay occluded and untitled.
            var shellCount = await page.EvaluateAsync<int>(
                "() => document.querySelectorAll('[data-occludable-job-id]').length");
            if (cursor >= shellCount)
            {
                // Walked the whole loaded list; stop once titles stop appearing (reached the end).
                if (titled == previousTitled)
                {
                    if (++stalls >= 3)
                    {
                        break;
                    }
                }
                else
                {
                    stalls = 0;
                    previousTitled = titled;
                }
            }
            else
            {
                stalls = 0;
            }

            await page.EvaluateAsync(
                "(idx) => { const ns = document.querySelectorAll('[data-occludable-job-id]'); if (ns.length) ns[Math.min(idx, ns.length - 1)].scrollIntoView({ block: 'center' }); else window.scrollBy(0, 700); }",
                cursor);
            cursor += 4;
            await Task.Delay(750, ct);
        }

        return order.Take(max).Select(id => byId[id]).ToList();
    }

    private static int CountTitled(Dictionary<string, LinkedInJobCard> byId)
    {
        var n = 0;
        foreach (var c in byId.Values)
        {
            if (c.Title != "(untitled)")
            {
                n++;
            }
        }

        return n;
    }

    private static LinkedInJobCard MakeCard(string jobId, string? title, string? company, string? location) => new(
        jobId,
        Title: string.IsNullOrWhiteSpace(title) ? "(untitled)" : title,
        Company: string.IsNullOrWhiteSpace(company) ? "(unknown)" : company,
        Location: string.IsNullOrWhiteSpace(location) ? null : location,
        WorkMode: LinkedInUrlBuilder.WorkModeFrom(location),
        CanonicalUrl: $"https://www.linkedin.com/jobs/view/{jobId}/");

    /// <summary>
    /// JS run at each scroll step: extract every currently-rendered card's fields (LinkedIn two-pane list
    /// DOM, 2026). The title link nests a visible <c>aria-hidden</c> span plus a visually-hidden
    /// "… with verification" duplicate — read the former (fallback: the aria-label, suffix stripped).
    /// </summary>
    private const string ExtractStepJs =
        """
        () => {
          const out = [];
          const seen = new Set();
          const nodes = document.querySelectorAll('[data-occludable-job-id], [data-job-id]');
          for (const n of nodes) {
            const jobId = n.getAttribute('data-occludable-job-id') || n.getAttribute('data-job-id');
            if (!jobId || seen.has(jobId)) continue;
            seen.add(jobId);
            const a = n.querySelector('a.job-card-list__title--link, a.job-card-container__link');
            let title = '';
            if (a) {
              const vis = a.querySelector('span[aria-hidden="true"]');
              title = (vis ? vis.innerText : (a.getAttribute('aria-label') || '')).trim();
              title = title.replace(/\s+with verification$/i, '').trim();
            }
            const pick = (sels) => { for (const s of sels) { const e = n.querySelector(s); if (e && e.innerText.trim()) return e.innerText.trim(); } return null; };
            const company = pick(['.artdeco-entity-lockup__subtitle', '.job-card-container__primary-description', '.job-card-container__company-name']);
            const location = pick(['.artdeco-entity-lockup__caption', '.job-card-container__metadata-wrapper', '.job-card-container__metadata-item']);
            out.push({ jobId, title, company, location });
          }
          return JSON.stringify(out);
        }
        """;

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
