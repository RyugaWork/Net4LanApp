using Net4.Logger;
using Net4;
using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;

#pragma warning disable IDE0130
namespace ClientApp;
#pragma warning restore IDE0130

class Program {
    static async Task Main(string[] args) {
        var client = new Client(new TcpClient());

        Logger.Trace().Log("Trace");
        Logger.Critical().Log("Critical");
        Logger.Debug().Log("Debug");
        Logger.Error().Log("Error");
        Logger.Warn().Log("Warn");
        Logger.Info().Log("Info");

        await client.ConnectAsync(Network.LocalIPAddress, 5000);

        // Simulate running app with graceful exit on Ctrl+C
        Console.CancelKeyPress += (s, e) => {
            Environment.Exit(0);
        };
        while (true) {
            Logger.Info().Log("Client is running - Ctr + C to exit");
            await Task.Delay(60000);
        }
    }
}