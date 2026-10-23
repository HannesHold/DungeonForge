using DungeonForge.Models;

namespace DungeonForge.Services;

/// <summary>
/// Text for the points channel: one leaderboard message, and a raid boss table that may span several messages.
/// </summary>
public static class PointsBoardFormatter
{
    public const int ChannelLeaderboardCap = 25;

    public static string FormatLeaderboard(
        IReadOnlyList<(ulong UserId, int Total)> ranked,
        int cap = ChannelLeaderboardCap,
        int maxLength = EventTemplateListFormatter.DiscordMessageLimit,
        string title = "Points leaderboard")
    {
        if (maxLength < 1)
            throw new ArgumentOutOfRangeException(nameof(maxLength));
        if (ranked.Count == 0)
            return Fit($"**{title}**\nNo points have been awarded yet.", maxLength);

        var header = $"**{title}**";
        var chosen = new List<string>();
        var limit = Math.Max(0, cap);
        for (var i = 0; i < ranked.Count && chosen.Count < limit; i++)
        {
            var candidate = new List<string>(chosen)
            {
                $"{i + 1}. {EventRenderer.Mention(ranked[i].UserId)} — {ranked[i].Total}",
            };
            if (Compose(header, candidate, ranked.Count).Length > maxLength)
                break;
            chosen = candidate;
        }

        if (chosen.Count == 0)
            return Fit(header, maxLength);

        return Compose(header, chosen, ranked.Count);
    }

    public static IReadOnlyList<string> FormatBossTable(
        IReadOnlyList<EventDefinition> templates,
        int pointsPerBossKill,
        int maxLength = EventTemplateListFormatter.DiscordMessageLimit)
    {
        var raids = templates
            .Where(template => template.Type == EventType.Raid)
            .OrderBy(template => template.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (raids.Count == 0)
            return DiscordMessagePacker.Pack(new[] { "**Raid boss points**", "No raid templates found." }, maxLength);

        var lines = new List<string>
        {
            "**Raid boss points**",
            $"Default points per kill: {pointsPerBossKill}. A boss with its own points uses that instead.",
            "",
        };

        foreach (var raid in raids)
        {
            lines.Add($"**{raid.DisplayName}** (`{raid.Name}`)");
            var bosses = raid.Bosses;
            if (bosses is null || bosses.Count == 0)
                lines.Add("• (no bosses)");
            else
            {
                foreach (var boss in bosses)
                {
                    var points = PointsLogic.ResolvePoints(boss, pointsPerBossKill);
                    var mark = boss.Points is null ? "" : " (override)";
                    lines.Add($"• {boss.Name} — {points}{mark}");
                }
            }

            lines.Add("");
        }

        var messages = DiscordMessagePacker.Pack(lines, maxLength);
        return messages.Count == 0 ? new[] { "**Raid boss points**" } : messages;
    }

    private static string Compose(string header, IReadOnlyList<string> rows, int totalPlayers)
    {
        var lines = new List<string> { header, "" };
        lines.AddRange(rows);
        if (rows.Count < totalPlayers)
            lines.Add($"Showing {rows.Count} of {totalPlayers} players.");
        return string.Join('\n', lines);
    }

    private static string Fit(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength];
}
