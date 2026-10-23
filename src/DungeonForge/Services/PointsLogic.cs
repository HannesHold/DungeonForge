using DungeonForge.Models;

namespace DungeonForge.Services;

/// <summary>Pure points, kill-roster and open-event rules. No Discord calls.</summary>
public static class PointsLogic
{
    public const string BenchRole = "Bench";
    public const string CantAttendRole = "Cant-attend";
    public const string ManualAttendeeRole = "Attendee";
    public const int RecentEntryCount = 10;
    public const int LeaderboardSize = 20;

    public readonly record struct RosterResult(bool Ok, string Message);

    public sealed record CompletionResult(
        bool AlreadyCompleted,
        bool AwardsPoints,
        IReadOnlyList<string> KilledBosses,
        IReadOnlyList<PointsLedgerEntry> Entries);

    public sealed record PlayerTotal(ulong UserId, string Roles, int Points, int Kills);

    /// <summary>Dungeons cannot award points. A raid defaults to on unless the manager passes false.</summary>
    public static string? DungeonPointsError(EventType? type, bool? requested) =>
        type == EventType.Dungeon && requested == true
            ? "Dungeons cannot award points. Leave awardpoints off, or set it to false."
            : null;

    public static bool AwardsPointsForNewEvent(EventType? type, bool? requested) =>
        type != EventType.Dungeon && requested != false;

    public static int ResolvePoints(BossDefinition? boss, int pointsPerBossKill) =>
        boss?.Points ?? pointsPerBossKill;

    /// <summary>Nearest integer, halves away from zero, never below zero.</summary>
    public static int BenchShare(int bossPoints, int benchPercent)
    {
        if (bossPoints <= 0 || benchPercent <= 0)
            return 0;
        if (benchPercent >= 100)
            return bossPoints;

        var rounded = (int)Math.Round(bossPoints * (benchPercent / 100m), MidpointRounding.AwayFromZero);
        return Math.Max(0, rounded);
    }

    public static bool IsBench(string? role) =>
        role is not null && role.Equals(BenchRole, StringComparison.OrdinalIgnoreCase);

    public static bool IsCantAttend(string? role) =>
        role is not null && role.Equals(CantAttendRole, StringComparison.OrdinalIgnoreCase);

    public static bool IsNormalRole(EventInstance e, string? role) =>
        role is not null && (e.AttendyPerRole?.Keys.Any(k => k.Equals(role, StringComparison.OrdinalIgnoreCase)) ?? false);

    /// <summary>Full means normal-role sign-ups (including players who are excluded but still signed up) meet the requirement.</summary>
    public static bool RaidIsFull(EventInstance e) =>
        SignupLogic.CountAttending(e) >= e.AttendiesRequired;

    public static RosterResult RecordKill(EventInstance e, string bossName, IReadOnlyList<BossDefinition>? bosses, DateTimeOffset now)
    {
        if (Closed(e) is { } closed)
            return closed;
        if (!e.AwardsPoints)
            return new RosterResult(false, "That event does not award points.");

        var match = FindBoss(bosses, bossName);
        if (match is null)
            return new RosterResult(false, $"'{bossName?.Trim()}' is not a boss of this raid.");

        e.Kills ??= new List<BossKillRecord>();
        if (e.Kills.Any(k => k.Boss.Equals(match.Name, StringComparison.OrdinalIgnoreCase)))
            return new RosterResult(false, $"**{match.Name}** is already recorded as killed.");

        e.Kills.Add(TakeSnapshot(e, match.Name, now));
        var kill = e.Kills[^1];
        var full = kill.RaidWasFull ? "raid full" : "raid not full";
        return new RosterResult(true, $"Recorded **{match.Name}** ({kill.Attendees.Count} attending, {full}, {kill.Bench.Count} on bench).");
    }

    public static RosterResult UndoKill(EventInstance e, string bossName)
    {
        if (Closed(e) is { } closed)
            return closed;
        if (!e.AwardsPoints)
            return new RosterResult(false, "That event does not award points.");

        var kill = FindKill(e, bossName);
        if (kill is null)
            return new RosterResult(false, "No recorded kill matches that boss.");

        e.Kills.Remove(kill);
        return new RosterResult(true, $"Removed the kill **{kill.Boss}**.");
    }

    public static RosterResult Exclude(EventInstance e, ulong userId)
    {
        if (Closed(e) is { } closed)
            return closed;
        if (!e.AwardsPoints)
            return new RosterResult(false, "That event does not award points.");

        e.ExcludedUserIds ??= new List<ulong>();
        if (e.ExcludedUserIds.Contains(userId))
            return new RosterResult(false, "That player is already excluded from future kills.");

        e.ExcludedUserIds.Add(userId);
        return new RosterResult(true, "That player will be left out of future boss-kill snapshots.");
    }

    public static RosterResult Include(EventInstance e, ulong userId)
    {
        if (Closed(e) is { } closed)
            return closed;
        if (!e.AwardsPoints)
            return new RosterResult(false, "That event does not award points.");

        var signup = e.Signups?.FirstOrDefault(s => s.UserId == userId);
        if (signup is null || (!IsNormalRole(e, signup.RoleKey) && !IsBench(signup.RoleKey)))
            return new RosterResult(false, "That player must be signed up with a role or Bench.");

        e.ExcludedUserIds ??= new List<ulong>();
        if (!e.ExcludedUserIds.Remove(userId))
            return new RosterResult(true, "That player is already included in future boss-kill snapshots.");

        return new RosterResult(true, "That player will be included in future boss-kill snapshots.");
    }

    public static RosterResult EditAttendee(EventInstance e, string bossName, ulong userId, bool add)
    {
        if (Closed(e) is { } closed)
            return closed;
        if (!e.AwardsPoints)
            return new RosterResult(false, "That event does not award points.");

        var kill = FindKill(e, bossName);
        if (kill is null)
            return new RosterResult(false, "No recorded kill matches that boss.");

        kill.Attendees ??= new List<KillAttendee>();
        kill.Bench ??= new List<KillAttendee>();
        var existing = kill.Attendees.FirstOrDefault(a => a.UserId == userId);

        if (add)
        {
            if (existing is not null)
                return new RosterResult(false, $"That player is already on the attendee list for **{kill.Boss}**.");

            var removedBench = kill.Bench.RemoveAll(b => b.UserId == userId) > 0;
            kill.Attendees.Add(new KillAttendee { UserId = userId, Role = RoleForManualAdd(e, userId) });
            return new RosterResult(true, removedBench
                ? $"Added that player to **{kill.Boss}** and removed them from that kill's bench."
                : $"Added that player to **{kill.Boss}**.");
        }

        if (existing is null)
            return new RosterResult(false, $"That player is not on the attendee list for **{kill.Boss}**.");

        kill.Attendees.Remove(existing);
        return new RosterResult(true, $"Removed that player from **{kill.Boss}**.");
    }

    public static BossKillRecord TakeSnapshot(EventInstance e, string bossName, DateTimeOffset now)
    {
        var excluded = (e.ExcludedUserIds ?? new List<ulong>()).ToHashSet();
        var attendees = new List<KillAttendee>();
        var bench = new List<KillAttendee>();
        foreach (var signup in (e.Signups ?? new List<Signup>()).OrderBy(s => s.SignedUpAt).ThenBy(s => s.UserId))
        {
            if (excluded.Contains(signup.UserId) || IsCantAttend(signup.RoleKey))
                continue;
            if (IsNormalRole(e, signup.RoleKey))
                attendees.Add(new KillAttendee { UserId = signup.UserId, Role = CanonicalRole(e, signup.RoleKey) });
            else if (IsBench(signup.RoleKey))
                bench.Add(new KillAttendee { UserId = signup.UserId, Role = BenchRole });
        }

        return new BossKillRecord
        {
            Boss = bossName,
            KilledAt = now,
            RaidWasFull = RaidIsFull(e),
            Attendees = attendees,
            Bench = bench,
        };
    }

    public static List<PointsLedgerEntry> BuildEntries(
        EventInstance e,
        Func<string, int> pointsForBoss,
        int benchPercent,
        DateTimeOffset now)
    {
        var entries = new List<PointsLedgerEntry>();
        foreach (var kill in e.Kills ?? new List<BossKillRecord>())
        {
            var points = pointsForBoss(kill.Boss);
            var attendeeIds = new HashSet<ulong>();
            foreach (var attendee in kill.Attendees ?? new List<KillAttendee>())
            {
                if (!attendeeIds.Add(attendee.UserId) || points <= 0)
                    continue;
                entries.Add(NewEntry(e, kill.Boss, attendee.UserId, string.IsNullOrWhiteSpace(attendee.Role) ? ManualAttendeeRole : attendee.Role, points, now));
            }

            if (!kill.RaidWasFull)
                continue;

            var benchPoints = BenchShare(points, benchPercent);
            if (benchPoints <= 0)
                continue;

            foreach (var bench in kill.Bench ?? new List<KillAttendee>())
            {
                if (attendeeIds.Contains(bench.UserId))
                    continue;
                entries.Add(NewEntry(e, kill.Boss, bench.UserId, BenchRole, benchPoints, now));
            }
        }

        return entries;
    }

    public static CompletionResult Complete(
        EventInstance e,
        Func<string, int> pointsForBoss,
        int benchPercent,
        DateTimeOffset now,
        bool ledgerAlreadyHasEvent)
    {
        var bosses = BossNames(e);
        if (e.Status == EventStatus.Completed || ledgerAlreadyHasEvent)
        {
            if (e.Status != EventStatus.Completed)
            {
                e.Status = EventStatus.Completed;
                e.CompletedAt ??= now;
            }

            return new CompletionResult(true, e.AwardsPoints, bosses, Array.Empty<PointsLedgerEntry>());
        }

        IReadOnlyList<PointsLedgerEntry> entries = Array.Empty<PointsLedgerEntry>();
        if (e.AwardsPoints && bosses.Count > 0)
            entries = BuildEntries(e, pointsForBoss, benchPercent, now);

        e.Status = EventStatus.Completed;
        e.CompletedAt = now;
        return new CompletionResult(false, e.AwardsPoints, bosses, entries);
    }

    public static int Total(IEnumerable<PointsLedgerEntry> entries, ulong userId) =>
        entries.Where(entry => entry.UserId == userId).Sum(entry => entry.Points);

    public static IReadOnlyList<PointsLedgerEntry> Recent(IEnumerable<PointsLedgerEntry> entries, ulong userId, int take) =>
        entries.Where(entry => entry.UserId == userId)
            .OrderByDescending(entry => entry.Timestamp)
            .ThenByDescending(entry => entry.Id, StringComparer.Ordinal)
            .Take(Math.Max(0, take))
            .ToList();

    /// <summary>Highest total first. Equal totals are ordered by user id, lowest first.</summary>
    public static IReadOnlyList<(ulong UserId, int Total)> Leaderboard(IEnumerable<PointsLedgerEntry> entries) =>
        entries.GroupBy(entry => entry.UserId)
            .Select(group => (UserId: group.Key, Total: group.Sum(entry => entry.Points)))
            .Where(row => row.Total > 0)
            .OrderByDescending(row => row.Total)
            .ThenBy(row => row.UserId)
            .ToList();

    public static IReadOnlyList<PlayerTotal> PlayerTotals(IEnumerable<PointsLedgerEntry> entries) =>
        entries.GroupBy(entry => entry.UserId)
            .Select(group =>
            {
                var roles = new List<string>();
                foreach (var entry in group)
                {
                    if (!roles.Any(role => role.Equals(entry.Role, StringComparison.OrdinalIgnoreCase)))
                        roles.Add(entry.Role);
                }

                return new PlayerTotal(
                    group.Key,
                    string.Join(", ", roles),
                    group.Sum(entry => entry.Points),
                    group.Select(entry => entry.Boss).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            })
            .OrderByDescending(row => row.Points)
            .ThenBy(row => row.UserId)
            .ToList();

    private static RosterResult? Closed(EventInstance e) =>
        e.Status == EventStatus.Completed ? new RosterResult(false, "That event is already completed.") : null;

    private static BossDefinition? FindBoss(IReadOnlyList<BossDefinition>? bosses, string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || bosses is null)
            return null;
        var trimmed = name.Trim();
        return bosses.FirstOrDefault(boss => boss.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static BossKillRecord? FindKill(EventInstance e, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var trimmed = name.Trim();
        return (e.Kills ?? new List<BossKillRecord>())
            .FirstOrDefault(kill => kill.Boss.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static string RoleForManualAdd(EventInstance e, ulong userId)
    {
        var signup = e.Signups?.FirstOrDefault(s => s.UserId == userId);
        return signup is not null && IsNormalRole(e, signup.RoleKey)
            ? CanonicalRole(e, signup.RoleKey)
            : ManualAttendeeRole;
    }

    private static string CanonicalRole(EventInstance e, string role) =>
        e.AttendyPerRole.Keys.First(key => key.Equals(role, StringComparison.OrdinalIgnoreCase));

    private static List<string> BossNames(EventInstance e) =>
        (e.Kills ?? new List<BossKillRecord>()).Select(kill => kill.Boss).ToList();

    private static PointsLedgerEntry NewEntry(EventInstance e, string boss, ulong userId, string role, int points, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            EventId = e.MessageId,
            EventName = e.EventName,
            DisplayName = e.DisplayName,
            Boss = boss,
            UserId = userId,
            Role = role,
            Points = points,
            Timestamp = now,
        };
}
