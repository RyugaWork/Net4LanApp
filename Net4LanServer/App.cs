using Net4;
using Net4.Logger;
using System;
using System.Threading.Tasks;
using System.Windows;

#pragma warning disable IDE0130
namespace ServerApp;
#pragma warning restore IDE0130

class Program {
    static async Task Main(string[] args) {
        Logger.Configure();

        var server = new Server(5000);
        server.Start();

        // Simulate running app with graceful exit on Ctrl+C
        Console.CancelKeyPress += (s, e) => {
            Environment.Exit(0);
        };
        while (true) {
            Logger.Info("Server is running - Ctr + C to exit");
            await Task.Delay(60000);
        }
    }
}