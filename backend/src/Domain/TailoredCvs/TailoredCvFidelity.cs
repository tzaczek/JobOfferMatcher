using System.Text.RegularExpressions;

namespace JobOfferMatcher.Domain.TailoredCvs;

/// <summary>
/// A pure, defence-in-depth heuristic for the no-fabrication guarantee (FR-006/SC-002), which has no
/// fully-deterministic test (the worker is an LLM). <see cref="FindAddedTokens"/> returns content words
/// that appear in a generated tailored CV but <b>not</b> in the source CV — case-folded, with HTML markup
/// and common layout/boilerplate words ignored. It is an <i>advisory aid</i> for the manual no-fabrication
/// spot-check (T053), <b>never a runtime gate</b>: a flagged token is a hint to look closer (e.g. a
/// fabricated employer), not proof of fabrication, and an empty result is reassuring but not a guarantee.
/// </summary>
public static partial class TailoredCvFidelity
{
    private const int MinTokenLength = 3;

    /// <summary>HTML elements + common English/CV-layout words the heuristic ignores (not evidence of fabrication).</summary>
    private static readonly HashSet<string> Boilerplate = new(StringComparer.OrdinalIgnoreCase)
    {
        // Markup / layout.
        "html", "head", "body", "div", "span", "style", "class", "page", "main", "aside", "section",
        "header", "footer", "title", "meta", "charset", "doctype", "font", "color", "background", "width",
        "height", "margin", "padding", "border", "grid", "flex", "left", "right", "top", "bottom",
        // Common CV section labels.
        "experience", "education", "skills", "summary", "profile", "contact", "projects", "languages",
        "interests", "references", "curriculum", "vitae", "present", "current",
        // Common English stop words.
        "the", "and", "for", "with", "from", "this", "that", "are", "was", "were", "has", "have", "had",
        "not", "but", "you", "your", "our", "their", "his", "her", "its", "into", "over", "under", "out",
        "all", "any", "can", "will", "would", "should", "could", "more", "most", "such", "than", "then",
        "they", "them", "using", "used", "use", "work", "worked", "working", "role", "roles", "team",
        "years", "year", "month", "months", "since",
    };

    [GeneratedRegex("<(script|style)[^>]*>.*?</\\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StyleScriptBlocks();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTags();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonWord();

    public static IReadOnlyList<string> FindAddedTokens(string generatedText, string sourceCvText)
    {
        var source = new HashSet<string>(Tokenize(sourceCvText), StringComparer.OrdinalIgnoreCase);

        var added = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in Tokenize(StripMarkup(generatedText)))
        {
            if (!Boilerplate.Contains(token) && !source.Contains(token) && seen.Add(token))
            {
                added.Add(token);
            }
        }

        return added;
    }

    private static string StripMarkup(string text) => HtmlTags().Replace(StyleScriptBlocks().Replace(text, " "), " ");

    private static IEnumerable<string> Tokenize(string text) =>
        NonWord()
            .Split(text.ToLowerInvariant())
            .Where(t => t.Length >= MinTokenLength && !t.All(char.IsDigit));
}
