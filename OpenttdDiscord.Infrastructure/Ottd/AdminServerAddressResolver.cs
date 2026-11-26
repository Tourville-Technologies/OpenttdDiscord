using System.Net;
using System.Net.Sockets;

namespace OpenttdDiscord.Infrastructure.Ottd;

internal static class AdminServerAddressResolver
{
    public static string Resolve(string address, Func<string, IPAddress[]>? lookup = null)
    {
        if (IPAddress.TryParse(address, out _))
        {
            return address;
        }

        IPAddress[] addresses = (lookup ?? Dns.GetHostAddresses)(address);
        IPAddress? resolved = addresses.FirstOrDefault(candidate => candidate.AddressFamily == AddressFamily.InterNetwork)
            ?? addresses.FirstOrDefault(candidate => candidate.AddressFamily == AddressFamily.InterNetworkV6);
        if (resolved is null)
        {
            throw new SocketException((int)SocketError.HostNotFound);
        }

        return resolved.ToString();
    }
}
