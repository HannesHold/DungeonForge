using System.Globalization;
using Discord;
using DungeonForge.Configuration;
using DungeonForge.Models;

namespace DungeonForge.Services;

/// <summary>Builds the Discord embed / texts for an event. No network access, fully testable.</summary>
public sealed class EventRenderer
{
    private const int FieldValueLimit = 1024;
    private const int ThreadNameLimit = 100;

    private readonly BotConfiguration _config;

    public EventRenderer(BotConfiguration config) => _config = config;

    /// <summary>The sign-up roles offered for this event: roles with a required count first, then the fixed roles.</summary>
    public IReadOnlyList<RoleDefinition> RolesForEvent(EventInstance e)
    {
        var main = _config.SubscriptionRoles
            .Where(r => e.AttendyPerRole.Keys.Any(k => k.Equals(r.Key, StringComparison.OrdinalIgnoreCase)));
        return main.Concat(_config.FixedRoles).ToList();
    }

    public string FormatLocal(EventInstance e) =>
        TimeZoneInfo.ConvertTime(e.StartTime, _config.TimeZone).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    public string ThreadName(EventInstance e)
    {
        var name = $"{e.DisplayName} – {FormatLocal(e)}";
        return name.Length <= ThreadNameLimit ? name : name[..(ThreadNameLimit - 1)] + "…";
    }

    public static string DiscordTimestamp(DateTimeOffset time, char style) =>
        $"<t:{time.ToUnixTimeSeconds()}:{style}>";

    public static string Mention(ulong userId) => $"<@{userId}>";

    public Embed BuildEmbed(EventInstance e, bool cancelled = false)
    {
        var attending = SignupLogic.CountAttending(e);
        var full = attending >= e.AttendiesRequired;
        var completed = !cancelled && e.Status == EventStatus.Completed;
        var kills = e.Kills ?? new List<BossKillRecord>();

        var duration = e.DurationInHours.ToString("0.##", CultureInfo.InvariantCulture);
        var description = string.Join('\n', new[]
        {
            cancelled ? "❌ **This event was cancelled.**" : null,
            completed ? "✅ **Completed**" : null,
            $"🗓️ {DiscordTimestamp(e.StartTime, 'F')} ({DiscordTimestamp(e.StartTime, 'R')})",
            $"🕒 {FormatLocal(e)} ({_config.TimeZone.Id})",
            $"⏱️ Duration: {duration} h",
            $"👥 Signed up: **{attending}/{e.AttendiesRequired}**{(full && !cancelled && !completed ? " ✅ full" : "")}",
            e.AwardsPoints ? "Points: enabled" : "Points: disabled",
            e.AwardsPoints
                ? "Bosses killed: " + (kills.Count == 0 ? "none yet" : string.Join(", ", kills.Select(kill => kill.Boss)))
                : null,
        }.Where(l => l is not null));

        var builder = new EmbedBuilder()
            .WithTitle(e.DisplayName)
            .WithDescription(description)
            .WithColor(cancelled ? Color.DarkGrey : completed ? Color.Blue : full ? Color.Green : Color.Orange)
            .WithFooter(cancelled
                ? $"{e.EventName} • cancelled"
                : completed
                    ? $"{e.EventName} • completed"
                    : $"{e.EventName} • React with a role emoji to sign up. Remove your reaction to sign off.");

        foreach (var role in RolesForEvent(e))
        {
            var signups = SignupLogic.ForRole(e, role.Key);
            var isFixed = _config.IsFixedRole(role.Key);

            string header;
            if (isFixed)
            {
                header = $"{role.Emoji} {role.DisplayName} ({signups.Count})";
            }
            else
            {
                var required = e.AttendyPerRole.First(kv => kv.Key.Equals(role.Key, StringComparison.OrdinalIgnoreCase)).Value;
                header = $"{role.Emoji} {role.DisplayName} ({signups.Count}/{required})";
            }

            builder.AddField(header, FormatMentions(signups), inline: true);
        }

        var excluded = e.ExcludedUserIds;
        if (e.AwardsPoints && excluded is { Count: > 0 })
            builder.AddField("Excluded from future kills", FormatUserIds(excluded), inline: false);

        return builder.Build();
    }

    private static string FormatUserIds(IReadOnlyList<ulong> userIds)
    {
        var signups = userIds.Select(id => new Signup { UserId = id }).ToList();
        return FormatMentions(signups);
    }

    private static string FormatMentions(IReadOnlyList<Signup> signups)
    {
        if (signups.Count == 0) return "—";

        var lines = new List<string>();
        var length = 0;
        for (var i = 0; i < signups.Count; i++)
        {
            var line = Mention(signups[i].UserId);
            var remainingNote = $"… +{signups.Count - i} more";
            if (length + line.Length + 1 + remainingNote.Length > FieldValueLimit)
            {
                lines.Add(remainingNote);
                break;
            }
            lines.Add(line);
            length += line.Length + 1;
        }
        return string.Join('\n', lines);
    }
}
