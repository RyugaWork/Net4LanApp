using Logger.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Logger.Core.Provider;
public class FileLoggerProvider : ILoggerProvider {
    private readonly string _filePath;

    // Get the current working directory (where process was launched)
    public static string cwd => Environment.CurrentDirectory;
    // Get base directory of the app domain (where the app assembly is)
    public static string baseDir => AppDomain.CurrentDomain.BaseDirectory;

    public FileLoggerProvider() {
        var sessionId = Guid.NewGuid().ToString("N")[..8];
        var logDir = Path.Combine(baseDir, "logs");   // logs folder
        if (!Directory.Exists(logDir))
            Directory.CreateDirectory(logDir);

        _filePath = Path.Combine(logDir, $"{sessionId}.log");
    }

    public ILogger CreateLogger(string categoryName) => new AsyncFileLogger(_filePath);

    public void Dispose() { }
}

public class AsyncFileLogger : ILogger {
    private readonly BlockingCollection<string> _logQueue = new();
    private readonly Task _workerTask;
    private bool _disposed = false;

    public AsyncFileLogger(string filePath) {
        _workerTask = Task.Run(async () => {
            foreach (var logLine in _logQueue.GetConsumingEnumerable()) {
                await File.AppendAllTextAsync(filePath, logLine + Environment.NewLine);
            }
        });
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => default!;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId,
        TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
        if (!IsEnabled(logLevel))
            return;

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