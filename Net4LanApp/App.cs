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
        Logger.Configure();

        var client = new Client(new TcpClient());

        await client.ConnectAsync(Network.LocalIPAddress, 5000);

        // Simulate running app with graceful exit on Ctrl+C
        Console.CancelKeyPress += (s, e) => {
            Environment.Exit(0);
        };
        while (true) {
            Logger.Info("Client is running - Ctr + C to exit");
            await Task.Delay(60000);
        }
    }
}