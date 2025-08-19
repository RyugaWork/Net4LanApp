using System.Text.Json;

#pragma warning disable IDE0130
namespace Net4;
#pragma warning restore IDE0130

public static class Json {
    public static readonly JsonSerializerOptions Options = new() {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
}