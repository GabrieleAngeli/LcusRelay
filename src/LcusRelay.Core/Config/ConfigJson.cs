
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LcusRelay.Core.Config;

public static class ConfigJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var opt = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        opt.Converters.Add(new JsonStringEnumConverter());
        return opt;
    }
}
