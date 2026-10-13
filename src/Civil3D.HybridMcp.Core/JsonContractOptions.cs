using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Civil3D.HybridMcp.Core;

public static class JsonContractOptions
{
    public static JsonSerializerOptions Default { get; } = CreateDefault();

    public static JsonNode? ToJsonNode<T>(T value) =>
        JsonSerializer.SerializeToNode(value, Default);

    private static JsonSerializerOptions CreateDefault()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = false,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

