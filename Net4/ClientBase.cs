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
    private readonly PacketDispatcher _dispatcher = new();

    public void RegisterHandler(string type, Func<Packet, Task> handler, int priority = 0) 
        => _dispatcher.RegisterHandler(type, handler, priority);
    public void UpdateLastPing() => _socket.UpdateLastPing();

    private void Init() {
        _socket.InitNetworkStream();
        _dispatcher.Init();

        OnInit();
    }

    public void Connect() {
        Logger.Logger.Info().Cid("Server").Log($"Client connected!");
        Init();

        _handshakeworkerTask = Task.Run(() => HandshakeAsync());
        _timeoutworkerTask = Task.Run(() => TimeOutCheckAsync());
    }

    public async Task SendAsync(Packet packet) {
        await _socket.SendAsync(packet);
    }
    public abstract void OnInit();
    public abstract Task OnConnect();

    public async Task ConnectAsync(string host, int port) {
        if (_socket.IsConnected)
            return;

        try { 
            _socket!.Tcpsocket!.Connect(host, port);
            Init();
        }
        catch (Exception ex) {
            Logger.Logger.Error().Log($"Connection unexpected error: {ex}");
            throw new Exception($"Connection unexpected error: {ex}");
        }   

        await OnConnect();

        Logger.Logger.Info().Cid("Client").Log($"Client connected to {host}:{port}");

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
                _dispatcher.Enqueue(packet);
            }
        }
    }

    public void Disconnect() {
        _cts.Cancel();
        _dispatcher.Stop();
        try {
            _socket?.Disconnect();
        }
        catch (Exception ex) {
            Logger.Logger.Error().Log($"Disconnect unexpected error: {ex}");
            throw new Exception($"Disconnect unexpected error: {ex}");
        }

    }

    ~ClientBase() {
        Disconnect();
    }
}

public class Client(TcpClient socket) : ClientBase(socket) {

    public override async Task OnConnect() { await Task.Delay(1000); }
    //public override async Task OnConnect() { 
    //    for(int i=0;i<10;i++) {
    //        await SendAsync(new Tcp_Mess_Pck() { Sender = "", Text = ""});
    //        await SendAsync(new Packet("Ping"));
    //        await Task.Delay(1);
    //    }
    //}

    public override void OnInit() {
        RegisterHandler("Ping", OnPing, 10);
        RegisterHandler("Message", OnPing, 0);
    }

    private async Task OnPing(Packet packet) => UpdateLastPing();
}
