using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

#pragma warning disable IDE0130
namespace Net4.Tcp;
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
        Logger.Logger.Warn("No network adapters with an IPv4 address found.");
        throw new Exception("No network adapters with an IPv4 address found.");
    }
}

public class TcpPingConfig() {
    public int TimeoutSeconds { get; set; } = 120;
    public int TimeoutDelay { get; set; } = 60000; 
}

public class Packet {
    // Type of the packet 
    public string? Type { get; set; } = null; // Packet type (e.g., "Connect", "Ping", "Message").
    public DateTime Timestamp { get; set; } = DateTime.Now; // Timestamp indicating when packet was created.

    public Packet(string? type) => this.Type = type;

    // Serializes the current object to a JSON string using its runtime type.
    public string Serialize() => JsonSerializer.Serialize(this, GetType());

    // Deserializes a JSON string into a specific Packet type based on the "type" property.
    public static Packet? Deserialize(string json) {
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("type", out var typeProp))
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

public class TcpSocket : IDisposable {
    private TcpClient? Tcpsocket { get; set; } = null;
    private NetworkStream? Tcpstream { get; set; } = null;

    /// <summary>
    /// Constructor that initializes with an existing TcpClient.
    /// </summary>
    public TcpSocket(TcpClient socket) {
        InitNetworkStream();

        this.Tcpsocket = socket;
    }

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
            // Create a StreamReader for reading from the TCP stream
            this._reader = new StreamReader(Tcpstream!, Encoding.UTF8);

            // Create a StreamWriter for writing to the TCP stream (auto-flush enabled)
            var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            _writer = new StreamWriter(Tcpstream!, utf8NoBom) { AutoFlush = true };
        }
        catch (Exception ex) {
            throw new Exception($"NetworkStream unexpected error: {ex}");
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
    public async Task SendAsync(Packet pck) {
        if (Tcpstream == null) {
            Logger.Logger.Warn("NetworkStream is not initiated");
            throw new Exception("NetworkStream is not initiated");
        }
            

        var packet = pck.Serialize() + "\n";
        var data = Encoding.UTF8.GetBytes(packet);

        try {
            await Tcpstream.WriteAsync(data);
            await Tcpstream.FlushAsync();
        }
        catch (IOException ioEx) { // Signal disconnect
            // This happens when the connection is closed/reset
            Disconnect();
            Logger.Logger.Warn($"NetworkStream connection closed: {ioEx}");
            throw new Exception($"NetworkStream connection closed: {ioEx}");
        }
        catch (SocketException sockEx) { // Signal disconnect
            Disconnect();
            Logger.Logger.Error($"NetworkStream socket error: {sockEx}");
            throw new Exception($"NetworkStream socket error: {sockEx}");
        }
        catch (Exception ex) {
            Logger.Logger.Error($"NetworkStream unexpected error: {ex}");
            throw new Exception($"NetworkStream unexpected error: {ex}");
        }
    }

    public async Task<Packet?> RecvAsync() {
        if (Tcpstream == null)
            throw new Exception("NetworkStream is not initiated");

        try {
            using var reader = new StreamReader(Tcpstream, Encoding.UTF8, leaveOpen: true);
            var line = await reader.ReadLineAsync();

            if (string.IsNullOrWhiteSpace(line))
                return null;

            return Packet.Deserialize(line);
        }
        catch (IOException ioEx) { // Signal disconnect
            // This happens when the connection is closed/reset
            Disconnect();
            Logger.Logger.Warn($"NetworkStream connection closed: {ioEx}");
            throw new Exception($"NetworkStream connection closed: {ioEx}");
        }
        catch (SocketException sockEx) { // Signal disconnect
            Disconnect();
            Logger.Logger.Error($"NetworkStream socket error: {sockEx}");
            throw new Exception($"NetworkStream socket error: {sockEx}");
        }
        catch (Exception ex) {
            Logger.Logger.Error($"NetworkStream unexpected error: {ex}");
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
                        Logger.Logger.Warn($"Shutdown unexpected error: {ex}");
                    }
                }

                Tcpsocket.Close();
                Tcpsocket.Dispose();
                Tcpsocket = null;
            }

            _reader?.Dispose();
            _writer?.Dispose();

            Tcpstream?.Close();
            Tcpstream?.Dispose();
            Tcpstream = null;
        }
        catch (Exception ex) {
            Logger.Logger.Error($"Disconnect unexpected error: {ex}");
            throw new Exception($"Disconnect unexpected error: {ex}");
        }

        Logger.Logger.Info($"Disconnected", "TCPSOCKET");
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

