using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Logger.Core;
internal static partial class LoggerHelper {
    private static readonly Dictionary<LogLevel, Action<ILogger, string>> _logMap =
    new()
    {
    { LogLevel.Critical,   (l, m) => l.LogCritical(m) },
    { LogLevel.Error,      (l, m) => l.LogError(m) },
    { LogLevel.Warning,    (l, m) => l.LogWarning(m) },
    { LogLevel.Information,(l, m) => l.LogInformation(m) },
    { LogLevel.Debug,      (l, m) => l.LogDebug(m) },
    { LogLevel.Trace,      (l, m) => l.LogTrace(m) }
    };

    public static void LogMessage(ILoggerFactory factory, LogLevel level, string message, [CallerMemberName] string? memberName = default) {
        var logger = factory.CreateLogger(memberName!);
        _logMap[level](logger, message);
    }

    public static readonly IConfiguration configuration = LoggerConfig.LoadConfiguration();
    [GeneratedRegex(@"\{(\w+)\}", RegexOptions.Compiled)]
    private static partial Regex PlaceholderRegex();

    private sealed record LoggerFormatConfig(string Mode, string TimeFormat, string TickFormat, string Template);
    private static readonly LoggerFormatConfig _config = new(
        configuration["Logger:Format:Mode"] ?? LoggerConfig.DefaultMode,
        configuration["Logger:Format:Time"] ?? "NULL",
        configuration["Logger:Format:Tick"] ?? "NULL",
        configuration["Logger:Format:Message"] ?? "NULL"
    );

    public static string Format(LogLevel level, string message, string? correlationId = null) {
        var placeholders = new Dictionary<string, string> {
            ["Mode"] = _config.Mode,
            ["Timestamp"] = DateTime.UtcNow.ToString(_config.TimeFormat),
            ["Tickstamp"] = DateTime.UtcNow.ToString(_config.TickFormat),
            ["Level"] = level.ToString(),
            ["CorrelationId"] = correlationId ?? Guid.NewGuid().ToString("N")[..6],
            ["Message"] = message
        };

        return PlaceholderRegex().Replace(_config.Template, match => {
            var key = match.Groups[1].Value; // "Message", "Level"
            return placeholders.TryGetValue(key, out var value) ? value : match.Value;
        });
    }
}