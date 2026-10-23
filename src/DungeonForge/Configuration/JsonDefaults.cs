using System.Text.Json;
using System.Text.Json.Serialization;

namespace DungeonForge.Configuration;

public static class JsonDefaults
{
    /// <summary>Lenient options for hand-edited configuration files (comments, trailing commas, quoted numbers).</summary>
    public static readonly JsonSerializerOptions ConfigFiles = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>Options for the persisted state file.</summary>
    public static readonly JsonSerializerOptions State = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };
}
