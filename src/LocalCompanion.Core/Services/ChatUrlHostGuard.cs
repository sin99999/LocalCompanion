using System.Net;
using System.Net.Sockets;

namespace LocalCompanion.Services;

/// <summary>チャット／エージェントの URL 取得向けホスト制限（SSRF 緩和）。</summary>
internal static class ChatUrlHostGuard
{
    public static bool IsBlocked(Uri uri)
    {
        if (uri.Scheme is not ("http" or "https"))
            return true;

        var host = uri.IdnHost;
        if (string.IsNullOrWhiteSpace(host))
            return true;

        if (IsBlockedHostName(host))
            return true;

        if (IPAddress.TryParse(host, out var literal))
            return IsBlockedAddress(literal);

        try
        {
            var addresses = Dns.GetHostAddresses(host);
            foreach (var address in addresses)
            {
                if (IsBlockedAddress(address))
                    return true;
            }
        }
        catch (SocketException)
        {
            // 解決できないホストは取得側でネットワークエラーになる
        }
        catch (ArgumentException)
        {
            return true;
        }

        return false;
    }

    internal static bool IsBlockedHostName(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("metadata.google.internal", StringComparison.OrdinalIgnoreCase))
            return true;

        if (host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    internal static bool IsBlockedAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            // 10.0.0.0/8
            if (bytes[0] == 10)
                return true;
            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                return true;
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
                return true;
            // 169.254.0.0/16 link-local / cloud metadata often
            if (bytes[0] == 169 && bytes[1] == 254)
                return true;
            // 0.0.0.0/8
            if (bytes[0] == 0)
                return true;
            // 127.0.0.0/8 already covered by IsLoopback for 127.0.0.1; cover rest
            if (bytes[0] == 127)
                return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal)
                return true;
            if (address.IsIPv4MappedToIPv6)
                return IsBlockedAddress(address.MapToIPv4());
        }

        return false;
    }
}
