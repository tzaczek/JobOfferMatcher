using System.Net;
using System.Net.Sockets;

namespace JobOfferMatcher.Web.Infrastructure;

/// <summary>
/// The load-bearing PII control for the loopback-only groups (<c>/api/enrichment/*</c>,
/// <c>/api/backup/*</c>) (Principle IV / ADR-4): those endpoints serialize CV/offer/DB <i>text</i>, so
/// they stay on the local machine. <b>Fail-closed</b>: a null/unknown remote IP is rejected.
/// <para>
/// In <b>host mode</b> the browser/worker hits <c>127.0.0.1</c> directly, so plain loopback suffices.
/// In <b>container mode</b> the host reaches the app through the Docker bridge gateway (a private
/// address, not loopback); there we (a) publish the port to the host loopback <b>only</b> and (b) set
/// <c>Loopback:TrustPrivateNetwork=true</c> so the app additionally trusts private-network sources. The
/// two settings are a pair — trusting private sources is safe <i>only</i> because the port is bound to
/// the host loopback, so nothing off-machine can reach it.
/// </para>
/// </summary>
public static class LoopbackGuard
{
    /// <summary>Strict loopback check (host mode).</summary>
    public static bool IsAllowed(IPAddress? remoteIp) => IsAllowed(remoteIp, trustPrivateNetwork: false);

    /// <summary>
    /// Loopback check that, when <paramref name="trustPrivateNetwork"/> is true (container mode behind a
    /// host-loopback-bound port), also admits private/link-local sources — i.e. the Docker gateway that
    /// host-published traffic arrives from. Public/routable addresses are always rejected.
    /// </summary>
    public static bool IsAllowed(IPAddress? remoteIp, bool trustPrivateNetwork)
    {
        if (remoteIp is null)
        {
            return false;
        }

        if (IPAddress.IsLoopback(remoteIp))
        {
            return true;
        }

        return trustPrivateNetwork && IsPrivateOrLinkLocal(remoteIp);
    }

    /// <summary>True for RFC-1918 / link-local IPv4 and IPv6 unique-local/link-local — never public addresses.</summary>
    private static bool IsPrivateOrLinkLocal(IPAddress ip)
    {
        if (ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal)
        {
            return true;
        }

        var v4 = ip.AddressFamily == AddressFamily.InterNetwork
            ? ip
            : ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : null;
        if (v4 is null)
        {
            return false;
        }

        var b = v4.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] is >= 16 and <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254);
    }
}
