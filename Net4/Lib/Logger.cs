using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

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
    public string CustomFormat { get; set; } = "[{Mode}][{Timestamp}]\n[{Level}][{Tickstamp}]: ({CorrelationId}) {Message}";
    public string TimeFormat { get; set; } = "HH:mm:ss";
    public string TickFormat{ get; set; } = "ffff";

}

public class LoggerFilter {
    // Correlation ID whitelist
    public HashSet<string> AllowedCorrelationIds { get; set; } = new();

    // Explicit level filter (if empty => allow all levels above MinimumLevel)
    public HashSet<LogLevel> AllowedLevels { get; set; } = new();
}

public readonly struct LoggerBuilder(LogLevel level, string? cid = null, LoggerConfig? config = null, LoggerFilter? filter = null) {
    private readonly LogLevel _level = level;
    private readonly string? _cid = cid;
    private readonly LoggerConfig? _config = config ?? new();
    private readonly LoggerFilter? _filter = filter ?? new();
    private readonly ILoggerFactory _factory = LoggerFactory.Create(builder => {
        builder.ClearProviders();
        builder.AddConsole();
#if DEBUG // Debug | Release

#else
                builder.AddProvider(new FileLoggerProvider(_config));
#endif

    });

    private string FormatMessage(LogLevel level, string message, string? correlationId) {
        if (level == LogLevel.Trace || level == LogLevel.Debug) {
            message = $"[{message}]"; // highlight noisy logs
        }

        var placeholders = new Dictionary<string, string?> {
            ["{Mode}"] = _config!.Mode,
            ["{Timestamp}"] = DateTime.UtcNow.ToString(_config!.TimeFormat),
            ["{Tickstamp}"] = DateTime.UtcNow.ToString(_config!.TickFormat),
            ["{Level}"] = level.ToString(),
            ["{CorrelationId}"] = correlationId ?? Guid.NewGuid().ToString("N")[..6],
            ["{Message}"] = message
        };

        var output = _config.CustomFormat;
        foreach (var kv in placeholders) {
            output = output.Replace(kv.Key, kv.Value);
        }

        return output;
    }

    private ILogger GetLogger() {
        // Walk stack to find the *caller of Logger.Info/Warn/Error/Debug*
        var stack = new StackTrace();
        var frame = stack.GetFrame(2); // 0 = GetLogger, 1 = Info(), 2 = real caller
        var method = frame?.GetMethod();
        var type = method?.DeclaringType?.Name ?? "UnknownClass";
        var methodName = method?.Name ?? "UnknownMethod";

        var category = $"{Regex.Replace(type, @"<([^>]+)>.*", "$1")}.{methodName}";
        return _factory.CreateLogger(category);
    }

    public LoggerBuilder Cid(string? cid) => new LoggerBuilder(_level,cid,_config,_filter);
    public LoggerBuilder Config(LoggerConfig? config) => new LoggerBuilder(_level,_cid,config,_filter);
    public LoggerBuilder Fillter(LoggerFilter? filter) => new LoggerBuilder(_level,_cid,_config,filter);

    public void Log(string message) {
        // filter by allowed levels
        if (_filter!.AllowedLevels.Count > 0 && !_filter.AllowedLevels.Contains(_level))
            return;

        // filter by correlationId
        if (_filter.AllowedCorrelationIds.Count > 0 &&
            (_cid == null || !_filter.AllowedCorrelationIds.Contains(_cid)))
            return;

        GetLogger().LogInformation(FormatMessage(_level, message, _cid));
    }

}

/// <summary>
/// Static Logger wrapper using Microsoft.Extensions.Logging
/// </summary>
public static class Logger {

    private static readonly LoggerFilter? _fillter = new LoggerFilter {
        //AllowedCorrelationIds = new HashSet<string> { "Recv" }
    };

    public static LoggerBuilder Trace() => new LoggerBuilder(LogLevel.Trace).Fillter(_fillter);
    public static LoggerBuilder Critical() => new LoggerBuilder(LogLevel.Critical).Fillter(_fillter);
    public static LoggerBuilder Info() => new LoggerBuilder(LogLevel.Information).Fillter(_fillter);
    public static LoggerBuilder Warn() => new LoggerBuilder(LogLevel.Warning).Fillter(_fillter);
    public static LoggerBuilder Error() => new LoggerBuilder(LogLevel.Error).Fillter(_fillter);
    public static LoggerBuilder Debug() => new LoggerBuilder(LogLevel.Debug).Fillter(_fillter);
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