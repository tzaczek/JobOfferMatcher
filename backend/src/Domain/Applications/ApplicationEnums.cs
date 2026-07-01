namespace JobOfferMatcher.Domain.Applications;

/// <summary>
/// Whether an application is still being pursued or has been wrapped up (data-model §2). This is a
/// FIXED dimension, orthogonal to the user-configurable <c>pipeline_stage</c> — closing is a decision
/// about the process, not a position in it. Stored <c>HasConversion&lt;string&gt;</c> as varchar(20).
/// </summary>
public enum ApplicationStatus
{
    Active,
    Closed,
}

/// <summary>
/// The fixed terminal outcome of a closed application (data-model §2). Non-null only while
/// <see cref="ApplicationStatus.Closed"/>; nullable varchar(20) column.
/// </summary>
public enum ApplicationOutcome
{
    Accepted,
    Rejected,
    Withdrawn,
    NoResponse,
}

/// <summary>Direction of a logged communication (data-model §2) — varchar(20).</summary>
public enum CommunicationDirection
{
    Inbound,
    Outbound,
}
