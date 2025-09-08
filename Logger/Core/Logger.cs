using Microsoft.Extensions.Logging;

namespace Logger.Core;
public static class Logger {
    public static Builder Trace() => new Builder(LogLevel.Trace);
    public static Builder Critical() => new Builder(LogLevel.Critical);
    public static Builder Info() => new Builder(LogLevel.Information);
    public static Builder Warn() => new Builder(LogLevel.Warning);
    public static Builder Error() => new Builder(LogLevel.Error);
    public static Builder Debug() => new Builder(LogLevel.Debug);
}


