using JobOfferMatcher.Infrastructure.Sources.JustJoinIt;

namespace JobOfferMatcher.Infrastructure.Tests;

/// <summary>
/// Unit test (T068): the source-collection User-Agent is a generic product token carrying NO personal
/// data — only a non-PII identifier leaves the machine during collection (Principle IV).
/// </summary>
public sealed class UserAgentPolicyTests
{
    [Fact]
    public void Default_user_agent_is_generic_and_carries_no_pii()
    {
        var ua = new JustJoinItOptions().UserAgent;

        ua.ShouldNotBeNullOrWhiteSpace();
        ua.ShouldStartWith("JobOfferMatcher/"); // generic product token
        ua.ShouldNotContain("@"); // no email address

        foreach (var pii in new[] { "tomasz", "zaczek", "gmail" })
        {
            ua.ToLowerInvariant().ShouldNotContain(pii);
        }
    }
}
