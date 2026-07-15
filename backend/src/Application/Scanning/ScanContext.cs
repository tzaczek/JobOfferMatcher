using JobOfferMatcher.Domain.Common.Ids;
using JobOfferMatcher.Domain.Scans;

namespace JobOfferMatcher.Application.Scanning;

/// <summary>
/// Scoped, in-memory carrier for the two facts a login-gated adapter needs about the current scan:
/// its <see cref="RunId"/> and whether it may launch an interactive login window (feature 008, ADR-3).
/// Set once by <see cref="ScanOrchestrator"/> at the top of a scan and read by <c>LinkedInSource</c>.
/// <para>
/// Registered <b>scoped</b> — <see cref="IScanRunner"/> is scoped and both scan entry points run in a
/// DI scope (the manual endpoint = request scope; the scheduler = <c>CreateAsyncScope()</c> per tick),
/// so the context is naturally isolated per scan and visible to the adapter built from the same scope.
/// This threads exactly what the login needs <b>without</b> touching <c>IJobSource.CollectAsync</c> or
/// the four existing adapters (Principle X).
/// </para>
/// </summary>
public sealed class ScanContext
{
    public ScanRunId RunId { get; private set; }

    public TriggerType Trigger { get; private set; }

    /// <summary>
    /// Only <b>manual</b> (attended) scans may auto-launch the headed login window and wait for the
    /// user; scheduled/catch-up/initial (unattended) scans must never open a window or hang the
    /// scheduler — they record "login required" instead (feature 008, Clarification Q4 / ADR-3).
    /// </summary>
    public bool AllowInteractiveLogin => Trigger == TriggerType.Manual;

    public void Begin(ScanRunId runId, TriggerType trigger)
    {
        RunId = runId;
        Trigger = trigger;
    }
}
