using System.Net;

namespace JobOfferMatcher.Web.Infrastructure;

/// <summary>
/// The load-bearing PII control for <c>/api/enrichment/*</c> (Principle IV / ADR-4): those endpoints
/// serialize CV/offer <i>text</i> over the channel, so they are loopback-only. <b>Fail-closed</b>: a
/// null/unknown remote IP is rejected, not trusted. Used by <c>LoopbackOnlyFilter</c>.
/// </summary>
public static class LoopbackGuard
{
    public static bool IsAllowed(IPAddress? remoteIp) => remoteIp is not null && IPAddress.IsLoopback(remoteIp);
}
