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

    // ---- Container mode (003): trust the Docker-gateway source behind a host-loopback-bound port ----

    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("172.17.0.1")]   // typical Docker bridge gateway
    [InlineData("172.31.255.1")]
    [InlineData("192.168.65.1")] // Docker Desktop gateway
    [InlineData("169.254.1.1")]  // link-local
    public void Private_sources_are_admitted_only_when_trust_is_enabled(string ip)
    {
        var address = IPAddress.Parse(ip);
        LoopbackGuard.IsAllowed(address, trustPrivateNetwork: false).ShouldBeFalse();
        LoopbackGuard.IsAllowed(address, trustPrivateNetwork: true).ShouldBeTrue();
    }

    [Fact]
    public void Public_addresses_are_rejected_even_when_trust_is_enabled()
    {
        LoopbackGuard.IsAllowed(IPAddress.Parse("8.8.8.8"), trustPrivateNetwork: true).ShouldBeFalse();
        LoopbackGuard.IsAllowed(IPAddress.Parse("203.0.113.7"), trustPrivateNetwork: true).ShouldBeFalse();
    }

    [Fact]
    public void Loopback_stays_allowed_and_null_stays_rejected_regardless_of_trust()
    {
        LoopbackGuard.IsAllowed(IPAddress.Loopback, trustPrivateNetwork: true).ShouldBeTrue();
        LoopbackGuard.IsAllowed(null, trustPrivateNetwork: true).ShouldBeFalse(); // fail-closed
    }
}
