using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Net4;

/// <summary>
/// A UDP socket helper for LAN discovery via broadcast.
/// Can send discovery messages and receive server responses.
/// </summary>
public class UdpSocket : IDisposable
{
    public readonly UdpClient _client;
    private readonly int _listenPort;
    private bool _isListening = false;
    private readonly CancellationTokenSource _cts = new();

    // Default broadcast port (choose one not commonly used)
    public const int DefaultBroadcastPort = 44444;

    public UdpSocket(int listenPort = DefaultBroadcastPort)
    {
        _listenPort = listenPort;
        _client = new UdpClient(listenPort);
        _client.EnableBroadcast = true;
    }

    /// <summary>
    /// Gets the local IPv4 address on the LAN (e.g., 192.168.x.x, 10.x.x.x).
    /// Avoids loopback (127.0.0.1) and returns first suitable LAN address.
    /// </summary>
    public static IPAddress? GetLocalLanAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                // Filter out loopback and prefer private IP ranges
                if (!IPAddress.IsLoopback(ip) && IsPrivateIp(ip))
                {
                    return ip;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Checks if an IP address is in a private (LAN) range.
    /// </summary>
    /// <param name="ip">The IP address to check.</param>
    /// <returns>True if private IP, false otherwise.</returns>
    private static bool IsPrivateIp(IPAddress ip)
    {
        var addr = BitConverter.ToUInt32(ip.GetAddressBytes().Reverse().ToArray(), 0);

        // 10.0.0.0/8
        if ((addr & 0xFF000000) == 0x0A000000) return true;
        // 172.16.0.0/12
        if ((addr & 0xFFF00000) == 0xAC100000) return true;
        // 192.168.0.0/16
        if ((addr & 0xFFFF0000) == 0xC0A80000) return true;
        // 169.254.0.0/16 (link-local)
        if ((addr & 0xFFFF0000) == 0xA9FE0000) return true;

        return false;
    }

    public async Task<List<ServerResponse>> DiscoverServersByScanAsync(
        string baseIp = "192.168.89",
        int port = 44444,
        int timeoutMs = 3000) {
        var responses = new List<ServerResponse>();
        var tasks = new List<Task>();

        // Scan typical IP range (1-254)
        for (int i = 1; i <= 254; i++) {
            var ip = $"{baseIp}.{i}";
            if (ip == GetLocalIpAddress())
                continue; // Skip self

            var task = TestServerAsync(ip, port, responses, timeoutMs);
            tasks.Add(task);
        }

        // Wait for all scans to complete (with timeout)
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromMilliseconds(timeoutMs * 2));

        return responses;
    }

    public async Task<List<ServerResponse>> DiscoverServersEnhancedAsync(
        string message = "DISCOVER_SERVER",
        int broadcastPort = DefaultBroadcastPort,
        int timeoutMs = 3000) {
        var allResponses = new List<ServerResponse>();

        Logger.Logger.Info().Cid("UdpSocket").Log("Starting enhanced discovery...");

        // Try localhost first (for same-machine testing)
        var localhostResponses = await DiscoverLocalhostAsync(message, broadcastPort, 1000);
        if (localhostResponses.Any()) {
            allResponses.AddRange(localhostResponses);
            Logger.Logger.Info().Cid("UdpSocket").Log($"Found {localhostResponses.Count} servers on localhost");
            return allResponses;
        }

        // Try broadcast (works on different machines)
        Logger.Logger.Info().Cid("UdpSocket").Log("Attempting broadcast discovery...");
        var broadcastResponses = await DiscoverServersAsync(message, broadcastPort, 2000);

        if (broadcastResponses.Any()) {
            allResponses.AddRange(broadcastResponses);
            Logger.Logger.Info().Cid("UdpSocket").Log($"Found {broadcastResponses.Count} servers via broadcast");
            return allResponses;
        }

        // Fallback to IP scanning (for WiFi networks)
        Logger.Logger.Info().Cid("UdpSocket").Log("Falling back to IP scan...");
        var localIp = GetLocalIpAddress();
        var baseIp = localIp.Substring(0, localIp.LastIndexOf('.'));

        var scanResponses = await DiscoverServersByScanAsync(baseIp, broadcastPort, timeoutMs);
        allResponses.AddRange(scanResponses);

        Logger.Logger.Info().Cid("UdpSocket").Log($"Found {scanResponses.Count} servers via scan");
        return allResponses;
    }

    private async Task TestServerAsync(string ip, int port, List<ServerResponse> responses, int timeoutMs) {
        try {
            using var client = new UdpClient();
            client.Client.ReceiveTimeout = timeoutMs;
            client.Client.SendTimeout = timeoutMs;

            var ep = new IPEndPoint(IPAddress.Parse(ip), port);

            // Send discovery message
            var message = Encoding.UTF8.GetBytes("DISCOVER_SERVER");
            await client.SendAsync(message, message.Length, ep);

            // Wait for response
            var response = await client.ReceiveAsync();
            var responseText = Encoding.UTF8.GetString(response.Buffer);

            ServerResponse? server;
            try {
                server = JsonSerializer.Deserialize<ServerResponse>(responseText);
            }
            catch {
                server = new ServerResponse {
                    Name = responseText.Trim(),
                    Ip = response.RemoteEndPoint.Address.ToString(),
                    Port = response.RemoteEndPoint.Port
                };
            }

            if (server != null) {
                lock (responses)
                    responses.Add(server);
                Logger.Logger.Info().Cid("UdpSocket").Log($"Server found via scan: {server}");
            }
        }
        catch {
            // Timeout or no response - server not found at this IP
        }
    }

    private string GetLocalIpAddress() {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList) {
            if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip)) {
                return ip.ToString();
            }
        }
        return "127.0.0.1";
    }

    /// <summary>
    /// Listens for incoming UDP responses from servers.
    /// </summary>
    private async Task ListenForResponsesAsync(List<ServerResponse> responses, CancellationToken ct)
    {
        if (_isListening) return;
        _isListening = true;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await _client.ReceiveAsync(ct);
                var responseJson = Encoding.UTF8.GetString(result.Buffer);

                // Try to parse as structured response
                ServerResponse? server;
                try
                {
                    server = JsonSerializer.Deserialize<ServerResponse>(responseJson);
                }
                catch
                {
                    // Fallback: treat as plain string
                    server = new ServerResponse
                    {
                        Name = responseJson.Trim(),
                        Ip = result.RemoteEndPoint.Address.ToString(),
                        Port = result.RemoteEndPoint.Port,
                        Timestamp = DateTime.UtcNow
                    };
                }

                if (server != null) {
                    responses.Add(server);
                    Logger.Logger.Info().Cid("UdpSocket").Log($"Server discovered: {server.Name} @ {server.Ip}:{server.Port}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout reached
        }
        catch (ObjectDisposedException)
        {
            // Socket closed
        }
        catch (Exception ex)
        {
            Logger.Logger.Error().Cid("UdpSocket").Log($"Error receiving UDP: {ex.Message}");
        }
        finally
        {
            _isListening = false;
        }
    }

    /// <summary>
    /// Gracefully disposes the UDP socket.
    /// </summary>
    public void Dispose()
    {
        _cts.Cancel();
        _client?.Dispose();
    }
}

/// <summary>
/// Represents a server response during discovery.
/// Can be extended with version, game type, player count, etc.
/// </summary>
public class ServerResponse
{
    public string Name { get; set; } = "Unknown Server";
    public string Ip { get; set; } = "";
    public int Port { get; set; } = 0;
    public string? Version { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public override string ToString()
    {
        return $"{Name} @ {Ip}:{Port}" + (Version != null ? $" (v{Version})" : "");
    }
}