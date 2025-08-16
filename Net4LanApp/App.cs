using System;
using System.Threading.Tasks;
using System.Windows;

using Net4.Logger;

#pragma warning disable IDE0130
namespace Client;
#pragma warning restore IDE0130

class Program {
    static async Task Main(string[] args) {
        Logger.Configure();

        // Simulate running app with graceful exit on Ctrl+C
        Console.CancelKeyPress += (s, e) => {
            Environment.Exit(0);
        };
        while (true) {
            Logger.Info("Client Running");
            await Task.Delay(1000);
        }
    }
}