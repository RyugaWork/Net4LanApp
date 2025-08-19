using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
    public string TickFormat { get; set; } = "ffff";

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