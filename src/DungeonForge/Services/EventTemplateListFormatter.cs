using System.Globalization;
using DungeonForge.Models;

namespace DungeonForge.Services;

/// <summary>
/// Builds the ephemeral reply for /dflisteventtemplates, /dflistraidtemplates and /dflistdungeontemplates.
/// No I/O and no Discord types, so the layout and the message-length rules can be tested on their own.
/// Feed it <see cref="EventDefinitionRepository.LoadAll"/>. Pass <paramref name="type"/> to list one kind.
/// </summary>
public static class EventTemplateListFormatter
{
    /// <summary>Discord message content limit, in UTF-16 code units (the same unit <see cref="string.Length"/> uses).</summary>
    public const int DiscordMessageLimit = 2000;

    /// <summary>
    /// One or more message bodies, each at most <paramref name="maxLength"/> characters.
    /// Raids are listed before dungeons; each section is alphabetical by display name.
    /// <paramref name="type"/> keeps only that kind (the two filtered list commands). Invalid files (the error
    /// strings from <see cref="EventDefinitionRepository.LoadAll"/>) are grouped under an "Invalid templates"
    /// section and never throw. Lines that cannot fit are cut, with a note; everything else is split across messages.
    /// </summary>
    public static IReadOnlyList<string> Format(
        IReadOnlyList<EventDefinition> templates,
        IReadOnlyList<string> errors,
        int maxLength = DiscordMessageLimit,
        EventType? type = null)
    {
        if (maxLength < 1)
            throw new ArgumentOutOfRangeException(nameof(maxLength), "Message length limit must be positive.");

        var selected = templates
            .Where(template => template.Type is EventType.Raid or EventType.Dungeon)
            .Where(template => type is null || template.Type == type)
            .ToList();

        if (selected.Count == 0 && errors.Count == 0)
            return DiscordMessagePacker.Pack(new[] { EmptyCatalogMessage(type, templates.Count == 0) }, maxLength);

        var lines = new List<string>();
        if (selected.Count == 0)
        {
            lines.Add(type switch
            {
                EventType.Raid => "There are no raid templates.",
                EventType.Dungeon => "There are no dungeon templates.",
                _ => "There are no valid event templates.",
            });
        }
        else if (type is null)
        {
            lines.Add($"**Event templates** ({selected.Count}) – the name is the value for /dfcreateevent");
            AddSection(lines, "Raids", Sort(selected.Where(template => template.Type == EventType.Raid)), withHint: false);
            AddSection(lines, "Dungeons", Sort(selected.Where(template => template.Type == EventType.Dungeon)), withHint: false);
        }
        else
        {
            var heading = type == EventType.Raid ? "Raids" : "Dungeons";
            AddSection(lines, heading, Sort(selected), withHint: true);
        }

        if (errors.Count > 0)
        {
            if (lines.Count > 0)
                lines.Add("");
            lines.Add("**Invalid templates**");
            lines.Add("");
            foreach (var (name, reason) in GroupErrors(errors))
                lines.Add($"• **{name}** – {reason}");
        }

        var messages = DiscordMessagePacker.Pack(lines, maxLength);
        if (messages.Count == 0)
            return DiscordMessagePacker.Pack(new[] { EmptyCatalogMessage(type, templates.Count == 0) }, maxLength);
        return messages;
    }

    private static string EmptyCatalogMessage(EventType? type, bool nothingPassed) => type switch
    {
        EventType.Raid => "There are no raid templates.",
        EventType.Dungeon => "There are no dungeon templates.",
        _ when nothingPassed => "There are no event templates.",
        _ => "There are no valid event templates.",
    };

    private static void AddSection(List<string> lines, string heading, IReadOnlyList<EventDefinition> templates, bool withHint)
    {
        if (templates.Count == 0)
            return;

        if (lines.Count > 0)
            lines.Add("");
        var hint = withHint ? " – the name is the value for /dfcreateevent" : "";
        lines.Add($"**{heading}** ({templates.Count}){hint}");
        lines.Add("");
        foreach (var template in templates)
            lines.Add(FormatTemplate(template));
    }

    private static List<EventDefinition> Sort(IEnumerable<EventDefinition> templates) =>
        templates
            .OrderBy(template => template.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string FormatTemplate(EventDefinition template)
    {
        var duration = template.DurationInHours.ToString("0.##", CultureInfo.InvariantCulture);
        return $"• **{template.Name}** – {template.DisplayName} – attendiesRequired: {template.AttendiesRequired}, durationInHours: {duration} – {FormatRoles(template.AttendyPerRole)}";
    }

    private static string FormatRoles(Dictionary<string, int>? roles)
    {
        if (roles is null || roles.Count == 0) return "no roles";
        return string.Join(", ", roles.Select(kv => $"{kv.Value} {kv.Key}"));
    }

    /// <summary>
    /// LoadAll prefixes every problem with "<c>File.json: </c>" and may report several problems per file.
    /// Group those back into one line per file, in first-seen order.
    /// </summary>
    private static IEnumerable<(string Name, string Reason)> GroupErrors(IReadOnlyList<string> errors)
    {
        var groups = new List<(string Name, List<string> Reasons)>();
        foreach (var error in errors)
        {
            if (string.IsNullOrWhiteSpace(error)) continue;

            var (name, reason) = SplitError(DiscordMessagePacker.OneLine(error));
            var index = groups.FindIndex(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                groups.Add((name, new List<string> { reason }));
            else
                groups[index].Reasons.Add(reason);
        }

        return groups.Select(g => (g.Name, string.Join("; ", g.Reasons)));
    }

    private static (string Name, string Reason) SplitError(string error)
    {
        var index = error.IndexOf(": ", StringComparison.Ordinal);
        if (index <= 0) return ("(unknown file)", error);
        return (error[..index], error[(index + 2)..]);
    }

}
