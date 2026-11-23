using System.Text.Json;
using System.Text.Json.Serialization;

namespace KashflowMCP;

internal static class JsonResults
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
