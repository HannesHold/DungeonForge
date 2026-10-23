using System.Text.Json;
using System.Text.Json.Serialization;

namespace DungeonForge.Models;

/// <summary>Whether an event template is a raid or a dungeon. JSON stores the name ("Raid" or "Dungeon").</summary>
[JsonConverter(typeof(EventTypeJsonConverter))]
public enum EventType
{
    Raid,
    Dungeon,
}

/// <summary>Reads <see cref="EventType"/> from a JSON string, ignoring case. Numbers and unknown text are rejected.</summary>
public sealed class EventTypeJsonConverter : JsonConverter<EventType>
{
    public override EventType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("\"type\" must be a string (\"Raid\" or \"Dungeon\").");

        var text = reader.GetString();
        if (!TryParse(text, out var type))
            throw new JsonException($"\"type\" is '{text}' but must be \"Raid\" or \"Dungeon\".");
        return type;
    }

    public override void Write(Utf8JsonWriter writer, EventType value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());

    /// <summary>Parses "Raid" or "Dungeon", ignoring case and surrounding whitespace. Rejects numbers and any other text.</summary>
    public static bool TryParse(string? text, out EventType type)
    {
        type = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Trim();
        foreach (var c in text)
        {
            if (!char.IsLetter(c))
                return false;
        }

        return Enum.TryParse(text, ignoreCase: true, out type) && Enum.IsDefined(type);
    }
}
