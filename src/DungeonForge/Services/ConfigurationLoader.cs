using System.Text.Json;
using DungeonForge.Bot;
using DungeonForge.Configuration;
using DungeonForge.Models;

namespace DungeonForge.Services;

/// <summary>Loads and validates everything in the Configurations directory.</summary>
public static class ConfigurationLoader
{
    public const string TokenEnvironmentVariable = "DUNGEONFORGE_TOKEN";

    public static BotConfiguration Load(string baseDirectory, Func<string, string?>? getEnvironmentVariable = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        var dir = Path.Combine(baseDirectory, "Configurations");
        var errors = new List<string>();

        var global = ReadFile<GlobalConfig>(Path.Combine(dir, "global.json"), errors);
        var roles = ReadFile<List<RoleDefinition>>(Path.Combine(dir, "subscription-roles.json"), errors);
        var fixedRoles = ReadFile<List<RoleDefinition>>(Path.Combine(dir, "fixed-subscription-roles.json"), errors);

        if (global is not null)
        {
            var envToken = getEnvironmentVariable(TokenEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(envToken))
                global.Token = envToken.Trim();
        }

        TimeZoneInfo? tz = null;
        if (global is not null)
        {
            ValidateGlobal(global, errors);
            tz = ResolveTimeZone(global.TimeZone, errors);
        }
        if (roles is not null) ValidateRoles(roles, "subscription-roles.json", errors, requireAtLeastOne: true);
        if (fixedRoles is not null) ValidateRoles(fixedRoles, "fixed-subscription-roles.json", errors, requireAtLeastOne: false);
        if (roles is not null && fixedRoles is not null)
            ValidateCombinedRoles(roles, fixedRoles, errors);

        if (errors.Count > 0 || global is null || roles is null || fixedRoles is null || tz is null)
            throw new ConfigurationException(errors);

        return new BotConfiguration(global, roles, fixedRoles, tz, baseDirectory);
    }

    public static T? ReadFile<T>(string path, List<string> errors) where T : class
    {
        var name = Path.GetFileName(path);
        if (!File.Exists(path))
        {
            errors.Add($"{name}: file not found at '{path}'.");
            return null;
        }

        try
        {
            var result = JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonDefaults.ConfigFiles);
            if (result is null) errors.Add($"{name}: file is empty.");
            return result;
        }
        catch (JsonException ex)
        {
            errors.Add($"{name}: invalid JSON ({ex.Message})");
            return null;
        }
    }

    public static void ValidateGlobal(GlobalConfig g, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(g.Token) || g.Token.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
            errors.Add($"global.json: \"token\" is still the placeholder. Put your bot token there or set the environment variable {TokenEnvironmentVariable}.");

        if (g.GuildId == 0)
            errors.Add("global.json: \"guildId\" is not set (still 0). Enter the id of your Discord server.");

        CheckNamedId(g.Channels.Events, "channels.events", errors);
        CheckNamedId(g.Channels.Manage, "channels.manage", errors);
        CheckNamedId(g.Channels.Log, "channels.log", errors);
        CheckNamedId(g.Roles.Dungeoneer, "roles.dungeoneer", errors);
        CheckNamedId(g.Roles.Manager, "roles.manager", errors);
        CheckNamedId(g.Roles.Logs, "roles.logs", errors);

        if (g.FinishedEventRetentionHours < 0)
            errors.Add("global.json: \"finishedEventRetentionHours\" must not be negative.");
        if (string.IsNullOrWhiteSpace(g.DataDirectory))
            errors.Add("global.json: \"dataDirectory\" must not be empty.");
    }

    private static void CheckNamedId(NamedId? value, string path, List<string> errors)
    {
        if (value is null || (value.Id == 0 && string.IsNullOrWhiteSpace(value.Name)))
            errors.Add($"global.json: \"{path}\" needs a \"name\" and/or an \"id\".");
    }

    private static TimeZoneInfo? ResolveTimeZone(string id, List<string> errors)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            errors.Add($"global.json: time zone \"{id}\" was not found on this machine. Use an IANA id such as \"Europe/Vienna\" (Windows also accepts ids like \"W. Europe Standard Time\").");
            return null;
        }
    }

    public static void ValidateRoles(IReadOnlyList<RoleDefinition> roles, string fileName, List<string> errors, bool requireAtLeastOne)
    {
        if (requireAtLeastOne && roles.Count == 0)
            errors.Add($"{fileName}: at least one role is required.");

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < roles.Count; i++)
        {
            var r = roles[i];
            if (r is null) { errors.Add($"{fileName}: entry #{i + 1} is null."); continue; }
            var label = string.IsNullOrWhiteSpace(r.Key) ? $"entry #{i + 1}" : $"role '{r.Key}'";
            if (string.IsNullOrWhiteSpace(r.Key)) errors.Add($"{fileName}: {label} has no \"key\".");
            else if (!keys.Add(r.Key)) errors.Add($"{fileName}: duplicate role key '{r.Key}'.");
            if (string.IsNullOrWhiteSpace(r.DisplayName)) errors.Add($"{fileName}: {label} has no \"displayName\".");
            if (string.IsNullOrWhiteSpace(r.Emoji)) errors.Add($"{fileName}: {label} has no \"emoji\".");
        }
    }

    private static void ValidateCombinedRoles(IReadOnlyList<RoleDefinition> roles, IReadOnlyList<RoleDefinition> fixedRoles, List<string> errors)
    {
        var all = roles.Concat(fixedRoles).Where(r => r is not null).ToList();

        foreach (var dup in all.GroupBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
                     .Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Count() > 1 &&
                                 roles.Any(r => r.Key.Equals(g.Key, StringComparison.OrdinalIgnoreCase)) &&
                                 fixedRoles.Any(r => r.Key.Equals(g.Key, StringComparison.OrdinalIgnoreCase))))
            errors.Add($"Role key '{dup.Key}' exists in both subscription-roles.json and fixed-subscription-roles.json.");

        foreach (var dup in all.Where(r => !string.IsNullOrWhiteSpace(r.Emoji))
                     .GroupBy(r => EmojiKey.FromConfigured(r.Emoji))
                     .Where(g => g.Count() > 1))
            errors.Add($"The emoji '{dup.First().Emoji}' is used by more than one role ({string.Join(", ", dup.Select(r => r.Key))}). Each role needs its own emoji.");
    }
}
