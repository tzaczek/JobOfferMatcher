namespace JobOfferMatcher.Application.Scanning;

/// <summary>
/// Explicit application-layer single-flight (research §2/§3): a scheduled scan and an on-demand
/// scan can collide; that must be a clean queued/rejected result, not a crash. Registered as a
/// singleton (a DI singleton, NOT a static mutable — Forbidden list).
/// </summary>
public sealed class ScanConcurrencyGuard : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>Try to take the single slot without waiting. Returns false if a scan is already running.</summary>
    public Task<bool> TryEnterAsync() => _semaphore.WaitAsync(0);

    /// <summary>
    /// Block-acquire the single slot, waiting up to <paramref name="timeout"/> — used by a restore to
    /// <i>drain</i> an in-flight scan (003 ADR-3). Returns false if the slot stays held past the timeout.
    /// </summary>
    public Task<bool> EnterAsync(TimeSpan timeout, CancellationToken ct = default) => _semaphore.WaitAsync(timeout, ct);

    public void Release() => _semaphore.Release();

    public void Dispose() => _semaphore.Dispose();
}
