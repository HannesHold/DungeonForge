using DungeonForge.Models;

namespace DungeonForge.Services;

/// <summary>
/// Text for the points channel: a full leaderboard that may span several messages, and a raid boss table.
/// </summary>
public static class PointsBoardFormatter
{
    /// <summary>
    /// Every player with points, highest total first. Rank numbers stay continuous across messages.
    /// A line is never split; <see cref="DiscordMessagePacker"/> only truncates a single line that is already over the limit.
    /// </summary>
    public static IReadOnlyList<string> FormatLeaderboard(
        IReadOnlyList<(ulong UserId, int Total)> ranked,
        int maxLength = EventTemplateListFormatter.DiscordMessageLimit,
        string title = "Points leaderboard")
    {
        if (maxLength < 1)
            throw new ArgumentOutOfRangeException(nameof(maxLength));

        if (ranked.Count == 0)
            return new[] { Fit($"**{title}**\nNo points have been awarded yet.", maxLength) };

        var lines = new List<string> { $"**{title}**", "" };
        for (var i = 0; i < ranked.Count; i++)
            lines.Add($"{i + 1}. {EventRenderer.Mention(ranked[i].UserId)} — {ranked[i].Total}");

        var messages = DiscordMessagePacker.Pack(lines, maxLength);
        return messages.Count == 0 ? new[] { Fit($"**{title}**", maxLength) } : messages;
    }

    /// <summary>
    /// New leaderboard messages are sent at the bottom of the channel. When that would place them after the boss
    /// table, the boss table is deleted and posted again after the full leaderboard.
    /// </summary>
    public static bool LeaderboardGrowthMovesBossTable(int previousLeaderboardMessages, int nextLeaderboardMessages, int bossTableMessages) =>
        nextLeaderboardMessages > previousLeaderboardMessages && bossTableMessages > 0;

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

    private static string Fit(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength];
}
