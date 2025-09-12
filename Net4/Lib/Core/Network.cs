using System.Net;
using System.Net.Sockets;

#pragma warning disable IDE0130
namespace Net4;
#pragma warning restore IDE0130

public static class Network {
    // Gets the local machine's IPv4 address.
    public static string LocalIPAddress => GetLocalIPAddress();

    // Retrieves the first available IPv4 address of the local machine.
    // Throws an exception if no IPv4 address is found.ns>
    private static string GetLocalIPAddress() {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList) {
            if (ip.AddressFamily == AddressFamily.InterNetwork) {
                return ip.ToString();
            }
        }
        Logger.Core.Logger.Warn().Log("No network adapters with an IPv4 address found.");
        throw new Exception("No network adapters with an IPv4 address found.");
    }
}