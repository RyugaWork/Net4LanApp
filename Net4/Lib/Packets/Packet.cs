using System.Text.Json;

#pragma warning disable IDE0130
namespace Net4;
#pragma warning restore IDE0130
public class Packet {
    // Type of the packet 
    public string Type { get; set; } = ""; // Packet type (e.g., "Connect", "Ping", "Message").
    public DateTime Timestamp { get; set; } = DateTime.UtcNow; // Timestamp indicating when packet was created.

    public Packet(string type) => this.Type = type;

    // Serializes the current object to a JSON string using its runtime type.
    public string Serialize() => JsonSerializer.Serialize(this, GetType(), Json.Options);

    // Deserializes a JSON string into a specific Packet type based on the "type" property.
    public static Packet? Deserialize(string json) {
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("Type", out var typeProp))
            return null;

        string? type = typeProp.GetString();
        return type switch {
            "Message" => JsonSerializer.Deserialize<PacketMessage>(json),
            _ => JsonSerializer.Deserialize<Packet>(json)
        };
    }
}

public class PacketMessage : Packet {
    public required string? Text { get; set; } = null; // Message content
    public required string? Sender { get; set; } = null; // Sender's identifier (e.g., username)

    public PacketMessage() : base("Message") { }
}
