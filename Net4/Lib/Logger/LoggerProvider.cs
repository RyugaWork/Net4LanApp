using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

#pragma warning disable IDE0130
namespace Net4.Logger;
#pragma warning restore IDE0130

public class FileLoggerProvider : ILoggerProvider {
    private readonly string _filePath;
    private readonly object _lock = new();
    private static LoggerConfig _config = new();

    // Get the current working directory (where process was launched)
    public static string cwd => Environment.CurrentDirectory;
    // Get base directory of the app domain (where the app assembly is)
    public static string baseDir => AppDomain.CurrentDomain.BaseDirectory;

    public FileLoggerProvider(LoggerConfig config) {
        _config = config;
        var sessionId = Guid.NewGuid().ToString("N")[..8];
        var logDir = Path.Combine(baseDir, "logs");   // logs folder
        if (!Directory.Exists(logDir))
            Directory.CreateDirectory(logDir);

        _filePath = Path.Combine(logDir, $"{sessionId}.log");
    }

    //public ILogger CreateLogger(string categoryName) => new FileLogger(_filePath, _lock, _config);
    public ILogger CreateLogger(string categoryName) => new AsyncFileLogger(_filePath, _config);

    public void Dispose() { }
}

public class FileLogger : ILogger {
    private readonly string _filePath;
    private readonly object _lock;
    private static LoggerConfig _config = new();

    public FileLogger(string filePath, object writeLock, LoggerConfig config) {
        _filePath = filePath;
        _lock = writeLock;
        _config = config;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => default!;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId,
        TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
        if (!IsEnabled(logLevel))
            return;

        var timestamp = DateTime.UtcNow.ToString(_config.TimeFormat);
        var message = formatter(state, exception);

        var logLine = $"{message}";
        if (exception != null)
            logLine += Environment.NewLine + exception;

        lock (_lock) {
            File.AppendAllText(_filePath, logLine + Environment.NewLine);
        }
    }
}

public class AsyncFileLogger : ILogger {
    private readonly string _filePath;
    private static LoggerConfig _config = new();
    private readonly BlockingCollection<string> _logQueue = new();
    private readonly Task _workerTask;
    private bool _disposed = false;


    public AsyncFileLogger(string filePath, LoggerConfig config) {
        _filePath = filePath;
        _config = config;
        _workerTask = Task.Run(async () => {
            foreach (var logLine in _logQueue.GetConsumingEnumerable()) {
                await File.AppendAllTextAsync(_filePath, logLine + Environment.NewLine);
            }
        });
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => default!;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId,
        TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
        if (!IsEnabled(logLevel))
            return;

        var timestamp = DateTime.UtcNow.ToString(_config.TimeFormat);
        var message = formatter(state, exception);

        var logLine = $"{message}";
        if (exception != null)
            logLine += Environment.NewLine + exception;

        _logQueue.Add(message);
    }


    public void Dispose() {
        if (_disposed)
            return;
        _disposed = true;

        _logQueue.CompleteAdding();
        _workerTask.Wait();
        _logQueue.Dispose();
    }
}
