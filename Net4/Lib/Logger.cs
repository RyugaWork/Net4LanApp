using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable IDE0130
namespace Net4.Logger;
#pragma warning restore IDE0130

/// <summary>
/// Custom Logger Class
/// Provides configurable logging to console or file with buffering, formatting.
/// </summary>

public class LoggerConfig {
    public string? Mode { get; set; } = null; // Debug | Release 
    public int BufferSize { get; set; } = 100;
    public int FlushInterval { get; set; } = 2000; // milliseconds
    public string CustomFormat { get; set; } = "[{mode}][{time}] [{level}]: {message}";
    public string TimeFormat { get; set; } = "yyyy-MM-dd HH:mm:ss";
}

public class Logger {
    private static LoggerConfig? Config;
    private static StreamWriter? FileWriter;
    private static CancellationTokenSource? Cts;
    private static BlockingCollection<string>? Buffer;
    private static string SessionId = string.Empty;
    private static Task? Worker;

    static Logger() {
        // Auto-cleanup when app shuts down
        AppDomain.CurrentDomain.ProcessExit += (_, __) => Shutdown();
        AppDomain.CurrentDomain.DomainUnload += (_, __) => Shutdown();

        Initialize(); // force a default config when create
    }

    private static void Initialize(LoggerConfig? config = null, string? configPath = null) {
        Config = config ?? new LoggerConfig();

        SessionId = Guid.NewGuid().ToString("N")[..8];

#if DEBUG
        Config.Mode = "Debug";
#else
            Config.Mode = "Release";
#endif

        if (configPath != null) {
            var logDir = configPath ?? Path.Combine(baseDir, "logs");
            var fileName = Path.Combine(logDir, $"log_{SessionId}.log");
            FileWriter = new StreamWriter(File.Open(fileName, FileMode.Append, FileAccess.Write, FileShare.Read)) {
                AutoFlush = true
            };
        }

        Buffer = new BlockingCollection<string>(Config.BufferSize);
        Cts = new CancellationTokenSource();

        Worker = Task.Run(() => FlushWorker(Cts.Token));
        AppDomain.CurrentDomain.ProcessExit += (_, __) => Shutdown();
    }


    private static string FormatLog(string level, string message, string cid) {
        var timestamp = DateTime.UtcNow.ToString(Config!.TimeFormat);
        return Config.CustomFormat
            .Replace("{mode}", Config.Mode)
            .Replace("{time}", timestamp)
            .Replace("{level}", level)
            .Replace("{cid}", cid)
            .Replace("{message}", message);
    }

    private static async Task FlushWorker(CancellationToken token) {
        while (!token.IsCancellationRequested) {
            try {
                await Task.Delay(Config!.FlushInterval, token);
                Flush();
            }
            catch (TaskCanceledException) { break; }
        }
    }

    #region public var

    // Get the current working directory (where process was launched)
    public static string cwd => Environment.CurrentDirectory;
    // Get base directory of the app domain (where the app assembly is)
    public static string baseDir => AppDomain.CurrentDomain.BaseDirectory;

    #endregion

    #region public method

    public static void Configure(LoggerConfig? config = null, string? configPath = null) {
        Shutdown(); // clear previous setup
        Initialize(config, configPath);
    }

    public static void Log(string level, string message, string? correlationId = null) {
        var cid = correlationId ?? Guid.NewGuid().ToString("N")[..6];
        var formatted = FormatLog(level.ToUpper(), message, cid);
        Buffer?.Add(formatted);
    }

    public static void Info(string message) => Log("Info", message);
    public static void Warn(string message) => Log("Warn", message);
    public static void Error(string message) => Log("Error", message);

    public static void Flush() {
        while (Buffer != null && Buffer.TryTake(out var logEntry)) {
#if DEBUG
                Console.WriteLine(logEntry);
#endif
            if (FileWriter != null)
                FileWriter!.WriteLine(logEntry);
        }
    }

    public static void Shutdown() {
        if (Cts == null)
            return;
        try {
            Cts.Cancel();
            Worker?.Wait();
            Flush();
        }
        catch { }
        finally {
            FileWriter?.Dispose();
            Cts.Dispose();
            Buffer?.Dispose();
            Cts = null;
        }
    }

    #endregion
}