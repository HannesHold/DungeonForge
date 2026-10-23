using DungeonForge.Models;

namespace DungeonForge.Services;

/// <summary>
/// Builds /dfcreateevent autocomplete choices. The value is the template name; the label shows the type and size
/// (for example "Molten Core (Raid, 40)"). Raids come first, then dungeons, each alphabetical by display name.
/// </summary>
public static class EventTemplateAutocomplete
{
    /// <summary>Discord accepts at most this many autocomplete choices.</summary>
    public const int DiscordChoiceLimit = 25;

    /// <summary>Discord autocomplete choice labels are capped at 100 characters.</summary>
    public const int DiscordLabelLimit = 100;

    public static string Label(EventDefinition template) =>
        $"{template.DisplayName} ({template.Type}, {template.AttendiesRequired})";

    public static IReadOnlyList<(string Label, string Value)> Suggest(IEnumerable<EventDefinition> templates, string? typed)
    {
        var query = typed ?? "";
        return templates
            .Where(template => template.Type is EventType.Raid or EventType.Dungeon)
            .Select(template => (Template: template, Label: Label(template)))
            .Where(choice => query.Length == 0
                || choice.Template.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || choice.Label.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(choice => choice.Template.Type == EventType.Raid ? 0 : 1)
            .ThenBy(choice => choice.Template.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(choice => choice.Template.Name, StringComparer.OrdinalIgnoreCase)
            .Take(DiscordChoiceLimit)
            .Select(choice => (Truncate(choice.Label), choice.Template.Name))
            .ToList();
    }

    private static string Truncate(string label) =>
        label.Length <= DiscordLabelLimit ? label : label[..DiscordLabelLimit];
}
