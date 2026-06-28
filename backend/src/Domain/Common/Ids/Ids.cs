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
