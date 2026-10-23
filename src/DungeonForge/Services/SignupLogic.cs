using DungeonForge.Models;

namespace DungeonForge.Services;

/// <summary>Pure sign-up rules (no Discord involved): one role per user per event, counting, reconciliation.</summary>
public static class SignupLogic
{
    /// <summary>Result of <see cref="Apply"/>.</summary>
    public readonly record struct ApplyResult(bool Changed, string? PreviousRoleKey);

    /// <summary>Signs the user up for <paramref name="roleKey"/>, moving them if they already hold another role.</summary>
    public static ApplyResult Apply(EventInstance e, ulong userId, string roleKey, DateTimeOffset now)
    {
        var existing = e.Signups.FirstOrDefault(s => s.UserId == userId);
        if (existing is not null)
        {
            if (existing.RoleKey.Equals(roleKey, StringComparison.OrdinalIgnoreCase))
                return new ApplyResult(false, null);

            var previous = existing.RoleKey;
            existing.RoleKey = roleKey;
            existing.SignedUpAt = now;
            return new ApplyResult(true, previous);
        }

        e.Signups.Add(new Signup { UserId = userId, RoleKey = roleKey, SignedUpAt = now });
        return new ApplyResult(true, null);
    }

    /// <summary>Removes the sign-up, but only if the user currently holds exactly <paramref name="roleKey"/>.</summary>
    public static bool Remove(EventInstance e, ulong userId, string roleKey)
    {
        var existing = e.Signups.FirstOrDefault(s =>
            s.UserId == userId && s.RoleKey.Equals(roleKey, StringComparison.OrdinalIgnoreCase));
        return existing is not null && e.Signups.Remove(existing);
    }

    public static IReadOnlyList<Signup> ForRole(EventInstance e, string roleKey) =>
        e.Signups.Where(s => s.RoleKey.Equals(roleKey, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.SignedUpAt)
            .ToList();

    public static int Count(EventInstance e, string roleKey) => ForRole(e, roleKey).Count;

    /// <summary>Number of sign-ups that count towards the required attendees (fixed roles like Bench are excluded).</summary>
    public static int CountAttending(EventInstance e) =>
        e.Signups.Count(s => e.AttendyPerRole.Keys.Any(k => k.Equals(s.RoleKey, StringComparison.OrdinalIgnoreCase)));

    public static bool IsFull(EventInstance e) => CountAttending(e) >= e.AttendiesRequired;

    public readonly record struct ReactionRemoval(ulong UserId, string RoleKey);

    public sealed record ReconcileResult(List<Signup> Signups, List<ReactionRemoval> ReactionsToRemove);

    /// <summary>
    /// Brings the stored sign-ups in line with the reactions that actually exist on the Discord message
    /// (used after a bot restart, when reactions may have changed while the bot was offline).
    /// </summary>
    /// <param name="reactionsByRole">role key -> users that reacted with that role's emoji (bots already removed).</param>
    /// <param name="roleOrder">Allowed role keys in priority order (used when someone holds several reactions and has no stored role).</param>
    /// <param name="isEligible">Whether a user may sign up at all (df-dungeoneer).</param>
    public static ReconcileResult Reconcile(
        EventInstance e,
        IReadOnlyDictionary<string, IReadOnlyCollection<ulong>> reactionsByRole,
        IReadOnlyList<string> roleOrder,
        Func<ulong, bool> isEligible,
        DateTimeOffset now)
    {
        var removals = new List<ReactionRemoval>();
        var result = new List<Signup>();

        // user -> roles they reacted with (in priority order)
        var byUser = new Dictionary<ulong, List<string>>();
        foreach (var role in roleOrder)
        {
            if (!reactionsByRole.TryGetValue(role, out var users)) continue;
            foreach (var user in users.Distinct())
            {
                if (!byUser.TryGetValue(user, out var list)) byUser[user] = list = new List<string>();
                list.Add(role);
            }
        }

        foreach (var (userId, roles) in byUser)
        {
            if (!isEligible(userId))
            {
                removals.AddRange(roles.Select(r => new ReactionRemoval(userId, r)));
                continue;
            }

            var stored = e.Signups.FirstOrDefault(s => s.UserId == userId);
            var keep = stored is not null
                ? roles.FirstOrDefault(r => r.Equals(stored.RoleKey, StringComparison.OrdinalIgnoreCase))
                : null;
            keep ??= roles[0];

            removals.AddRange(roles.Where(r => r != keep).Select(r => new ReactionRemoval(userId, r)));
            var keepsStoredRole = stored is not null && stored.RoleKey.Equals(keep, StringComparison.OrdinalIgnoreCase);
            result.Add(new Signup
            {
                UserId = userId,
                RoleKey = keep,
                SignedUpAt = keepsStoredRole ? stored!.SignedUpAt : now,
            });
        }

        result = result.OrderBy(s => s.SignedUpAt).ThenBy(s => s.UserId).ToList();
        return new ReconcileResult(result, removals);
    }
}
