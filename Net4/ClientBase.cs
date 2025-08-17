using Net4.Tcp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Net4;

public abstract class ClientBase(TcpClient socket) {
    private readonly TcpSocket _socket = new TcpSocket(socket);
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private Task? _handshakeworkerTask;
    private Task? _timeoutworkerTask;

    public void Init() {
        _socket.InitNetworkStream();
    }

    public void Connect() {
        Logger.Logger.Info($"Client connected!", "Server");
        _socket.InitNetworkStream();

        _handshakeworkerTask = Task.Run(() => HandshakeAsync());
        _timeoutworkerTask = Task.Run(() => TimeOutCheckAsync());
    }

    public abstract Task OnConnect();

    public async Task ConnectAsync(string host, int port) {
        if (_socket.IsConnected)
            return;

        try { 
            _socket!.Tcpsocket!.Connect(host, port);
            _socket.InitNetworkStream();
        }
        catch (Exception ex) {
            Logger.Logger.Error($"Connection unexpected error: {ex}");
            throw new Exception($"Connection unexpected error: {ex}");
        }   

        await OnConnect();

        Logger.Logger.Info($"Client connect to {host}:{port}","Client");

        _handshakeworkerTask = Task.Run(() => HandshakeAsync());
        _timeoutworkerTask = Task.Run(() => TimeOutCheckAsync());
    }

    private async Task TimeOutCheckAsync() {
        while (!_cts.Token.IsCancellationRequested && _socket!.IsConnected) { 
            await _socket.SendAsync(new Packet("Ping"));
            if (!_socket.IsAlive) {
                _socket.Disconnect();
                break;
            }
            await Task.Delay(_socket._config.TimeoutDelay, _cts.Token);
        }
    }

    private async Task HandshakeAsync() {
        while (!_cts.Token.IsCancellationRequested && _socket!.IsConnected) {
            var packet = await _socket.RecvAsync();

            if (packet != null) {
                Logger.Logger.Info($"Recv >> {packet.Serialize()}");
                switch (packet.Type) {
                    case "Ping": {
                        _socket!.UpdateLastPing();
                        break;
                    }

                    default:
                        break;
                }
            }
        }
    }

    public void Disconnect() {
        _cts.Cancel();
        try {
            _socket?.Disconnect();
        }
        catch (Exception ex) {
            Logger.Logger.Error($"Disconnect unexpected error: {ex}");
            throw new Exception($"Disconnect unexpected error: {ex}");
        }

    }

    ~ClientBase() {
        Disconnect();
    }
}

public class Client(TcpClient socket) : ClientBase(socket) {

    public override async Task OnConnect() { await Task.Delay(1000); }
}
