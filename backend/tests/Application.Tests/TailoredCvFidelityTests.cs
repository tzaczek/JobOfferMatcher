using JobOfferMatcher.Domain.TailoredCvs;

namespace JobOfferMatcher.Application.Tests;

/// <summary>
/// No-fabrication fidelity heuristic tests (T052, defence-in-depth for FR-006/SC-002): generated HTML
/// drawn only from the source CV flags no added content tokens; HTML injecting a fabricated employer is
/// flagged. This aids the T053 manual review — it is not a runtime gate.
/// </summary>
public sealed class TailoredCvFidelityTests
{
    private const string SourceCv =
        "Jan Kowalski. Senior Backend Engineer at Acme Software in Krakow. " +
        "Built payment services with dotnet, PostgreSQL and Kafka. Earlier at Globex as a developer.";

    [Fact]
    public void In_source_html_flags_no_added_content_tokens()
    {
        const string generated =
            "<!doctype html><html><body><h1>Jan Kowalski</h1>" +
            "<p>Senior Backend Engineer at Acme Software, Krakow. Built payment services with PostgreSQL and Kafka.</p>" +
            "<p>Earlier: developer at Globex.</p></body></html>";

        TailoredCvFidelity.FindAddedTokens(generated, SourceCv).ShouldBeEmpty();
    }

    [Fact]
    public void Html_injecting_a_fabricated_employer_is_flagged()
    {
        const string generated =
            "<!doctype html><html><body><h1>Jan Kowalski</h1>" +
            "<p>Senior Backend Engineer at Microsoft, building payment services.</p></body></html>";

        var added = TailoredCvFidelity.FindAddedTokens(generated, SourceCv);

        added.ShouldContain(t => t.Equals("microsoft", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Markup_and_layout_words_are_not_flagged()
    {
        const string generated =
            "<html><head><style>.page{height:297mm}</style></head><body><aside>Skills</aside></body></html>";

        // Only markup/layout/boilerplate tokens — nothing content-bearing is added.
        TailoredCvFidelity.FindAddedTokens(generated, SourceCv).ShouldBeEmpty();
    }
}
