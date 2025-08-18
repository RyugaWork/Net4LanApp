using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

#pragma warning disable IDE0130
namespace Net4.Logger;
#pragma warning restore IDE0130

/// <summary>
/// Logger configuration class
/// </summary>  
public class LoggerConfig {

#if DEBUG // Debug | Release
    public readonly string Mode = "Debug"; 
#else
    public readonly string Mode = "Release"; 
#endif
    public string CustomFormat { get; set; } = "[{Mode}][{Timestamp}] [{Level}] ({CorrelationId}): {Message}";
    public string TimeFormat { get; set; } = "HH:mm:ss.ffff";
}

/// <summary>
/// Static Logger wrapper using Microsoft.Extensions.Logging
/// </summary>
public static class Logger {
    private static ILoggerFactory? _factory;
    private static LoggerConfig _config = new();

    public static void Configure(LoggerConfig? config = null) {
        _config = config ?? new LoggerConfig();

        _factory = LoggerFactory.Create(builder => {
            builder.ClearProviders();
            builder.AddConsole();
            #if DEBUG // Debug | Release

            #else
                builder.AddProvider(new FileLoggerProvider(_config));
            #endif

        });
    }

    private static ILogger GetLogger() {
        if (_factory == null)
            throw new InvalidOperationException("Logger not configured. Call Logger.Configure() first.");

        // Walk stack to find the *caller of Logger.Info/Warn/Error/Debug*
        var stack = new StackTrace();
        var frame = stack.GetFrame(2); // 0 = GetLogger, 1 = Info(), 2 = real caller
        var method = frame?.GetMethod();
        var type = method?.DeclaringType?.Name ?? "UnknownClass";
        var methodName = method?.Name ?? "UnknownMethod";

        var category = $"{type}.{methodName}";
        return _factory.CreateLogger(category);
    }

    private static string FormatMessage(LogLevel level, string message, string? correlationId) {
        var timestamp = DateTime.UtcNow.ToString(_config.TimeFormat);
        return _config.CustomFormat
            .Replace("{Mode}", _config.Mode)
            .Replace("{Timestamp}", timestamp)
            .Replace("{Level}", level.ToString())
            .Replace("{CorrelationId}", correlationId ?? Guid.NewGuid().ToString("N")[..6])
            .Replace("{Message}", message);
    }

    public static void Info(string message, string? correlationId = null) =>
        GetLogger().LogInformation(FormatMessage(LogLevel.Information, message, correlationId));

    public static void Warn(string message, string? correlationId = null) =>
        GetLogger().LogWarning(FormatMessage(LogLevel.Warning, message, correlationId));

    public static void Error(string message, string? correlationId = null) =>
        GetLogger().LogError(FormatMessage(LogLevel.Error, message, correlationId));

    public static void Debug(string message, string? correlationId = null) =>
        GetLogger().LogDebug(FormatMessage(LogLevel.Debug, message, correlationId));
}

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