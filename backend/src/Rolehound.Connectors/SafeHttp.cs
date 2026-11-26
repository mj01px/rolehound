using System.Net;
using System.Net.Sockets;

namespace Rolehound.Connectors;

/// <summary>
/// The HTTP handler for sites people add (careers sites): it connects only to
/// public internet addresses. The check runs when the socket opens, after DNS,
/// so a redirect or a name that resolves to an internal address is refused too.
/// </summary>
public static class SafeHttp
{
    public const long MaxResponseBytes = 4 * 1024 * 1024;

    public static SocketsHttpHandler CreateHandler() => new()
    {
        // A proxy from HTTP_PROXY would make the connection, and the address check below would never run.
        UseProxy = false,
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = TimeSpan.FromSeconds(10),
        ConnectCallback = ConnectAsync,
    };

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
        if (addresses.Length == 0 || addresses.Any(a => !IsPublic(a)))
        {
            throw new HttpRequestException($"{context.DnsEndPoint.Host}: endereço fora da internet pública.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// False for loopback, private, link-local (cloud metadata), shared, multicast and reserved ranges.
    /// IPv6 forms that carry an IPv4 address (mapped, compatible, NAT64, 6to4) are judged by that address;
    /// Teredo, whose relay hides the real one, is refused.
    /// </summary>
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var v6 = address.GetAddressBytes();
            if (EmbeddedIPv4(v6) is { } inner)
            {
                return IsPublic(inner);
            }

            // Teredo (2001:0::/32), documentation (2001:db8::/32), local-use NAT64 (64:ff9b:1::/48) and the unspecified address.
            if ((v6[0] == 0x00 && v6[1] == 0x64 && v6[2] == 0xff && v6[3] == 0x9b && v6[4] == 0 && v6[5] == 1)
                || (v6[0] == 0x20 && v6[1] == 0x01 && v6[2] == 0 && v6[3] == 0)
                || (v6[0] == 0x20 && v6[1] == 0x01 && v6[2] == 0x0d && v6[3] == 0xb8)
                || address.Equals(IPAddress.IPv6Any))
            {
                return false;
            }
        }

        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 0
                || b[0] == 10
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                || b[0] == 127
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 198 && (b[1] == 18 || b[1] == 19))
                || b[0] >= 224);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return !(address.Equals(IPAddress.IPv6None)
                || address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6Multicast
                || (b[0] & 0xFE) == 0xFC);
        }

        return false;
    }

    /// <summary>The IPv4 address inside an IPv4-compatible (::a.b.c.d), NAT64 (64:ff9b::/96) or 6to4 (2002::/16) address.</summary>
    private static IPAddress? EmbeddedIPv4(byte[] b)
    {
        var compatible = b.AsSpan(0, 12).IndexOfAnyExcept((byte)0) < 0 && !(b[12] == 0 && b[13] == 0 && b[14] == 0 && b[15] <= 1);
        var nat64 = b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b && b.AsSpan(4, 8).IndexOfAnyExcept((byte)0) < 0;
        if (compatible || nat64)
        {
            return new IPAddress(b.AsSpan(12, 4));
        }

        return b[0] == 0x20 && b[1] == 0x02 ? new IPAddress(b.AsSpan(2, 4)) : null;
    }
}
