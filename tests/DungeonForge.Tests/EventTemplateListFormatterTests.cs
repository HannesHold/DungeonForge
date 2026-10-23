using DungeonForge.Configuration;
using DungeonForge.Models;
using DungeonForge.Services;

namespace DungeonForge.Tests;

public class EventTemplateListFormatterTests
{
    [Fact]
    public void Format_ListsValidTemplatesWithFieldsAndRoleCounts()
    {
        var templates = new[]
        {
            Template("MoltenCore40", "Molten Core", 40, 4, EventType.Raid,
                ("Tank", 4), ("Heal", 9), ("Melee-DPS", 13), ("Range-DPS", 14)),
            Template("WailingCaverns5", "Wailing Caverns", 5, 1.5, EventType.Dungeon,
                ("Tank", 1), ("Heal", 1), ("Melee-DPS", 1), ("Range-DPS", 2)),
        };

        var text = string.Join('\n', EventTemplateListFormatter.Format(templates, Array.Empty<string>()));

        Assert.Contains("**Event templates** (2)", text);
        Assert.Contains("/dfcreateevent", text);
        var raidsAt = text.IndexOf("**Raids**", StringComparison.Ordinal);
        var dungeonsAt = text.IndexOf("**Dungeons**", StringComparison.Ordinal);
        Assert.True(raidsAt >= 0 && raidsAt < dungeonsAt);
        Assert.True(raidsAt < text.IndexOf("**MoltenCore40**", StringComparison.Ordinal));
        Assert.True(text.IndexOf("**MoltenCore40**", StringComparison.Ordinal) < dungeonsAt);
        Assert.True(dungeonsAt < text.IndexOf("**WailingCaverns5**", StringComparison.Ordinal));
        Assert.Contains("**MoltenCore40**", text);
        Assert.Contains("Molten Core", text);
        Assert.Contains("attendiesRequired: 40", text);
        Assert.Contains("durationInHours: 4", text);
        Assert.Contains("4 Tank, 9 Heal, 13 Melee-DPS, 14 Range-DPS", text);
        Assert.Contains("**WailingCaverns5**", text);
        Assert.Contains("Wailing Caverns", text);
        Assert.Contains("attendiesRequired: 5", text);
        Assert.Contains("durationInHours: 1.5", text);
        Assert.Contains("1 Tank, 1 Heal, 1 Melee-DPS, 2 Range-DPS", text);
        Assert.DoesNotContain("Invalid templates", text);
    }

    [Fact]
    public void Format_ListsInvalidTemplatesWithTheirReasons()
    {
        var valid = new[]
        {
            Template("WailingCaverns5", "Wailing Caverns", 5, 1.5, EventType.Dungeon,
                ("Tank", 1), ("Heal", 1), ("Melee-DPS", 1), ("Range-DPS", 2)),
        };
        var errors = new[]
        {
            "Broken.json: \"displayName\" is missing.",
            "Broken.json: \"durationInHours\" must be a positive number (is 0).",
            "Nope.json: invalid JSON (expected '}')",
        };

        var text = string.Join('\n', EventTemplateListFormatter.Format(valid, errors));
        var invalidAt = text.IndexOf("**Invalid templates**", StringComparison.Ordinal);
        Assert.True(invalidAt > text.IndexOf("**WailingCaverns5**", StringComparison.Ordinal));

        var invalid = text[invalidAt..];
        Assert.Contains("**Broken.json**", invalid);
        Assert.Contains("\"displayName\" is missing.", invalid);
        Assert.Contains("\"durationInHours\" must be a positive number (is 0).", invalid);
        Assert.Contains("; ", invalid); // several problems of one file share a line
        Assert.Contains("**Nope.json**", invalid);
        Assert.Contains("invalid JSON", invalid);
        Assert.DoesNotContain("WailingCaverns5", invalid);
    }

    [Fact]
    public void Format_WhenThereAreNoTemplates_SaysSo()
    {
        var messages = EventTemplateListFormatter.Format(Array.Empty<EventDefinition>(), Array.Empty<string>());

        var text = Assert.Single(messages);
        Assert.Equal("There are no event templates.", text);
        Assert.DoesNotContain("Invalid templates", text);
    }

    [Fact]
    public void Format_WhenEveryFileIsInvalid_SaysThereAreNoValidTemplates()
    {
        var messages = EventTemplateListFormatter.Format(
            Array.Empty<EventDefinition>(),
            new[] { "Bad.json: file is empty." });

        var text = string.Join('\n', messages);
        Assert.Contains("There are no valid event templates.", text);
        Assert.Contains("**Invalid templates**", text);
        Assert.Contains("**Bad.json** – file is empty.", text);
    }

    [Fact]
    public void Format_SplitsAndTruncatesToTheLengthLimit()
    {
        var templates = new List<EventDefinition>();
        for (var i = 0; i < 8; i++)
        {
            templates.Add(Template(
                $"Raid{i}",
                new string('D', 80),
                10 + i,
                1.5,
                EventType.Raid,
                ("Tank", 2), ("Heal", 4), ("Melee-DPS", 2), ("Range-DPS", 2)));
        }

        const int limit = 250;
        var messages = EventTemplateListFormatter.Format(
            templates,
            new[] { "Huge.json: " + new string('e', 500) },
            limit);

        Assert.True(messages.Count > 1);
        Assert.All(messages, m => Assert.InRange(m.Length, 1, limit));
        Assert.All(messages, m => Assert.False(m.StartsWith('\n') || m.StartsWith(' ')));

        var all = string.Join('\n', messages);
        for (var i = 0; i < 8; i++)
            Assert.Contains($"**Raid{i}**", all);
        Assert.Contains("attendiesRequired: 17", all);
        Assert.Contains("durationInHours: 1.5", all);
        Assert.Contains("**Invalid templates**", all);
        Assert.Contains("**Huge.json**", all);
        Assert.Contains("… (truncated)", all);
        Assert.DoesNotContain(new string('e', 500), all);
    }

    [Fact]
    public void Format_ThirtyTemplates_ProduceChunksWithinDiscordLimit()
    {
        var templates = new List<EventDefinition>();
        for (var i = 0; i < 30; i++)
        {
            var hours = i % 2 == 0 ? 1.5 : 1;
            templates.Add(Template(
                $"Fixture{i:00}",
                $"Fixture number {i:00}",
                i == 0 ? 10 : 5,
                hours,
                i < 7 ? EventType.Raid : EventType.Dungeon,
                i == 0
                    ? new[] { ("Tank", 2), ("Heal", 2), ("Melee-DPS", 3), ("Range-DPS", 3) }
                    : new[] { ("Tank", 1), ("Heal", 1), ("Melee-DPS", 1), ("Range-DPS", 2) }));
        }

        var messages = EventTemplateListFormatter.Format(templates, Array.Empty<string>());

        Assert.True(messages.Count > 1);
        Assert.All(messages, m => Assert.InRange(m.Length, 1, EventTemplateListFormatter.DiscordMessageLimit));
        Assert.All(messages, m => Assert.False(m.StartsWith('\n') || m.StartsWith(' ') || m.EndsWith('\n')));

        var all = string.Join('\n', messages);
        Assert.Contains("**Event templates** (30)", all);
        Assert.Equal(30, all.Split('\n').Count(line => line.StartsWith("• **", StringComparison.Ordinal)));
        for (var i = 0; i < 30; i++)
            Assert.Contains($"**Fixture{i:00}**", all);
        Assert.Contains("durationInHours: 1.5", all);
        Assert.Contains("attendiesRequired: 10", all);
        Assert.Contains("2 Tank, 2 Heal, 3 Melee-DPS, 3 Range-DPS", all);
        Assert.DoesNotContain("… (truncated)", all);

        var root = ConfigurationTests.CopyConfigurationsToTemp(g => g);
        var cfg = new BotConfiguration(new GlobalConfig(), TestSupport.Roles, TestSupport.FixedRoles,
            TimeZoneInfo.Utc, root);
        var (shipped, errors) = new EventDefinitionRepository(cfg).LoadAll();
        Assert.Empty(errors);
        Assert.Equal(30, shipped.Count);

        var shippedMessages = EventTemplateListFormatter.Format(shipped, errors);
        Assert.True(shippedMessages.Count > 1);
        Assert.All(shippedMessages, m => Assert.InRange(m.Length, 1, EventTemplateListFormatter.DiscordMessageLimit));
        var shippedText = string.Join('\n', shippedMessages);
        Assert.Contains("**Event templates** (30)", shippedText);
        Assert.True(shippedText.IndexOf("**Raids**", StringComparison.Ordinal) < shippedText.IndexOf("**Dungeons**", StringComparison.Ordinal));
        Assert.True(shippedText.IndexOf("Ahn'Qiraj Temple", StringComparison.Ordinal) < shippedText.IndexOf("Zul'Gurub", StringComparison.Ordinal));
        Assert.True(shippedText.IndexOf("**Dungeons**", StringComparison.Ordinal) < shippedText.IndexOf("Blackfathom Deeps", StringComparison.Ordinal));
        Assert.True(shippedText.IndexOf("Blackfathom Deeps", StringComparison.Ordinal) < shippedText.IndexOf("Zul'Farrak", StringComparison.Ordinal));
        Assert.Contains(
            "**WailingCaverns5** – Wailing Caverns – attendiesRequired: 5, durationInHours: 1.5 – 1 Tank, 1 Heal, 1 Melee-DPS, 2 Range-DPS",
            shippedText);
        Assert.Contains(
            "**RagefireChasm5** – Ragefire Chasm – attendiesRequired: 5, durationInHours: 1 – 1 Tank, 1 Heal, 1 Melee-DPS, 2 Range-DPS",
            shippedText);
        Assert.Contains(
            "**BlackrockSpire10** – Blackrock Spire – attendiesRequired: 10, durationInHours: 3 – 2 Tank, 2 Heal, 3 Melee-DPS, 3 Range-DPS",
            shippedText);
        Assert.Contains(
            "**MoltenCore40** – Molten Core – attendiesRequired: 40, durationInHours: 4 – 4 Tank, 9 Heal, 13 Melee-DPS, 14 Range-DPS",
            shippedText);
        Assert.Equal(30, shippedText.Split('\n').Count(line => line.StartsWith("• **", StringComparison.Ordinal)));
        Assert.DoesNotContain("… (truncated)", shippedText);
    }

    [Fact]
    public void LoadAll_InvalidFile_IsListedAndDoesNotThrow()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var dir = Path.Combine(root, "EventConfigurations");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Ok.json"), """
            {
              "name": "Ok",
              "type": "Raid",
              "displayName": "Ok Raid",
              "attendiesRequired": 2,
              "attendyPerRole": { "Tank": 1, "Heal": 1 },
              "durationInHours": 2,
              "bosses": [ { "name": "Boss One" } ]
            }
            """);
        File.WriteAllText(Path.Combine(dir, "Bad.json"), "{ nope");

        var cfg = new BotConfiguration(new GlobalConfig(), TestSupport.Roles, TestSupport.FixedRoles,
            TimeZoneInfo.Utc, root);
        var (valid, errors) = new EventDefinitionRepository(cfg).LoadAll();
        var text = string.Join('\n', EventTemplateListFormatter.Format(valid, errors));

        Assert.Contains("**Ok**", text);
        Assert.Contains("Ok Raid", text);
        Assert.Contains("attendiesRequired: 2", text);
        Assert.Contains("durationInHours: 2", text);
        Assert.Contains("1 Tank, 1 Heal", text);
        Assert.Contains("**Invalid templates**", text);
        Assert.Contains("**Bad.json**", text);
        Assert.Contains("invalid JSON", text);
    }

    [Fact]
    public void Format_ListsRaidsBeforeDungeons_AlphabeticalWithinEach()
    {
        var templates = new[]
        {
            Template("ZulGurub20", "Zul'Gurub", 20, 3, EventType.Raid,
                ("Tank", 2), ("Heal", 4), ("Melee-DPS", 7), ("Range-DPS", 7)),
            Template("Deadmines5", "Deadmines", 5, 1, EventType.Dungeon,
                ("Tank", 1), ("Heal", 1), ("Melee-DPS", 1), ("Range-DPS", 2)),
            Template("MoltenCore40", "Molten Core", 40, 4, EventType.Raid,
                ("Tank", 4), ("Heal", 9), ("Melee-DPS", 13), ("Range-DPS", 14)),
            Template("BlackrockSpire10", "Blackrock Spire", 10, 3, EventType.Dungeon,
                ("Tank", 2), ("Heal", 2), ("Melee-DPS", 3), ("Range-DPS", 3)),
        };

        var text = string.Join('\n', EventTemplateListFormatter.Format(templates, Array.Empty<string>()));
        var raidsAt = text.IndexOf("**Raids** (2)", StringComparison.Ordinal);
        var dungeonsAt = text.IndexOf("**Dungeons** (2)", StringComparison.Ordinal);
        Assert.True(raidsAt >= 0 && raidsAt < dungeonsAt);
        Assert.Contains("**Event templates** (4)", text);
        Assert.True(text.IndexOf("Molten Core", StringComparison.Ordinal) < text.IndexOf("Zul'Gurub", StringComparison.Ordinal));
        Assert.True(text.IndexOf("Zul'Gurub", StringComparison.Ordinal) < dungeonsAt);
        Assert.True(dungeonsAt < text.IndexOf("Blackrock Spire", StringComparison.Ordinal));
        Assert.True(text.IndexOf("Blackrock Spire", StringComparison.Ordinal) < text.IndexOf("Deadmines", StringComparison.Ordinal));
    }

    [Fact]
    public void Format_TypeFilter_ListsOnlyThatType_InDisplayNameOrder()
    {
        var templates = new[]
        {
            Template("ZulGurub20", "Zul'Gurub", 20, 3, EventType.Raid,
                ("Tank", 2), ("Heal", 4), ("Melee-DPS", 7), ("Range-DPS", 7)),
            Template("Deadmines5", "Deadmines", 5, 1, EventType.Dungeon,
                ("Tank", 1), ("Heal", 1), ("Melee-DPS", 1), ("Range-DPS", 2)),
            Template("MoltenCore40", "Molten Core", 40, 4, EventType.Raid,
                ("Tank", 4), ("Heal", 9), ("Melee-DPS", 13), ("Range-DPS", 14)),
            Template("BlackrockSpire10", "Blackrock Spire", 10, 3, EventType.Dungeon,
                ("Tank", 2), ("Heal", 2), ("Melee-DPS", 3), ("Range-DPS", 3)),
        };
        var errors = new[] { "Bad.json: file is empty." };

        var raids = string.Join('\n', EventTemplateListFormatter.Format(templates, errors, type: EventType.Raid));
        Assert.Contains("**Raids** (2)", raids);
        Assert.Contains("/dfcreateevent", raids);
        Assert.DoesNotContain("**Dungeons**", raids);
        Assert.DoesNotContain("**Event templates**", raids);
        Assert.DoesNotContain("Deadmines", raids);
        Assert.DoesNotContain("Blackrock Spire", raids);
        Assert.True(raids.IndexOf("Molten Core", StringComparison.Ordinal) < raids.IndexOf("Zul'Gurub", StringComparison.Ordinal));
        Assert.Contains("**Invalid templates**", raids);
        Assert.Contains("**Bad.json**", raids);

        var dungeons = string.Join('\n', EventTemplateListFormatter.Format(templates, Array.Empty<string>(), type: EventType.Dungeon));
        Assert.Contains("**Dungeons** (2)", dungeons);
        Assert.DoesNotContain("**Raids**", dungeons);
        Assert.DoesNotContain("Molten Core", dungeons);
        Assert.DoesNotContain("Zul'Gurub", dungeons);
        Assert.DoesNotContain("Invalid templates", dungeons);
        Assert.True(dungeons.IndexOf("Blackrock Spire", StringComparison.Ordinal) < dungeons.IndexOf("Deadmines", StringComparison.Ordinal));
    }

    [Fact]
    public void Format_TypeFilter_WhenNoneMatch_SaysSo()
    {
        var templates = new[]
        {
            Template("Deadmines5", "Deadmines", 5, 1, EventType.Dungeon,
                ("Tank", 1), ("Heal", 1), ("Melee-DPS", 1), ("Range-DPS", 2)),
        };

        var text = Assert.Single(EventTemplateListFormatter.Format(templates, Array.Empty<string>(), type: EventType.Raid));
        Assert.Equal("There are no raid templates.", text);

        var withErrors = string.Join('\n', EventTemplateListFormatter.Format(
            Array.Empty<EventDefinition>(),
            new[] { "Bad.json: file is empty." },
            type: EventType.Dungeon));
        Assert.Contains("There are no dungeon templates.", withErrors);
        Assert.Contains("**Invalid templates**", withErrors);
        Assert.Contains("**Bad.json**", withErrors);
    }

    [Fact]
    public void Format_DoesNotLeaveASectionHeadingAtTheEndOfAChunk()
    {
        var templates = new[]
        {
            Template("MoltenCore40", "Molten Core", 40, 4, EventType.Raid,
                ("Tank", 4), ("Heal", 9), ("Melee-DPS", 13), ("Range-DPS", 14)),
            Template("Deadmines5", "Deadmines", 5, 1, EventType.Dungeon,
                ("Tank", 1), ("Heal", 1), ("Melee-DPS", 1), ("Range-DPS", 2)),
        };

        var lines = EventTemplateListFormatter.Format(templates, Array.Empty<string>(), maxLength: 10_000).Single().Split('\n');
        var heading = Array.FindIndex(lines, line => line.StartsWith("**Dungeons**", StringComparison.Ordinal));
        Assert.True(heading > 0);
        var before = string.Join('\n', lines[..heading]);
        var limit = before.Length + 1 + lines[heading].Length;

        var messages = EventTemplateListFormatter.Format(templates, Array.Empty<string>(), limit);
        Assert.True(messages.Count > 1);
        Assert.All(messages, message => Assert.InRange(message.Length, 1, limit));
        Assert.All(messages, message =>
        {
            var last = message.Split('\n')[^1];
            Assert.False(last.StartsWith("**", StringComparison.Ordinal), $"orphaned heading: {last}");
        });
        Assert.Contains(messages, message =>
            message.Contains("**Dungeons**", StringComparison.Ordinal) && message.Contains("**Deadmines5**", StringComparison.Ordinal));
    }

    private static EventDefinition Template(
        string name, string displayName, int required, double hours, EventType type, params (string Role, int Count)[] roles)
    {
        var perRole = new Dictionary<string, int>();
        foreach (var (role, count) in roles)
            perRole[role] = count;

        return new EventDefinition
        {
            Name = name,
            DisplayName = displayName,
            Type = type,
            AttendiesRequired = required,
            DurationInHours = hours,
            AttendyPerRole = perRole,
        };
    }
}
