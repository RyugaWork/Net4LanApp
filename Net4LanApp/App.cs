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

        Logger.Trace().Cid("Trace").Log("");
        Logger.Critical().Cid("Critical").Log("");
        Logger.Debug().Cid("Debug").Log("");
        Logger.Error().Cid("Error").Log("");
        Logger.Warn().Cid("Warn").Log("");
        Logger.Info().Cid("Info").Log("");

        //await client.ConnectAsync(Network.LocalIPAddress, 5000);
        await client.ConnectAsync();

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