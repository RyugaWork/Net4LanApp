using System.Collections.Concurrent;

#pragma warning disable IDE0130
namespace Net4;
#pragma warning restore IDE0130

public class PacketDispatcher(int workerCount = 1) {
    private readonly int _workerCount = workerCount; // Number of thread use
    private readonly ConcurrentDictionary<string, Func<Packet, Task>> _handlers = new();
    private readonly ConcurrentDictionary<string, int> _priorities = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly PriorityQueue<Packet, int> _queue = new();
    private readonly object _lock = new();
    private readonly SemaphoreSlim _signal = new(0);

    // Register a handler for a given packet type.
    public void RegisterHandler(string type, Func<Packet, Task> handler, int priority = 0) {
        _handlers[type] = handler;
        _priorities[type] = priority;
    }

    // Enqueue a packet with a given priority.
    public void Enqueue(Packet packet) {
        int priority = _priorities[packet.Type!];
        Logger.Logger.Debug().Cid("PacketDispatcher").Log($"Enqueue packet: {packet}");
        lock (_lock) {
            // Negative priority because PriorityQueue is min-heap by default
            _queue.Enqueue(packet, -priority);
        }
        _signal.Release();
    }

    // Initilize worker thread
    public void Init() {
        for (int i = 1; i <= _workerCount; i++) {
            Logger.Logger.Debug().Cid("PacketDispatcher").Log($"Init woker [{i}]");
            _ = Task.Run(() => Job());
        }
    }

    private async Task Job() {
        while (!_cts.IsCancellationRequested) {
            await _signal.WaitAsync(_cts.Token); // wait for the packet

            Packet? packet = null;
            lock (_lock) {
                if (_queue.Count > 0)
                    packet = _queue.Dequeue();
            }

            if (packet != null)
                await HandlePacket(packet);
        }
    }

    private async Task HandlePacket(Packet packet) {
        Logger.Logger.Debug().Cid("PacketDispatcher").Log($"Handling ticket: {packet}");
        if (_handlers.TryGetValue(packet.Type ?? "", out var handler)) {
            try {
                await handler(packet);
            }
            catch (Exception ex) {
                Logger.Logger.Error().Log($"Packet {packet.Type} failed: {ex}");
            }
        }
        else {
            Logger.Logger.Warn().Log($"No handler for type {packet.Type}");
        }
    }

    public void Stop() => _cts.Cancel();
}