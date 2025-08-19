using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Net4;

public class ServerBase(int port) {
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private TcpListener? Tcplistener { get; set; } = new TcpListener(IPAddress.Parse(Network.LocalIPAddress), port);
    private List<ClientBase> _clients { get; set; } = new List<ClientBase>();
    private UdpSocket? _udpSocket; // For responding to discovery
    private Task? _listenerworkerTask;

    public int ListeningPort => ((IPEndPoint)Tcplistener!.LocalEndpoint).Port;

    public void Start() {
        _listenerworkerTask = Task.Run(() => Listener());

        // Start UDP responder for LAN discovery
        _udpSocket = new UdpSocket();
        _ = Task.Run(UdpDiscoveryResponder, _cts.Token);
    }

    /// <summary>
    /// Responds to UDP broadcast discovery messages (e.g., "DISCOVER_SERVER").
    /// </summary>
    private async Task UdpDiscoveryResponder() {
        const string discoveryMessage = "DISCOVER_SERVER";
        const string responseKey = "ServerDiscovery";

        while (!_cts.Token.IsCancellationRequested) {
            try {
                var result = await _udpSocket!._client.ReceiveAsync(_cts.Token);

                var msg = Encoding.UTF8.GetString(result.Buffer);
                if (msg.Contains(discoveryMessage)) {
                    var response = new ServerResponse {
                        Name = "Net4 Game Server",
                        Ip = Network.LocalIPAddress,
                        Port = ListeningPort,
                        Version = "3.0.0"
                    };

                    var json = JsonSerializer.Serialize(response);
                    var data = Encoding.UTF8.GetBytes(json);

                    await _udpSocket._client.SendAsync(data, data.Length, result.RemoteEndPoint);
                    Logger.Logger.Info().Cid(responseKey)
                        .Log($"Discovery response sent to {result.RemoteEndPoint}");
                }
            }
            catch (OperationCanceledException) {
                break;
            }
            catch (Exception ex) when (!_cts.Token.IsCancellationRequested) {
                Logger.Logger.Error().Cid(responseKey)
                    .Log($"UDP discovery responder error: {ex.Message}");
            }
        }
    }

    private void Listener() {
        Tcplistener!.Start();

        Logger.Logger.Info().Cid("Listener").Log($"Server started listen on {Network.LocalIPAddress}:{ListeningPort}");

        while (!_cts.Token.IsCancellationRequested && Tcplistener != null) {
            var socket = Tcplistener!.AcceptTcpClient();
            var client = new Client(socket);
            client.Connect();

            _clients!.Add(client);
        }
    }


    ~ServerBase() {
        _cts.Cancel();
        Tcplistener?.Stop();
        Tcplistener = null;
    }
}
 
public class Server(int port = 0) : ServerBase(port) {

}