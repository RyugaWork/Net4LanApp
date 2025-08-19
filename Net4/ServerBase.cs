using System.Net;
using System.Net.Sockets;

namespace Net4;

public class ServerBase(int port) {
    private CancellationTokenSource _cts = new CancellationTokenSource();
    private TcpListener? Tcplistener { get; set; } = new TcpListener(IPAddress.Parse(Network.LocalIPAddress), port);
    private List<ClientBase> Clients { get; set; } = new List<ClientBase>();
    private Task? _listenerworkerTask;

    public int ListeningPort => ((IPEndPoint)Tcplistener!.LocalEndpoint).Port;

    public void Start() {
        _listenerworkerTask = Task.Run(() => Listener());
    }

    private void Listener() {
        Tcplistener!.Start();

        Logger.Logger.Info().Cid("Listener").Log($"Server started listen on {Network.LocalIPAddress}:{ListeningPort}");

        while (!_cts.Token.IsCancellationRequested && Tcplistener != null) {
            var socket = Tcplistener!.AcceptTcpClient();
            var client = new Client(socket);
            client.Connect();

            Clients!.Add(client);
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