using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Logger.Core;

internal static class LoggerConfig {

#if DEBUG
    public const string DefaultMode = "Debug";
#else
    public const string DefaultMode = "Release";
#endif

    public static IConfiguration LoadConfiguration(string? filepath = "Logger.settings.json") {
        var assembly = Assembly.GetExecutingAssembly();

        // try load embedded settings.json
        using var embedded = assembly.GetManifestResourceStream(filepath!);

        var configBuilder = new ConfigurationBuilder();

        if (embedded != null) {
            configBuilder.AddJsonStream(embedded);
        }

        // allow override with external settings.json if present
        configBuilder.AddJsonFile("settings.json", optional: true, reloadOnChange: true);

        return configBuilder.Build();
    }
}