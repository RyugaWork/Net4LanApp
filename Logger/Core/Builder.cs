using Logger.Core.Provider;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Logger.Core;

public partial class Builder(LogLevel level, string? cid = null) : ILogger {
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    private static readonly IConfiguration _configuration = LoggerHelper.configuration;

    private readonly ILoggerFactory _factory = LoggerFactory.Create(builder =>
    {
        builder.AddConfiguration(_configuration.GetSection("Logger"));

        builder.ClearProviders();
        builder.AddConsole();
        builder.AddProvider(new FileLoggerProvider());
    });

    private readonly LogLevel _level = level;
    private readonly string? _cid = cid;

    public Builder Cid(string? cid) => new Builder(_level, cid);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
        throw new NotImplementedException();
    }

    public void Log(string message, [CallerMemberName] string? memberName = default) {
        LoggerHelper.LogMessage(_factory, _level, LoggerHelper.Format(_level, message, _cid), memberName!);
    }
}