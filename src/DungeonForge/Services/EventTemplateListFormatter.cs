using System.Globalization;
using DungeonForge.Models;

namespace DungeonForge.Services;

/// <summary>
/// Builds the ephemeral reply for /dflisteventtemplates. No I/O and no Discord types, so the layout and
/// the message-length rules can be tested on their own. Feed it <see cref="EventDefinitionRepository.LoadAll"/>.
/// </summary>
public static class EventTemplateListFormatter
{
    /// <summary>Discord message content limit, in UTF-16 code units (the same unit <see cref="string.Length"/> uses).</summary>
    public const int DiscordMessageLimit = 2000;

    private const string TruncationNote = "… (truncated)";

    /// <summary>
    /// One or more message bodies, each at most <paramref name="maxLength"/> characters.
    /// An empty catalog says so. Invalid files (the error strings from
    /// <see cref="EventDefinitionRepository.LoadAll"/>) are grouped under an "Invalid templates" section
    /// and never throw. Lines that cannot fit are cut, with a note; everything else is split across messages.
    /// </summary>
    public static IReadOnlyList<string> Format(
        IReadOnlyList<EventDefinition> templates,
        IReadOnlyList<string> errors,
        int maxLength = DiscordMessageLimit)
    {
        if (maxLength < 1)
            throw new ArgumentOutOfRangeException(nameof(maxLength), "Message length limit must be positive.");

        if (templates.Count == 0 && errors.Count == 0)
            return new[] { Truncate("There are no event templates.", maxLength) };

        var lines = new List<string>();
        if (templates.Count == 0)
        {
            lines.Add("There are no valid event templates.");
        }
        else
        {
            lines.Add($"**Event templates** ({templates.Count}) – the name is the value for /dfcreateevent");
            lines.Add("");
            foreach (var template in templates)
                lines.Add(FormatTemplate(template));
        }

        if (errors.Count > 0)
        {
            lines.Add("");
            lines.Add("**Invalid templates**");
            lines.Add("");
            foreach (var (name, reason) in GroupErrors(errors))
                lines.Add($"• **{name}** – {reason}");
        }

        var messages = Pack(lines, maxLength);
        if (messages.Count == 0)
            messages.Add(Truncate("There are no event templates.", maxLength));
        return messages;
    }

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

            var (name, reason) = SplitError(OneLine(error));
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

    private static List<string> Pack(IReadOnlyList<string> lines, int maxLength)
    {
        var messages = new List<string>();
        var current = new List<string>();
        var length = 0;

        void Flush()
        {
            if (current.Count == 0) return;
            messages.Add(string.Join('\n', current));
            current.Clear();
            length = 0;
        }

        foreach (var raw in lines)
        {
            var line = Truncate(OneLine(raw), maxLength);
            if (line.Length == 0)
            {
                // Keep a blank line between sections, but never start a message with one.
                if (length == 0) continue;
                if (length + 1 > maxLength)
                {
                    Flush();
                    continue;
                }

                current.Add("");
                length += 1;
                continue;
            }

            var added = length == 0 ? line.Length : line.Length + 1;
            if (length > 0 && length + added > maxLength)
                Flush();

            current.Add(line);
            length += length == 0 ? line.Length : line.Length + 1;
        }

        Flush();
        return messages;
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        if (maxLength <= TruncationNote.Length)
            return Cut(text, maxLength);
        return Cut(text, maxLength - TruncationNote.Length) + TruncationNote;
    }

    /// <summary>Cuts on a UTF-16 boundary that is not the middle of a surrogate pair.</summary>
    private static string Cut(string text, int maxChars)
    {
        if (maxChars <= 0) return "";
        if (text.Length <= maxChars) return text;
        if (char.IsHighSurrogate(text[maxChars - 1]))
            maxChars--;
        return maxChars <= 0 ? "" : text[..maxChars];
    }

    private static string OneLine(string text) =>
        string.Join(' ', text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
}
