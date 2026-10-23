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
            Template("MoltenCore20", "Molten Core (20)", 20, 4,
                ("Tank", 2), ("Heal", 4), ("Melee-DPS", 7), ("Range-DPS", 7)),
            Template("Dungeon5", "Dungeon (5)", 5, 1.5,
                ("Tank", 1), ("Heal", 1), ("Melee-DPS", 1), ("Range-DPS", 2)),
        };

        var text = string.Join('\n', EventTemplateListFormatter.Format(templates, Array.Empty<string>()));

        Assert.Contains("**Event templates** (2)", text);
        Assert.Contains("/dfcreateevent", text);
        Assert.Contains("**MoltenCore20**", text);
        Assert.Contains("Molten Core (20)", text);
        Assert.Contains("attendiesRequired: 20", text);
        Assert.Contains("durationInHours: 4", text);
        Assert.Contains("2 Tank, 4 Heal, 7 Melee-DPS, 7 Range-DPS", text);
        Assert.Contains("**Dungeon5**", text);
        Assert.Contains("Dungeon (5)", text);
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
            Template("Dungeon5", "Dungeon (5)", 5, 1.5,
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
        Assert.True(invalidAt > text.IndexOf("**Dungeon5**", StringComparison.Ordinal));

        var invalid = text[invalidAt..];
        Assert.Contains("**Broken.json**", invalid);
        Assert.Contains("\"displayName\" is missing.", invalid);
        Assert.Contains("\"durationInHours\" must be a positive number (is 0).", invalid);
        Assert.Contains("; ", invalid); // several problems of one file share a line
        Assert.Contains("**Nope.json**", invalid);
        Assert.Contains("invalid JSON", invalid);
        Assert.DoesNotContain("Dungeon5", invalid);
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
    public void LoadAll_InvalidFile_IsListedAndDoesNotThrow()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var dir = Path.Combine(root, "EventConfigurations");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Ok.json"), """
            {
              "name": "Ok",
              "displayName": "Ok Raid",
              "attendiesRequired": 2,
              "attendyPerRole": { "Tank": 1, "Heal": 1 },
              "durationInHours": 2
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

    private static EventDefinition Template(
        string name, string displayName, int required, double hours, params (string Role, int Count)[] roles)
    {
        var perRole = new Dictionary<string, int>();
        foreach (var (role, count) in roles)
            perRole[role] = count;

        return new EventDefinition
        {
            Name = name,
            DisplayName = displayName,
            AttendiesRequired = required,
            DurationInHours = hours,
            AttendyPerRole = perRole,
        };
    }
}
