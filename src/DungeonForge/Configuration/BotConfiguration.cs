using DungeonForge.Bot;
using DungeonForge.Models;

namespace DungeonForge.Configuration;

/// <summary>Everything loaded from the Configurations directory, validated and ready to use.</summary>
public sealed class BotConfiguration
{
    private readonly Dictionary<string, RoleDefinition> _byKey;
    private readonly Dictionary<string, RoleDefinition> _byEmoji;

    public BotConfiguration(
        GlobalConfig global,
        IReadOnlyList<RoleDefinition> subscriptionRoles,
        IReadOnlyList<RoleDefinition> fixedRoles,
        TimeZoneInfo timeZone,
        string baseDirectory,
        Func<string, string>? emojiNormalizer = null)
    {
        Global = global;
        SubscriptionRoles = subscriptionRoles;
        FixedRoles = fixedRoles;
        TimeZone = timeZone;
        BaseDirectory = baseDirectory;

        var normalize = emojiNormalizer ?? EmojiKey.FromConfigured;
        AllRoles = subscriptionRoles.Concat(fixedRoles).ToList();
        _byKey = AllRoles.ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);
        _byEmoji = AllRoles.ToDictionary(r => normalize(r.Emoji), StringComparer.Ordinal);
    }

    public GlobalConfig Global { get; }
    public IReadOnlyList<RoleDefinition> SubscriptionRoles { get; }
    public IReadOnlyList<RoleDefinition> FixedRoles { get; }
    public IReadOnlyList<RoleDefinition> AllRoles { get; }
    public TimeZoneInfo TimeZone { get; }
    public string BaseDirectory { get; }

    public string ConfigurationsDirectory => Path.Combine(BaseDirectory, "Configurations");
    public string EventConfigurationsDirectory => Path.Combine(BaseDirectory, "EventConfigurations");
    public string DataDirectory => Path.GetFullPath(Path.Combine(BaseDirectory, Global.DataDirectory));

    public RoleDefinition? FindRole(string key) => _byKey.GetValueOrDefault(key);

    /// <summary>Finds a role by an already normalized emoji key (see <see cref="EmojiKey"/>).</summary>
    public RoleDefinition? FindRoleByEmojiKey(string normalizedEmoji) => _byEmoji.GetValueOrDefault(normalizedEmoji);

    public bool IsFixedRole(string key) =>
        FixedRoles.Any(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
}
