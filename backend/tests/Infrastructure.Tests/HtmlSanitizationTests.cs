using Ganss.Xss;

namespace JobOfferMatcher.Infrastructure.Tests;

/// <summary>
/// Unit test (T068): the HTML-sanitizer policy the app applies to source-supplied offer descriptions
/// strips script execution vectors before the markup can reach a renderer (XSS defense). Uses the same
/// default <see cref="HtmlSanitizer"/> configuration registered in Infrastructure DI.
/// </summary>
public sealed class HtmlSanitizationTests
{
    private static readonly HtmlSanitizer Sanitizer = new();

    [Fact]
    public void Strips_script_tags()
    {
        var clean = Sanitizer.Sanitize("<p>Great role</p><script>alert('xss')</script>");

        clean.ShouldContain("Great role");
        clean.ShouldNotContain("<script", Case.Insensitive);
        clean.ShouldNotContain("alert");
    }

    [Fact]
    public void Strips_inline_event_handlers_and_javascript_urls()
    {
        var clean = Sanitizer.Sanitize(
            "<img src=x onerror=\"alert(1)\"><a href=\"javascript:alert(2)\">click</a>");

        clean.ShouldNotContain("onerror", Case.Insensitive);
        clean.ShouldNotContain("javascript:", Case.Insensitive);
    }

    [Fact]
    public void Keeps_safe_formatting()
    {
        var clean = Sanitizer.Sanitize("<p>Responsibilities:</p><ul><li><strong>C#</strong></li></ul>");

        clean.ShouldContain("<ul>");
        clean.ShouldContain("<strong>");
        clean.ShouldContain("C#");
    }
}
