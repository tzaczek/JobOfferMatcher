using System.Runtime.CompilerServices;

namespace JobOfferMatcher.Domain.Common;

/// <summary>
/// Guards for genuinely exceptional invariants (programmer errors) — these throw.
/// Expected, user-facing invalid input is modelled with <see cref="Result{T}"/> instead
/// (e.g. value-object construction returns <c>Result.Failure</c>, never throws).
/// </summary>
public static class Guard
{
    public static T AgainstNull<T>(T? value, [CallerArgumentExpression(nameof(value))] string? name = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value, name);
        return value;
    }

    public static string AgainstNullOrEmpty(string? value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(value, name);
        return value;
    }

    public static string AgainstNullOrWhiteSpace(string? value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value;
    }

    public static decimal AgainstNegative(decimal value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "Value must not be negative.");
        }

        return value;
    }

    public static int AgainstOutOfRange(int value, int min, int max, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value < min || value > max)
        {
            throw new ArgumentOutOfRangeException(name, value, $"Value must be within [{min}, {max}].");
        }

        return value;
    }
}
