namespace JobOfferMatcher.Application.Scanning;

/// <summary>
/// Process-wide coordination for backup/restore vs. the background and request-path writers
/// (003 ADR-3 / data-model §6). A <b>backup</b> runs concurrently with scans (MVCC gives a
/// consistent point-in-time) and only serialises against other backup/restore ops; a
/// <b>restore</b> additionally pauses writers and drains the in-flight scan by acquiring the
/// scan single-flight. Registered as a DI singleton (NOT a static mutable — Forbidden list),
/// beside <see cref="ScanConcurrencyGuard"/>.
/// </summary>
public sealed class MaintenanceGate(ScanConcurrencyGuard scanConcurrency) : IDisposable
{
    // At most one backup OR restore at a time. A second attempt sees a non-blocking miss → 409.
    private readonly SemaphoreSlim _slot = new(1, 1);

    // True only for the duration of a restore — writers consult this to defer/reject.
    private volatile bool _maintenanceActive;

    // Completed whenever no restore is active; reset to a pending source while one runs, so a
    // deferring writer awaits exactly until EndRestore (event-driven, no polling).
    private volatile TaskCompletionSource _maintenanceCleared = CreateCompleted();

    /// <summary>True while a restore holds the gate; scanning + enrichment writes defer/reject.</summary>
    public bool IsMaintenanceActive => _maintenanceActive;

    /// <summary>Take the single maintenance slot for a backup without waiting. False if a backup/restore is already running.</summary>
    public bool TryBeginBackup() => _slot.Wait(0);

    /// <summary>Release the slot taken by <see cref="TryBeginBackup"/>.</summary>
    public void EndBackup() => _slot.Release();

    /// <summary>
    /// Take the slot for a restore, mark maintenance active, and drain any in-flight scan by
    /// block-acquiring <see cref="ScanConcurrencyGuard"/> within <paramref name="drainTimeout"/>.
    /// Returns false (and leaves the gate untouched) if another backup/restore holds the slot or a
    /// scan could not be drained in time. Only call <see cref="EndRestore"/> when this returns true.
    /// </summary>
    public async Task<bool> TryBeginRestoreAsync(TimeSpan drainTimeout, CancellationToken ct = default)
    {
        if (!_slot.Wait(0))
        {
            return false;
        }

        _maintenanceActive = true;
        _maintenanceCleared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            if (!await scanConcurrency.EnterAsync(drainTimeout, ct))
            {
                ResetMaintenanceState();
                _slot.Release();
                return false;
            }
        }
        catch
        {
            ResetMaintenanceState();
            _slot.Release();
            throw;
        }

        return true;
    }

    /// <summary>Release the scan single-flight, clear maintenance, and release the slot taken by a restore.</summary>
    public void EndRestore()
    {
        scanConcurrency.Release();
        ResetMaintenanceState();
        _slot.Release();
    }

    /// <summary>
    /// Await until no restore is active (event-driven). Returns immediately when idle; otherwise
    /// completes when <see cref="EndRestore"/> runs. Used by enrichment write-backs to <i>defer</i>
    /// (rather than reject) for the brief restore window (FR-020).
    /// </summary>
    public Task WaitWhileActiveAsync(CancellationToken ct = default) =>
        !_maintenanceActive ? Task.CompletedTask : _maintenanceCleared.Task.WaitAsync(ct);

    public void Dispose() => _slot.Dispose();

    private void ResetMaintenanceState()
    {
        _maintenanceActive = false;
        _maintenanceCleared.TrySetResult();
    }

    private static TaskCompletionSource CreateCompleted()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.SetResult();
        return tcs;
    }
}
