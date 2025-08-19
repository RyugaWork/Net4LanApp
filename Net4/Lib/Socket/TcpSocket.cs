using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

#pragma warning disable IDE0130
namespace Net4;
#pragma warning restore IDE0130


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

