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



public class LoggerBuilder(LogLevel level, string? cid = null, LoggerConfig? config = null, LoggerFilter? filter = null) {
    private readonly LogLevel _level = level;
    private readonly string? _cid = cid;
    private readonly LoggerConfig? _config = config ?? new();
    private readonly LoggerFilter? _filter = filter ?? new();
    private readonly ILoggerFactory _factory = CreateFactory();

    public static ILoggerFactory CreateFactory() => LoggerFactory.Create(builder => {
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

