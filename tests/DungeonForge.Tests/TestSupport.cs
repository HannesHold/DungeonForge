using DungeonForge.Configuration;
using DungeonForge.Models;

namespace DungeonForge.Tests;

internal static class TestSupport
{
    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DungeonForge.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("DungeonForge.sln not found above " + AppContext.BaseDirectory);
        }
    }

    public static string ProjectDir => Path.Combine(RepoRoot, "src", "DungeonForge");

    public static readonly List<RoleDefinition> Roles = new()
    {
        new() { Key = "Tank", DisplayName = "Tank", Emoji = "🛡️" },
        new() { Key = "Heal", DisplayName = "Heal", Emoji = "💚" },
        new() { Key = "Melee-DPS", DisplayName = "Melee-DPS", Emoji = "⚔️" },
        new() { Key = "Range-DPS", DisplayName = "Range-DPS", Emoji = "🏹" },
    };

    public static readonly List<RoleDefinition> FixedRoles = new()
    {
        new() { Key = "Bench", DisplayName = "Bench", Emoji = "🪑" },
        new() { Key = "Cant-attend", DisplayName = "Cant attend", Emoji = "❌" },
    };

    public static BotConfiguration Config(string timeZone = "Europe/Vienna") =>
        new(new GlobalConfig(), Roles, FixedRoles, TimeZoneInfo.FindSystemTimeZoneById(timeZone), Path.GetTempPath());

    /// <summary>A Molten Core (40) instance, matching the shipped MoltenCore40 template.</summary>
    public static EventInstance Event40() => new()
    {
        EventName = "MoltenCore40",
        DisplayName = "Molten Core",
        AttendiesRequired = 40,
        AttendyPerRole = new(StringComparer.OrdinalIgnoreCase)
            { ["Tank"] = 4, ["Heal"] = 9, ["Melee-DPS"] = 13, ["Range-DPS"] = 14 },
        StartTime = new DateTimeOffset(2026, 10, 24, 18, 0, 0, TimeSpan.FromHours(2)),
        DurationInHours = 4,
        MessageId = 111,
    };

    public static readonly DateTimeOffset Now = new(2026, 10, 23, 9, 0, 0, TimeSpan.FromHours(2));
}
