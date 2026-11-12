using System.Net;
using System.Net.Sockets;

namespace CortaFeStudio.Api.Services;

/// <summary>
/// Define quem pode acessar o estúdio. Por padrão, só o próprio computador (loopback).
/// Com CORTAFE_ALLOW_PRIVATE_NETWORK=1 (uso em servidor/Docker), também redes privadas e VPN (10.x, 172.16-31.x, 192.168.x, 100.64-127.x).
/// </summary>
public static class RemoteAccessPolicy
{
    public static bool IsAllowed(IPAddress? address, bool allowPrivateNetwork)
    {
        address ??= IPAddress.Loopback;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        if (!allowPrivateNetwork) return false;
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127);
        return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || (bytes[0] & 0xFE) == 0xFC;
    }
}
