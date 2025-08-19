#pragma warning disable IDE0130
namespace Net4;
#pragma warning restore IDE0130

public class TcpPingConfig() {
    public int TimeoutSeconds { get; set; } = 120;
    public int TimeoutDelay { get; set; } = 60000;
}