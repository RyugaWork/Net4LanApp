using System.Net.Sockets;

namespace Net4;

public abstract class ClientBase(TcpClient socket, PacketDispatcher dispatcher) : IAsyncDisposable {
    private readonly TcpSocket _socket = new TcpSocket(socket);
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private Task? _handshakeworkerTask;
    private Task? _timeoutworkerTask;
    private readonly PacketDispatcher _dispatcher = dispatcher;

    // ========================================
    // Public API
    // ========================================

    public void UpdateLastPing() => _socket.UpdateLastPing();

    // Called once during initialization after network stream setup.
    // Override to register handlers.
    public abstract void OnInit();
    // Called after successful connection. Perform handshake logic here.
    public abstract Task OnConnect();

    public void Connect() {
        Logger.Logger.Info().Cid("Server").Log($"Client connected!");
        Init();

        StartBackgroundWorkers();
    }

    public async Task ConnectAsync(string host, int port) {
        if (_socket.IsConnected)
            return;

        try { 
            _socket.Tcpsocket.Connect(host, port);
            Init();
        }
        catch (Exception ex) {
            Logger.Logger.Error().Log($"Connection unexpected error: {ex}");
            throw new Exception($"Connection unexpected error: {ex}");
        }   

        await OnConnect();

        Logger.Logger.Info().Cid("Client").Log($"Client connected to {host}:{port}");

        StartBackgroundWorkers();
    }

    public async Task SendAsync(Packet packet) {
        await _socket.SendAsync(packet);
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

    // ========================================
    // Background Workers
    // ========================================

    private async Task TimeOutCheckAsync() {
        while (!_cts.Token.IsCancellationRequested && _socket!.IsConnected) {
            try {
                await _socket.SendAsync(new Packet("Ping"));
                if (!_socket.IsAlive) {
                    _socket.Disconnect();
                    break;
                }
                await Task.Delay(_socket._config.TimeoutDelay, _cts.Token);
            }
            catch (OperationCanceledException) {
                break;
            }
            catch (Exception ex) {
                Logger.Logger.Error().Cid("TimeOutCheckAsync").Log($"failed: {ex}");
                break;
            }
        }
    }

    private async Task HandshakeAsync() {
        try {
            while (!_cts.Token.IsCancellationRequested && _socket.IsConnected) {
                var packet = await _socket.RecvAsync();
                if (packet != null)
                    _dispatcher.Enqueue(packet);
            }
        }
        catch (Exception ex) {
            Logger.Logger.Error().Cid("HandshakeAsync").Log($"failed: {ex}");
        }
        finally {
            Disconnect();
        }
    }

    // ========================================
    // Lifecycle & Disposal
    // ========================================

    private void Init() {
        _socket.InitNetworkStream();
        _dispatcher.Init();

        OnInit();
    }

    private void StartBackgroundWorkers() {
        _handshakeworkerTask = Task.Run(HandshakeAsync, _cts.Token);
        _timeoutworkerTask = Task.Run(TimeOutCheckAsync, _cts.Token);
    }

    public async ValueTask DisposeAsync() {
        if (!_cts.IsCancellationRequested) {
            _cts.Cancel();
        }

        _dispatcher.Stop();

        await (_handshakeworkerTask ?? Task.CompletedTask);
        await (_timeoutworkerTask ?? Task.CompletedTask);

        _socket?.Disconnect();
        _cts.Dispose();

        GC.SuppressFinalize(this);
    }

    public void Dispose() => DisposeAsync().GetAwaiter().GetResult();

    ~ClientBase() {
        Disconnect();
    }
}

public class Client(TcpClient socket) : ClientBase(socket, _dispatcher) {

    private static readonly PacketDispatcher _dispatcher = new();

    //public override async Task OnConnect() { await Task.Delay(1000); }
    public override async Task OnConnect() {
        for (int i = 0; i < 10; i++) {
            await SendAsync(new Tcp_Mess_Pck() { Sender = "", Text = $"{i}" });
            await SendAsync(new Packet("Ping"));
            await Task.Delay(1);
        }
    }

    public override void OnInit() {
        _dispatcher.RegisterHandler("Ping", OnPing, 10);
        _dispatcher.RegisterHandler("Message", OnPing, 0);
    }

    private async Task OnPing(Packet packet) => UpdateLastPing();
}
