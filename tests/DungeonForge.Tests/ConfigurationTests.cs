using System.Text.Json;
using DungeonForge.Bot;
using DungeonForge.Configuration;
using DungeonForge.Models;
using DungeonForge.Services;

namespace DungeonForge.Tests;

public class ConfigurationTests
{
    [Fact]
    public void ShippedGlobalConfig_IsRejectedBecauseOfPlaceholders()
    {
        var ex = Assert.Throws<ConfigurationException>(() => ConfigurationLoader.Load(TestSupport.ProjectDir, _ => null));
        Assert.Contains(ex.Errors, e => e.Contains("token"));
        Assert.Contains(ex.Errors, e => e.Contains("guildId"));
        Assert.Equal(2, ex.Errors.Count); // nothing else is wrong with the shipped files
    }

    [Fact]
    public void ShippedConfig_LoadsWhenTokenAndGuildAreProvided()
    {
        var dir = CopyConfigurationsToTemp(g => g.Replace("\"guildId\": 0", "\"guildId\": 42"));
        var cfg = ConfigurationLoader.Load(dir, name => name == "DUNGEONFORGE_TOKEN" ? "env-token-value" : null);

        Assert.Equal("env-token-value", cfg.Global.Token); // env var overrides the placeholder
        Assert.Equal(42UL, cfg.Global.GuildId);
        Assert.Equal("Europe/Vienna", cfg.TimeZone.Id);
        Assert.Equal(new[] { "Tank", "Heal", "Melee-DPS", "Range-DPS" }, cfg.SubscriptionRoles.Select(r => r.Key));
        Assert.Equal(new[] { "Bench", "Cant-attend" }, cfg.FixedRoles.Select(r => r.Key));
        Assert.Equal("dungeon-forge", cfg.Global.Channels.Events.Name);
        Assert.Equal("dungeon-forge-manage", cfg.Global.Channels.Manage.Name);
        Assert.Equal("dungeon-forge-log", cfg.Global.Channels.Log.Name);
        Assert.Equal("df-dungeoneer", cfg.Global.Roles.Dungeoneer.Name);
        Assert.Equal("df-manager", cfg.Global.Roles.Manager.Name);
        Assert.Equal("df-logs", cfg.Global.Roles.Logs.Name);
        Assert.True(cfg.Global.DeleteStrayMessages);
        Assert.True(cfg.Global.DeleteStrayThreadMessages);
        Assert.True(cfg.Global.ArchiveCompletedEventThreads);
        Assert.Equal(10, cfg.Global.PointsPerBossKill);
        Assert.Equal(25, cfg.Global.BenchPointsPercent);
        Assert.Equal(10, cfg.Global.MaxOpenEvents);
        Assert.Equal("dungeon-forge-points", cfg.Global.Channels.Points.Name);
        Assert.Equal(0UL, cfg.Global.Channels.Points.Id);
    }

    [Fact]
    public void PointsSettings_RejectOutOfRangeValues_AndMissingKeysKeepDefaults()
    {
        var dir = CopyConfigurationsToTemp(g => g
            .Replace("\"guildId\": 0", "\"guildId\": 1")
            .Replace("\"pointsPerBossKill\": 10", "\"pointsPerBossKill\": 0")
            .Replace("\"benchPointsPercent\": 25", "\"benchPointsPercent\": 101")
            .Replace("\"maxOpenEvents\": 10", "\"maxOpenEvents\": 0"));
        var ex = Assert.Throws<ConfigurationException>(() => ConfigurationLoader.Load(dir, _ => "tok"));
        Assert.Contains(ex.Errors, e => e.Contains("pointsPerBossKill"));
        Assert.Contains(ex.Errors, e => e.Contains("benchPointsPercent"));
        Assert.Contains(ex.Errors, e => e.Contains("maxOpenEvents"));

        var defaults = JsonSerializer.Deserialize<GlobalConfig>("""{ "token": "t", "guildId": 1 }""", JsonDefaults.ConfigFiles);
        Assert.NotNull(defaults);
        Assert.Equal(10, defaults.PointsPerBossKill);
        Assert.Equal(25, defaults.BenchPointsPercent);
        Assert.Equal(10, defaults.MaxOpenEvents);
        Assert.True(defaults.ArchiveCompletedEventThreads);
        Assert.True(defaults.DeleteStrayThreadMessages);
        Assert.Equal("dungeon-forge-points", defaults.Channels.Points.Name);
    }

    [Fact]
    public void MissingFiles_ProduceClearErrors()
    {
        var empty = Directory.CreateTempSubdirectory().FullName;
        Directory.CreateDirectory(Path.Combine(empty, "Configurations"));
        var ex = Assert.Throws<ConfigurationException>(() => ConfigurationLoader.Load(empty, _ => null));
        Assert.Equal(3, ex.Errors.Count);
        Assert.All(ex.Errors, e => Assert.Contains("not found", e));
    }

    [Fact]
    public void InvalidJson_IsReported()
    {
        var dir = CopyConfigurationsToTemp(g => g);
        File.WriteAllText(Path.Combine(dir, "Configurations", "subscription-roles.json"), "[ { broken");
        var ex = Assert.Throws<ConfigurationException>(() => ConfigurationLoader.Load(dir, n => "tok"));
        Assert.Contains(ex.Errors, e => e.Contains("subscription-roles.json") && e.Contains("invalid JSON"));
    }

    [Fact]
    public void UnknownTimeZone_IsReported()
    {
        var dir = CopyConfigurationsToTemp(g => g.Replace("\"guildId\": 0", "\"guildId\": 1").Replace("Europe/Vienna", "Mars/Olympus"));
        var ex = Assert.Throws<ConfigurationException>(() => ConfigurationLoader.Load(dir, n => "tok"));
        Assert.Contains(ex.Errors, e => e.Contains("Mars/Olympus"));
    }

    [Fact]
    public void RoleFiles_RejectDuplicateKeysAndEmojiVariants()
    {
        var errors = new List<string>();
        ConfigurationLoader.ValidateRoles(new List<RoleDefinition>
        {
            new() { Key = "Tank", DisplayName = "Tank", Emoji = "🛡️" },
            new() { Key = "tank", DisplayName = "Tank2", Emoji = "💚" },
            new() { Key = "", DisplayName = "x", Emoji = "" },
        }, "subscription-roles.json", errors, requireAtLeastOne: true);
        Assert.Contains(errors, e => e.Contains("duplicate role key"));
        Assert.Contains(errors, e => e.Contains("no \"key\""));
        Assert.Contains(errors, e => e.Contains("no \"emoji\""));
    }

    [Fact]
    public void EmojiKey_IgnoresVariationSelector_AndParsesCustomEmoji()
    {
        Assert.Equal(EmojiKey.FromConfigured("🛡️"), EmojiKey.FromConfigured("🛡"));
        Assert.Equal(EmojiKey.FromConfigured("⚔️"), EmojiKey.FromReaction(new Discord.Emoji("⚔")));
        Assert.Equal("custom:123456789012345678", EmojiKey.FromConfigured("<:tank:123456789012345678>"));
        Assert.Equal("custom:123456789012345678", EmojiKey.FromConfigured("<a:tank:123456789012345678>"));
        Assert.NotEqual(EmojiKey.FromConfigured("💚"), EmojiKey.FromConfigured("🏹"));
    }

    [Fact]
    public void ConfigurationFindsRolesByEmojiAndKey()
    {
        var cfg = TestSupport.Config();
        Assert.Equal("Tank", cfg.FindRoleByEmojiKey(EmojiKey.FromReaction(new Discord.Emoji("🛡")))?.Key);
        Assert.Equal("Bench", cfg.FindRole("bench")?.Key);
        Assert.True(cfg.IsFixedRole("Cant-attend"));
        Assert.False(cfg.IsFixedRole("Tank"));
        Assert.Null(cfg.FindRoleByEmojiKey(EmojiKey.FromReaction(new Discord.Emoji("🍕"))));
    }

    internal static string CopyConfigurationsToTemp(Func<string, string> editGlobal)
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var target = Path.Combine(root, "Configurations");
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(Path.Combine(TestSupport.ProjectDir, "Configurations")))
        {
            var text = File.ReadAllText(file);
            if (Path.GetFileName(file) == "global.json") text = editGlobal(text);
            File.WriteAllText(Path.Combine(target, Path.GetFileName(file)), text);
        }
        CopyDirectory(Path.Combine(TestSupport.ProjectDir, "EventConfigurations"), Path.Combine(root, "EventConfigurations"));
        return root;

        static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
            foreach (var directory in Directory.GetDirectories(source))
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
