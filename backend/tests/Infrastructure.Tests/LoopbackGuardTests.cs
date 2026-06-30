using System.Net;
using JobOfferMatcher.Web.Infrastructure;

namespace JobOfferMatcher.Infrastructure.Tests;

/// <summary>
/// Loopback-guard unit test (T021): the <c>/api/enrichment/*</c> PII control is <b>fail-closed</b> —
/// only genuine loopback IPs pass; null/unknown and any routable address are rejected (FR-012/SC-005).
/// </summary>
public sealed class LoopbackGuardTests
{
    [Fact]
    public void Loopback_ipv4_and_ipv6_are_allowed()
    {
        LoopbackGuard.IsAllowed(IPAddress.Loopback).ShouldBeTrue();        // 127.0.0.1
        LoopbackGuard.IsAllowed(IPAddress.IPv6Loopback).ShouldBeTrue();    // ::1
        LoopbackGuard.IsAllowed(IPAddress.Parse("127.0.0.5")).ShouldBeTrue();
    }

    [Fact]
    public void Null_or_unknown_remote_ip_is_rejected_fail_closed()
    {
        LoopbackGuard.IsAllowed(null).ShouldBeFalse();
    }

    [Fact]
    public void Routable_addresses_are_rejected()
    {
        LoopbackGuard.IsAllowed(IPAddress.Parse("10.0.0.5")).ShouldBeFalse();
        LoopbackGuard.IsAllowed(IPAddress.Parse("192.168.1.20")).ShouldBeFalse();
        LoopbackGuard.IsAllowed(IPAddress.Parse("8.8.8.8")).ShouldBeFalse();
    }
}
