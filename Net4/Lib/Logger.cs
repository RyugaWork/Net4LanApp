using Microsoft.Extensions.Logging;
using Net4.Logger;
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

public static class LogFormatter {
    public static string Format(LogLevel level, string message, string? correlationId, LoggerConfig? config = null) {
        config ??= new LoggerConfig(); // Use default if null

        var placeholders = new Dictionary<string, string?> {
            ["{Mode}"] = config.Mode,
            ["{Timestamp}"] = DateTime.UtcNow.ToString(config.TimeFormat),
            ["{Tickstamp}"] = DateTime.UtcNow.Ticks.ToString(), // More meaningful than ToString(format)
            ["{Level}"] = level.ToString(),
            ["{CorrelationId}"] = correlationId ?? Guid.NewGuid().ToString("N")[..6],
            ["{Message}"] = message
        };

        var output = config.CustomFormat;

        foreach (var kv in placeholders) {
            output = output.Replace(kv.Key, kv.Value ?? string.Empty);
        }

        return output;
    }
}

public static class LoggerHelper {
    public static ILogger GetCallerLogger(ILoggerFactory factory) {
        var stack = new StackTrace();
        var frame = stack.GetFrame(2); // 0=GetCallerLogger, 1=caller in LoggerBuilder, 2=actual user method
        var method = frame?.GetMethod();
        var type = method?.DeclaringType?.Name ?? "UnknownClass";
        var methodName = method?.Name ?? "UnknownMethod";

        // Clean up compiler-generated names (e.g., from async or lambdas)
        var cleanTypeName = Regex.Replace(type, @"<([^>]+)>.*", "$1");

        var category = $"{cleanTypeName}.{methodName}";
        return factory.CreateLogger(category);
    }
}

public class LoggerBuilder(LogLevel level, string? cid = null, LoggerConfig? config = null, LoggerFilter? filter = null) {
    private readonly LogLevel _level = level;
    private readonly string? _cid = cid;
    private readonly LoggerConfig? _config = config ?? new();
    private readonly LoggerFilter? _filter = filter ?? new();
    private readonly ILoggerFactory _factory = CreateFactory();

    public ILoggerFactory CreateFactory() => LoggerFactory.Create(builder => {
        builder.ClearProviders();

        builder.AddConsole();
#if DEBUG // Debug | Release
        builder.SetMinimumLevel(LogLevel.Trace); // This enables all levels
#else
        builder.AddProvider(new FileLoggerProvider(_config));
#endif

    });

    public LoggerBuilder Cid(string? cid) => new LoggerBuilder(_level,cid,_config,_filter);
    public LoggerBuilder Config(LoggerConfig? config) => new LoggerBuilder(_level,_cid,config,_filter);
    public LoggerBuilder Filter(LoggerFilter? filter) => new LoggerBuilder(_level,_cid,_config,filter);

    public void LogMessage(string message) {
        var logger = LoggerHelper.GetCallerLogger(_factory);

        switch (_level) {
            case LogLevel.Critical:
                logger.LogCritical(message);
                break;
            case LogLevel.Error:
                logger.LogError(message);
                break;
            case LogLevel.Warning:
                logger.LogWarning(message);
                break;
            case LogLevel.Information:
                logger.LogInformation(message);
                break;
            case LogLevel.Debug:
                logger.LogDebug(message);
                break;
            case LogLevel.Trace:
                logger.LogTrace(message);
                break;
            default:
                logger.LogInformation(message);
                break;
        }
    }

    public void Log(string message) {
        // filter by allowed levels
        if (_filter!.AllowedLevels.Count > 0 && !_filter.AllowedLevels.Contains(_level))
            return;

        // filter by correlationId
        if (_filter.AllowedCorrelationIds.Count > 0 &&
            (_cid == null || !_filter.AllowedCorrelationIds.Contains(_cid)))
            return;

        LogMessage(LogFormatter.Format(_level, message, _cid));
    }
}

/// <summary>
/// Static Logger wrapper using Microsoft.Extensions.Logging
/// </summary>
public static class Logger {

    private static readonly LoggerFilter? _filter = new LoggerFilter {
        //AllowedCorrelationIds = new HashSet<string> { "Recv" }
    };

    public static LoggerBuilder Trace() => new LoggerBuilder(LogLevel.Trace).Filter(_filter);
    public static LoggerBuilder Critical() => new LoggerBuilder(LogLevel.Critical).Filter(_filter);
    public static LoggerBuilder Info() => new LoggerBuilder(LogLevel.Information).Filter(_filter);
    public static LoggerBuilder Warn() => new LoggerBuilder(LogLevel.Warning).Filter(_filter);
    public static LoggerBuilder Error() => new LoggerBuilder(LogLevel.Error).Filter(_filter);
    public static LoggerBuilder Debug() => new LoggerBuilder(LogLevel.Debug).Filter(_filter);
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