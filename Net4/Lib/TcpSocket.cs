using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

#pragma warning disable IDE0130
namespace Net4;
#pragma warning restore IDE0130

public static class Network {
    // Gets the local machine's IPv4 address.
    public static string LocalIPAddress => GetLocalIPAddress();
    public static IPAddress? LocalLanAddress => GetLocalLanAddress();

    // Retrieves the first available IPv4 address of the local machine.
    // Throws an exception if no IPv4 address is found.ns>
    private static string GetLocalIPAddress() {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList) {
            if (ip.AddressFamily == AddressFamily.InterNetwork) {
                return ip.ToString();
            }
        }
        Logger.Logger.Warn().Log("No network adapters with an IPv4 address found.");
        throw new Exception("No network adapters with an IPv4 address found.");
    }

    /// <summary>
    /// Gets the local IPv4 address on the LAN (e.g., 192.168.x.x, 10.x.x.x).
    ///Avoids loopback (127.0.0.1) and returns first suitab le LAN address.
    /// </summary>
    private static IPAddress? GetLocalLanAddress() {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList) {
            if (ip.AddressFamily == AddressFamily.InterNetwork) {
                // Filter out loopback and prefer private IP ranges
                if (!IPAddress.IsLoopback(ip) && IsPrivateIp(ip)) {
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
    private static bool IsPrivateIp(IPAddress ip) {
        var addr = BitConverter.ToUInt32(ip.GetAddressBytes().Reverse().ToArray(), 0);

        // 10.0.0.0/8
        if ((addr & 0xFF000000) == 0x0A000000)
            return true;
        // 172.16.0.0/12
        if ((addr & 0xFFF00000) == 0xAC100000)
            return true;
        // 192.168.0.0/16
        if ((addr & 0xFFFF0000) == 0xC0A80000)
            return true;
        // 169.254.0.0/16 (link-local)
        if ((addr & 0xFFFF0000) == 0xA9FE0000)
            return true;

        return false;
    }
}

public class TcpPingConfig() {
    public int TimeoutSeconds { get; set; } = 120;
    public int TimeoutDelay { get; set; } = 60000; 
}

public static class Json {
    public static readonly JsonSerializerOptions Options = new() {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
}

public class Packet {
    // Type of the packet 
    public string Type { get; set; } = ""; // Packet type (e.g., "Connect", "Ping", "Message").
    public DateTime Timestamp { get; set; } = DateTime.UtcNow; // Timestamp indicating when packet was created.

    public Packet(string type) => this.Type = type;

    // Serializes the current object to a JSON string using its runtime type.
    public string Serialize() => JsonSerializer.Serialize(this, GetType(), Json.Options);

    // Deserializes a JSON string into a specific Packet type based on the "type" property.
    public static Packet? Deserialize(string json) {
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("Type", out var typeProp))
            return null;

        string? type = typeProp.GetString();
        return type switch {
            "Message" => JsonSerializer.Deserialize<Tcp_Mess_Pck>(json),
            _ => JsonSerializer.Deserialize<Packet>(json)
        };
    }
}

public class Tcp_Mess_Pck : Packet {
    public required string? Text { get; set; } = null; // Message content
    public required string? Sender { get; set; } = null; // Sender's identifier (e.g., username)

    public Tcp_Mess_Pck() : base("Message") { }
}

public class TcpSocket(TcpClient socket) : IDisposable {
    public readonly TcpClient Tcpsocket = socket;
    private NetworkStream? Tcpstream { get; set; } = null;

    public bool IsConnected => this.Tcpsocket!.Connected; // Checks if the TCP socket is connected.

    /// <summary>
    /// Initializes the network stream by setting up the reader and writer with UTF-8 encoding.
    /// </summary>
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public void InitNetworkStream() {
        //this.Tcpsocket!.NoDelay = true;
        this.Tcpstream = Tcpsocket!.GetStream();

        try {
            // Reuse the same StreamReader/Writer
            this._reader = new StreamReader(Tcpstream, Encoding.UTF8);
            var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            _writer = new StreamWriter(Tcpstream, utf8NoBom) { AutoFlush = true };
        }
        catch (Exception ex) {
            Logger.Logger.Error().Log($"NetworkStream init error: {ex}");
            throw new InvalidOperationException($"Failed to initialize network stream: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Duration (seconds) to determine if the client is still 
    /// considered alive based on the time of the last received ping.
    /// </summary>
    public TcpPingConfig _config = new TcpPingConfig();
    private DateTime _lastping = DateTime.UtcNow;
    public bool IsAlive => (DateTime.UtcNow - _lastping).TotalSeconds <= _config.TimeoutSeconds;
    public void UpdateLastPing() => _lastping = DateTime.UtcNow;

    /// <summary>
    /// Recive, Sent packet asynchronously over the TCP stream.
    /// </summary>
    public async Task SendAsync(Packet packet) {
        if (Tcpstream == null) {
            Logger.Logger.Warn().Log("NetworkStream is not initiated");
            throw new Exception("NetworkStream is not initiated");
        }

        var json = packet.Serialize();

        try {
            Logger.Logger.Info().Cid("Send").Log($"{packet}");
            await _writer!.WriteLineAsync(json);
        }
        catch (IOException ioEx) { // Signal disconnect
            // This happens when the connection is closed/reset
            Disconnect();
            Logger.Logger.Warn().Log($"NetworkStream connection closed: {ioEx}");
            throw new Exception($"NetworkStream connection closed: {ioEx}");
        }
        catch (SocketException sockEx) { // Signal disconnect
            Disconnect();
            Logger.Logger.Error().Log($"NetworkStream socket error: {sockEx}");
            throw new Exception($"NetworkStream socket error: {sockEx}");
        }
        catch (Exception ex) {
            Logger.Logger.Error().Log($"NetworkStream unexpected error: {ex}");
            throw new Exception($"NetworkStream unexpected error: {ex}");
        }
    }

    public async Task<Packet?> RecvAsync() {
        if (Tcpstream == null) {
            Logger.Logger.Warn().Log("NetworkStream is not initiated");
            throw new InvalidOperationException("NetworkStream is not initialized. Call InitNetworkStream first.");
        }

        try {
            var line = await _reader!.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line))
                return null;

            var packet = Packet.Deserialize(line);
            if (packet != null)
                Logger.Logger.Info().Cid("Recv").Log($"{packet.Serialize()}");
            return packet;
        }
        catch (IOException ioEx) { // Signal disconnect
            // This happens when the connection is closed/reset
            Disconnect();
            Logger.Logger.Warn().Log($"NetworkStream connection closed: {ioEx}");
            throw new Exception($"NetworkStream connection closed: {ioEx}");
        }
        catch (SocketException sockEx) { // Signal disconnect
            Disconnect();
            Logger.Logger.Error().Log($"NetworkStream socket error: {sockEx}");
            throw new Exception($"NetworkStream socket error: {sockEx}");
        }
        catch (Exception ex) {
            Logger.Logger.Error().Log($"NetworkStream unexpected error: {ex}");
            throw new Exception($"NetworkStream unexpected error: {ex}");
        }
    }

    /// <summary>
    /// Disconnects the TCP socket and disposes of its resources.
    /// </summary>
    public void Disconnect() {
        try {
            if (Tcpsocket != null) {
                if (Tcpsocket.Connected) {
                    try {
                        Tcpsocket.Client.Shutdown(SocketShutdown.Both);
                    }
                    catch (Exception ex) {
                        /* ignore shutdown errors */
                        Logger.Logger.Warn().Log($"Shutdown unexpected error: {ex}");
                    }
                }

                Tcpsocket.Close();
                Tcpsocket.Dispose();
            }

            _reader?.Dispose();
            _writer?.Dispose();

            Tcpstream?.Close();
            Tcpstream?.Dispose();
            Tcpstream = null;
        }
        catch (Exception ex) {
            Logger.Logger.Error().Log($"Disconnect unexpected error: {ex}");
            throw new Exception($"Disconnect unexpected error: {ex}");
        }

        Logger.Logger.Info().Cid("TcpSocket").Log($"Disconnected");
    }

    private bool disposed = false; // to detect redundant calls
    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this); // prevent finalizer from running again
    }

    protected virtual void Dispose(bool disposing) {
        if (disposed)
            return;
        disposed = true;

        if (disposing) {
            // Dispose managed resources
            Disconnect();
        }

        // No unmanaged resources directly, so nothing special here
    }

    ~TcpSocket() {
        Dispose(false); // cleanup unmanaged only (but we don't have any here)
    }
}

