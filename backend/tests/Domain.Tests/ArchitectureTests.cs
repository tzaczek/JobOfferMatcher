using System.Reflection;
using JobOfferMatcher.Domain.Common;

namespace JobOfferMatcher.Domain.Tests;

/// <summary>
/// Architecture test (T067): the Domain layer is framework-free and dependencies point INWARD
/// (Principle I). It reflects over the compiled Domain assembly's referenced assemblies and asserts
/// every one is either the BCL/runtime or Domain itself — an ALLOW-list, so introducing ANY third-
/// party or outer-layer dependency (not just the few we can name today) fails the test. Implemented
/// with plain reflection — no extra dependency (Principle X — Simple by Default).
/// </summary>
public sealed class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(Result).Assembly;

    /// <summary>Assembly-name prefixes that are part of the BCL/runtime (allowed for a framework-free Domain).</summary>
    private static readonly string[] AllowedBclPrefixes =
    [
        "System",          // System.*, System.Private.CoreLib, etc.
        "mscorlib",
        "netstandard",
        "Microsoft.CSharp", // C# language runtime support (BCL)
        "Microsoft.Win32",  // BCL registry/interop surface
        "WindowsBase",
    ];

    [Fact]
    public void Domain_references_only_the_bcl_and_itself()
    {
        var disallowed = Domain.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => name.Length > 0)
            .Where(name => !IsAllowed(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        disallowed.ShouldBeEmpty(
            "Domain must stay framework-free (dependencies point inward, Principle I). "
            + "Unexpected references outside the BCL/Domain: " + string.Join(", ", disallowed));
    }

    private static bool IsAllowed(string assemblyName) =>
        assemblyName == "JobOfferMatcher.Domain"
        || AllowedBclPrefixes.Any(p => assemblyName == p || assemblyName.StartsWith(p + ".", StringComparison.Ordinal));
}
