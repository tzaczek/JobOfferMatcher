using JobOfferMatcher.Application.Scanning;

namespace JobOfferMatcher.Application.Tests;

/// <summary>
/// Unit tests for the maintenance gate (003 T029, FR-020): only one backup/restore at a time; a restore
/// marks maintenance active, drains the scan single-flight, and lets deferring writers proceed once it ends.
/// </summary>
public sealed class MaintenanceGateTests
{
    private static MaintenanceGate NewGate(out ScanConcurrencyGuard scan)
    {
        scan = new ScanConcurrencyGuard();
        return new MaintenanceGate(scan);
    }

    [Fact]
    public void Only_one_backup_at_a_time()
    {
        var gate = NewGate(out _);

        gate.TryBeginBackup().ShouldBeTrue();
        gate.TryBeginBackup().ShouldBeFalse();

        gate.EndBackup();
        gate.TryBeginBackup().ShouldBeTrue();
    }

    [Fact]
    public async Task A_backup_blocks_a_restore_and_vice_versa()
    {
        var gate = NewGate(out _);

        gate.TryBeginBackup().ShouldBeTrue();
        (await gate.TryBeginRestoreAsync(TimeSpan.FromMilliseconds(50))).ShouldBeFalse();
        gate.EndBackup();

        (await gate.TryBeginRestoreAsync(TimeSpan.FromSeconds(1))).ShouldBeTrue();
        gate.TryBeginBackup().ShouldBeFalse();
        gate.EndRestore();
    }

    [Fact]
    public async Task A_restore_marks_maintenance_active_until_it_ends()
    {
        var gate = NewGate(out _);
        gate.IsMaintenanceActive.ShouldBeFalse();

        (await gate.TryBeginRestoreAsync(TimeSpan.FromSeconds(1))).ShouldBeTrue();
        gate.IsMaintenanceActive.ShouldBeTrue();

        gate.EndRestore();
        gate.IsMaintenanceActive.ShouldBeFalse();
    }

    [Fact]
    public async Task A_restore_drains_an_in_flight_scan_and_times_out_if_it_cannot()
    {
        var gate = NewGate(out var scan);

        // Simulate an in-flight scan holding the single-flight.
        (await scan.TryEnterAsync()).ShouldBeTrue();

        // Restore cannot drain it within the timeout → busy, and maintenance is NOT left active.
        (await gate.TryBeginRestoreAsync(TimeSpan.FromMilliseconds(100))).ShouldBeFalse();
        gate.IsMaintenanceActive.ShouldBeFalse();

        // Once the scan releases, the restore can drain it.
        scan.Release();
        (await gate.TryBeginRestoreAsync(TimeSpan.FromSeconds(1))).ShouldBeTrue();
        gate.EndRestore();
    }

    [Fact]
    public async Task WaitWhileActive_returns_immediately_when_idle_and_defers_during_a_restore()
    {
        var gate = NewGate(out _);

        // Idle → completes synchronously.
        await gate.WaitWhileActiveAsync().WaitAsync(TimeSpan.FromSeconds(1));

        (await gate.TryBeginRestoreAsync(TimeSpan.FromSeconds(1))).ShouldBeTrue();
        var waiter = gate.WaitWhileActiveAsync();
        waiter.IsCompleted.ShouldBeFalse();

        gate.EndRestore();
        await waiter.WaitAsync(TimeSpan.FromSeconds(1)); // completes once the restore ends
    }
}
