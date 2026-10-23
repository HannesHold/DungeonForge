namespace DungeonForge.Models;

/// <summary>
/// Template of a dungeon / raid (EventConfigurations/&lt;name&gt;.json).
/// <see cref="DateTime"/> is only a template/default; the real date and time come from the slash command.
/// </summary>
public sealed class EventDefinition
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int AttendiesRequired { get; set; }

    /// <summary>Role key (from subscription-roles.json) -> number of required attendees.</summary>
    public Dictionary<string, int> AttendyPerRole { get; set; } = new();

    public DateTime? DateTime { get; set; }
    public double DurationInHours { get; set; }
}
