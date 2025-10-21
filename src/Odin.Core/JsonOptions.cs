using System.Text.Json;
using System.Text.Json.Serialization;

namespace Odin.Core;

/// <summary>
/// Standard JSON serialization options for Odin
/// </summary>
public static class JsonOptions
{
    /// <summary>
    /// Default JSON serializer options for Odin data
    /// </summary>
    public static JsonSerializerOptions Default { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
    };

    /// <summary>
    /// Pretty-printed JSON options for debugging and logging
    /// </summary>
    public static JsonSerializerOptions Pretty { get; } = new(Default)
    {
        WriteIndented = true
    };

    /// <summary>
    /// Serializes an object to JSON string using default options
    /// </summary>
    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, Default);
    }

    /// <summary>
    /// Deserializes a JSON string to an object using default options
    /// </summary>
    public static T? Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, Default);
    }

    /// <summary>
    /// Serializes an object to pretty-printed JSON
    /// </summary>
    public static string SerializePretty<T>(T value)
    {
        return JsonSerializer.Serialize(value, Pretty);
    }
}
