using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Net4;

public class ServerBase : IDisposable {
    private readonly int _port;
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private TcpListener? _tcpListener;
    private UdpClient? _udpClient;
    private List<ClientBase> _clients = new List<ClientBase>();
    private Task? _listenerTask;
    private Task? _udpTask;

    public int ListeningPort => _port;

    public ServerBase(int port = 0) {
        _port = port;
    }

    public void Start() {
        // Start TCP listener
        var localIp = IPAddress.Parse(Network.LocalIPAddress);
        _tcpListener = new TcpListener(localIp, _port);
        _tcpListener.Start();

        var actualPort = ((IPEndPoint)_tcpListener.LocalEndpoint).Port;
        Logger.Logger.Info().Cid("Server").Log($"Server TCP listening on {Network.LocalIPAddress}:{actualPort}");

        // Start UDP discovery responder on standard port
        _udpTask = Task.Run(UdpDiscoveryResponder, _cts.Token);

        // Start TCP client listener
        _listenerTask = Task.Run(Listener, _cts.Token);
    }

    /// <summary>
    /// Responds to UDP broadcast discovery messages
    /// </summary>
    private async Task UdpDiscoveryResponder() {
        const string discoveryMessage = "DISCOVER_SERVER";
        const int discoveryPort = UdpSocket.DefaultBroadcastPort; // 44444

        try {
            // Bind to all interfaces to handle both network and localhost requests
            _udpClient = new UdpClient(discoveryPort);
            _udpClient.EnableBroadcast = true;

            Logger.Logger.Info().Cid("Discovery").Log($"UDP discovery responder started on port {discoveryPort}");
            // ... rest of your existing code
        }
        catch (Exception ex) {
            // If port is in use, try binding to localhost only
            try {
                _udpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, discoveryPort));
                _udpClient.EnableBroadcast = true;
                Logger.Logger.Info().Cid("Discovery").Log($"UDP discovery responder started on localhost:{discoveryPort}");
            }
            catch (Exception ex2) {
                Logger.Logger.Error().Cid("Discovery")
                    .Log($"Failed to start UDP discovery on any interface: {ex.Message}, {ex2.Message}");
                return;
            }
        }

        // ... rest of your existing code
    }

    private void Listener() {
        try {
            Logger.Logger.Info().Cid("Listener").Log("TCP listener started");

            while (!_cts.Token.IsCancellationRequested && _tcpListener != null) {
                try {
                    var tcpClient = _tcpListener.AcceptTcpClient();
                    var client = new Client(tcpClient);
                    client.Connect();

                    _clients.Add(client);
                    Logger.Logger.Info().Cid("Listener").Log($"New client connected!");
                }
                catch (ObjectDisposedException) {
                    // Listener stopped
                    break;
                }
                catch (Exception ex) {
                    if (!_cts.Token.IsCancellationRequested) {
                        Logger.Logger.Error().Cid("Listener").Log($"Error accepting client: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex) {
            Logger.Logger.Error().Cid("Listener").Log($"Listener error: {ex.Message}");
        }
    }

    public void Dispose() {
        _cts.Cancel();

        try {
            _tcpListener?.Stop();
        }
        catch { }

        try {
            _udpClient?.Dispose();
        }
        catch { }

        _cts.Dispose();
    }
}

public class Server : ServerBase {
    public Server(int port = 0) : base(port) { }
}