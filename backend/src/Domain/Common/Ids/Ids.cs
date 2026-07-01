using System.Diagnostics.CodeAnalysis;

namespace JobOfferMatcher.Domain.Common.Ids;

/// <summary>
/// Contract for wrapped entity identifiers (Constitution Principle II — no raw <see cref="Guid"/>
/// in Domain/Application). Each id is a <c>readonly record struct</c> over a <see cref="Guid"/>
/// with a <c>New()</c> factory and parse/format helpers.
/// </summary>
public interface IStronglyTypedId<TSelf>
    where TSelf : struct, IStronglyTypedId<TSelf>
{
    Guid Value { get; }
    static abstract TSelf New();
    static abstract TSelf From(Guid value);
}

internal static class StronglyTypedId
{
    public static bool TryParse<TSelf>(string? text, [NotNullWhen(true)] out TSelf id)
        where TSelf : struct, IStronglyTypedId<TSelf>
    {
        if (Guid.TryParse(text, out var guid))
        {
            id = TSelf.From(guid);
            return true;
        }

        id = default;
        return false;
    }
}

public readonly record struct OfferId(Guid Value) : IStronglyTypedId<OfferId>
{
    public static OfferId New() => new(Guid.NewGuid());
    public static OfferId From(Guid value) => new(value);
    public static bool TryParse(string? text, out OfferId id) => StronglyTypedId.TryParse(text, out id);
    public override string ToString() => Value.ToString();
}

public readonly record struct SourceId(Guid Value) : IStronglyTypedId<SourceId>
{
    public static SourceId New() => new(Guid.NewGuid());
    public static SourceId From(Guid value) => new(value);
    public static bool TryParse(string? text, out SourceId id) => StronglyTypedId.TryParse(text, out id);
    public override string ToString() => Value.ToString();
}

public readonly record struct ScanRunId(Guid Value) : IStronglyTypedId<ScanRunId>
{
    public static ScanRunId New() => new(Guid.NewGuid());
    public static ScanRunId From(Guid value) => new(value);
    public static bool TryParse(string? text, out ScanRunId id) => StronglyTypedId.TryParse(text, out id);
    public override string ToString() => Value.ToString();
}

public readonly record struct OfferVersionId(Guid Value) : IStronglyTypedId<OfferVersionId>
{
    public static OfferVersionId New() => new(Guid.NewGuid());
    public static OfferVersionId From(Guid value) => new(value);
    public static bool TryParse(string? text, out OfferVersionId id) => StronglyTypedId.TryParse(text, out id);
    public override string ToString() => Value.ToString();
}

public readonly record struct OfferObservationId(Guid Value) : IStronglyTypedId<OfferObservationId>
{
    public static OfferObservationId New() => new(Guid.NewGuid());
    public static OfferObservationId From(Guid value) => new(value);
    public static bool TryParse(string? text, out OfferObservationId id) => StronglyTypedId.TryParse(text, out id);
    public override string ToString() => Value.ToString();
}

public readonly record struct OfferEventId(Guid Value) : IStronglyTypedId<OfferEventId>
{
    public static OfferEventId New() => new(Guid.NewGuid());
    public static OfferEventId From(Guid value) => new(value);
    public static bool TryParse(string? text, out OfferEventId id) => StronglyTypedId.TryParse(text, out id);
    public override string ToString() => Value.ToString();
}

public readonly record struct RoleGroupId(Guid Value) : IStronglyTypedId<RoleGroupId>
{
    public static RoleGroupId New() => new(Guid.NewGuid());
    public static RoleGroupId From(Guid value) => new(value);
    public static bool TryParse(string? text, out RoleGroupId id) => StronglyTypedId.TryParse(text, out id);
    public override string ToString() => Value.ToString();
}

public readonly record struct CvId(Guid Value) : IStronglyTypedId<CvId>
{
    public static CvId New() => new(Guid.NewGuid());
    public static CvId From(Guid value) => new(value);
    public static bool TryParse(string? text, out CvId id) => StronglyTypedId.TryParse(text, out id);
    public override string ToString() => Value.ToString();
}

// --- Application tracking (005). The JobApplication root reuses OfferId as its key (satellite,
// like OfferFit / tailored_cv); its pipeline stage + the five child tables get their own ids. ---

public readonly record struct PipelineStageId(Guid Value) : IStronglyTypedId<PipelineStageId>
{
    public static PipelineStageId New() => new(Guid.NewGuid());
    public static PipelineStageId From(Guid value) => new(value);
    public static bool TryParse(string? text, out PipelineStageId id) => StronglyTypedId.TryParse(text, out id);
    public override string ToString() => Value.ToString();
}

public readonly record struct ApplicationNoteId(Guid Value) : IStronglyTypedId<ApplicationNoteId>
{
    public static ApplicationNoteId New() => new(Guid.NewGuid());
    public static ApplicationNoteId From(Guid value) => new(value);
    public static bool TryParse(string? text, out ApplicationNoteId id) => StronglyTypedId.TryParse(text, out id);
    public override string ToString() => Value.ToString();
}

public readonly record struct ApplicationTaskId(Guid Value) : IStronglyTypedId<ApplicationTaskId>
{
    public static ApplicationTaskId New() => new(Guid.NewGuid());
    public static ApplicationTaskId From(Guid value) => new(value);
    public static bool TryParse(string? text, out ApplicationTaskId id) => StronglyTypedId.TryParse(text, out id);
    public override string ToString() => Value.ToString();
}

public readonly record struct ApplicationDocumentId(Guid Value) : IStronglyTypedId<ApplicationDocumentId>
{
    public static ApplicationDocumentId New() => new(Guid.NewGuid());
    public static ApplicationDocumentId From(Guid value) => new(value);
    public static bool TryParse(string? text, out ApplicationDocumentId id) => StronglyTypedId.TryParse(text, out id);
    public override string ToString() => Value.ToString();
}

public readonly record struct ApplicationCommunicationId(Guid Value) : IStronglyTypedId<ApplicationCommunicationId>
{
    public static ApplicationCommunicationId New() => new(Guid.NewGuid());
    public static ApplicationCommunicationId From(Guid value) => new(value);
    public static bool TryParse(string? text, out ApplicationCommunicationId id) => StronglyTypedId.TryParse(text, out id);
    public override string ToString() => Value.ToString();
}

public readonly record struct ApplicationInterviewId(Guid Value) : IStronglyTypedId<ApplicationInterviewId>
{
    public static ApplicationInterviewId New() => new(Guid.NewGuid());
    public static ApplicationInterviewId From(Guid value) => new(value);
    public static bool TryParse(string? text, out ApplicationInterviewId id) => StronglyTypedId.TryParse(text, out id);
    public override string ToString() => Value.ToString();
}
