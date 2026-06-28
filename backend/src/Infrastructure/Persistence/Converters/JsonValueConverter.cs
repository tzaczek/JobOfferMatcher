using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace JobOfferMatcher.Infrastructure.Persistence.Converters;

/// <summary>
/// Maps a value object (or a read-only list of them) to a PostgreSQL <c>jsonb</c> column via
/// System.Text.Json. Used for raw salary bands, skill lists, derived-profile snapshots, etc.
/// — stored as authoritative JSON without flattening (data-model §PostgreSQL schema).
/// </summary>
public static class JsonColumn
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        // Value objects with validated private constructors need explicit converters.
        options.Converters.Add(new CurrencyJsonConverter());
        return options;
    }

    public static PropertyBuilder<T> HasJsonbConversion<T>(this PropertyBuilder<T> builder)
        where T : class
    {
        var converter = new ValueConverter<T, string>(
            v => JsonSerializer.Serialize(v, Options),
            s => JsonSerializer.Deserialize<T>(s, Options)!);

        var comparer = new ValueComparer<T>(
            (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
            v => JsonSerializer.Serialize(v, Options).GetHashCode(StringComparison.Ordinal),
            v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Options), Options)!);

        builder.HasConversion(converter, comparer).HasColumnType("jsonb");
        return builder;
    }

    /// <summary>
    /// jsonb conversion for an <see cref="IReadOnlyList{T}"/> property: deserializes to a concrete
    /// <c>List&lt;T&gt;</c> so EF can assign it, while the model exposes it read-only.
    /// </summary>
    public static PropertyBuilder<IReadOnlyList<T>> HasJsonbListConversion<T>(this PropertyBuilder<IReadOnlyList<T>> builder)
    {
        var converter = new ValueConverter<IReadOnlyList<T>, string>(
            v => JsonSerializer.Serialize(v, Options),
            s => JsonSerializer.Deserialize<List<T>>(s, Options) ?? new List<T>());

        var comparer = new ValueComparer<IReadOnlyList<T>>(
            (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
            v => JsonSerializer.Serialize(v, Options).GetHashCode(StringComparison.Ordinal),
            v => (IReadOnlyList<T>)(JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(v, Options), Options) ?? new List<T>()));

        builder.HasConversion(converter, comparer).HasColumnType("jsonb");
        return builder;
    }
}
