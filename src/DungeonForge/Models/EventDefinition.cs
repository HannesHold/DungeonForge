namespace DungeonForge.Models;

/// <summary>
/// Template of a dungeon or raid (<c>EventConfigurations/Raids</c>, <c>EventConfigurations/Dungeons</c>, or a file
/// directly under <c>EventConfigurations</c>). <see cref="DateTime"/> is only a template/default; the real date
/// and time come from the slash command.
/// </summary>
public sealed class EventDefinition
{
    public string Name { get; set; } = "";

    /// <summary>"Raid" or "Dungeon". Null when the JSON omitted it or the value was not a known type.</summary>
    public EventType? Type { get; set; }

    public string DisplayName { get; set; } = "";
    public int AttendiesRequired { get; set; }

    /// <summary>Role key (from subscription-roles.json) -> number of required attendees.</summary>
    public Dictionary<string, int> AttendyPerRole { get; set; } = new();

    public DateTime? DateTime { get; set; }
    public double DurationInHours { get; set; }

    /// <summary>
    /// Encounter order. Required and non-empty for raids. Absent or empty for dungeons.
    /// A boss may omit <see cref="BossDefinition.Points"/> to use the global default.
    /// </summary>
    public List<BossDefinition>? Bosses { get; set; }
}
