namespace JobOfferMatcher.Domain.Scans;

/// <summary>Completeness of a scan — gates disappearance reconciliation (FR-015, data-model §ScanRun).</summary>
public enum ScanOutcome
{
    Complete,
    Partial,
    Failed,
}

/// <summary>What initiated a scan (FR-020). <see cref="Initial"/> seeds first-run state (research §3).</summary>
public enum TriggerType
{
    Manual,
    Scheduled,
    CatchUp,
    Initial,
}

/// <summary>Why a run was not Complete (FR-036), surfaced to the user.</summary>
public enum IncompleteReason
{
    LoginNotCompleted,
    ChallengeDetected,
    NetworkFailure,
    LayoutChanged,
}
