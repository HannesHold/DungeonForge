using DungeonForge.Models;

namespace DungeonForge.Services;

/// <summary>Completion and /dfpoints text. Chunked to Discord's message limit.</summary>
public static class PointsSummaryFormatter
{
    public static IReadOnlyList<string> FormatCompletion(
        EventInstance e,
        bool alreadyCompleted,
        IReadOnlyList<PointsLedgerEntry> entries,
        Func<string, int> pointsForBoss,
        int maxLength = EventTemplateListFormatter.DiscordMessageLimit)
    {
        if (alreadyCompleted)
            return Pack(new[] { $"**{e.DisplayName}** is already completed." }, maxLength);

        if (!e.AwardsPoints)
            return Pack(new[] { $"**{e.DisplayName}** is completed. This event does not award points." }, maxLength);

        var kills = e.Kills ?? new List<BossKillRecord>();
        if (kills.Count == 0)
            return Pack(new[] { $"**{e.DisplayName}** is completed. No bosses were killed, so no points were paid." }, maxLength);

        var lines = new List<string>
        {
            $"**{e.DisplayName}** is completed.",
            "Bosses killed: " + string.Join(", ", kills.Select(kill => $"{kill.Boss} ({pointsForBoss(kill.Boss)})")),
        };

        var players = PointsLogic.PlayerTotals(entries);
        if (players.Count == 0)
            lines.Add("No points were paid.");
        else
        {
            lines.Add("");
            foreach (var player in players)
            {
                var killWord = player.Kills == 1 ? "kill" : "kills";
                lines.Add($"• {EventRenderer.Mention(player.UserId)} ({player.Roles}) — {player.Points} points, {player.Kills} {killWord}");
            }
        }

        return Pack(lines, maxLength);
    }

    public static IReadOnlyList<string> FormatUser(
        ulong userId,
        int total,
        IReadOnlyList<PointsLedgerEntry> recent,
        int maxLength = EventTemplateListFormatter.DiscordMessageLimit)
    {
        var lines = new List<string>
        {
            $"**Points for {EventRenderer.Mention(userId)}**",
            $"Total: **{total}**",
        };

        if (recent.Count == 0)
            lines.Add("No points yet.");
        else
        {
            lines.Add("");
            lines.Add("Recent:");
            foreach (var entry in recent)
            {
                var when = EventRenderer.DiscordTimestamp(entry.Timestamp, 'f');
                lines.Add($"• {entry.DisplayName} — {entry.Boss} — {entry.Role} — {entry.Points} — {when}");
            }
        }

        return Pack(lines, maxLength);
    }

    public static IReadOnlyList<string> FormatLeaderboard(
        IReadOnlyList<(ulong UserId, int Total)> ranked,
        int cap,
        int maxLength = EventTemplateListFormatter.DiscordMessageLimit)
    {
        var text = PointsBoardFormatter.FormatLeaderboard(ranked, cap, maxLength, "Points leaderboard");
        return new[] { text };
    }

    private static IReadOnlyList<string> Pack(IReadOnlyList<string> lines, int maxLength)
    {
        var messages = DiscordMessagePacker.Pack(lines, maxLength);
        return messages.Count == 0 ? new[] { "Completed." } : messages;
    }
}
