using DungeonForge.Configuration;
using DungeonForge.Models;
using DungeonForge.Services;

namespace DungeonForge.Tests;

public class EventDefinitionTests
{
    /// <summary>Every file shipped in EventConfigurations, in autocomplete (ordinal, case-insensitive) order.</summary>
    private static readonly (string Name, string DisplayName, int Players, double Hours, int Tank, int Heal, int Melee, int Range)[] ShippedTemplates =
    {
        ("AhnQirajTemple40", "Ahn'Qiraj Temple", 40, 5, 4, 9, 13, 14),
        ("BlackwingLair40", "Blackwing Lair", 40, 4, 4, 9, 13, 14),
        ("Dungeon10", "Upper Dungeon (10)", 10, 3, 2, 2, 3, 3),
        ("Dungeon5", "Dungeon (5)", 5, 1.5, 1, 1, 1, 2),
        ("MoltenCore20", "Molten Core (20)", 20, 4, 2, 4, 7, 7),
        ("MoltenCore40", "Molten Core", 40, 4, 4, 9, 13, 14),
        ("Naxxramas40", "Naxxramas", 40, 6, 4, 9, 13, 14),
        ("OnyxiasLair40", "Onyxia's Lair", 40, 2, 4, 9, 13, 14),
        ("RuinsOfAhnQiraj20", "Ruins of Ahn'Qiraj", 20, 3, 2, 4, 7, 7),
        ("ZulGurub20", "Zul'Gurub", 20, 3, 2, 4, 7, 7),
    };

    private static EventDefinitionRepository ShippedRepo()
    {
        var root = ConfigurationTests.CopyConfigurationsToTemp(g => g);
        var cfg = new BotConfiguration(new GlobalConfig(), TestSupport.Roles, TestSupport.FixedRoles,
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna"), root);
        return new EventDefinitionRepository(cfg);
    }

    [Fact]
    public void ShippedSamples_AreValid()
    {
        var repo = ShippedRepo();
        var (valid, errors) = repo.LoadAll();
        Assert.Empty(errors);

        var onDisk = Directory.GetFiles(Path.Combine(TestSupport.ProjectDir, "EventConfigurations"), "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(ShippedTemplates.Select(t => t.Name), onDisk);
        Assert.Equal(ShippedTemplates.Select(t => t.Name), valid.Select(v => v.Name));
        Assert.Equal(new[] { 5, 10, 20, 20, 20, 40, 40, 40, 40, 40 }, valid.Select(v => v.AttendiesRequired).Order());

        foreach (var spec in ShippedTemplates)
        {
            var def = valid.Single(v => v.Name == spec.Name);
            Assert.Equal(spec.DisplayName, def.DisplayName);
            Assert.Equal(spec.Players, def.AttendiesRequired);
            Assert.Equal(spec.Hours, def.DurationInHours);
            Assert.NotNull(def.DateTime);
            Assert.Equal(4, def.AttendyPerRole.Count);
            Assert.Equal(spec.Players, def.AttendyPerRole.Values.Sum());
            Assert.Equal(spec.Tank, def.AttendyPerRole["Tank"]);
            Assert.Equal(spec.Heal, def.AttendyPerRole["Heal"]);
            Assert.Equal(spec.Melee, def.AttendyPerRole["Melee-DPS"]);
            Assert.Equal(spec.Range, def.AttendyPerRole["Range-DPS"]);
            Assert.All(def.AttendyPerRole.Values, count => Assert.True(count > 0));
        }
    }

    [Fact]
    public void MoltenCore20_HasExpectedContent_AndLookupIsCaseInsensitive()
    {
        var def = ShippedRepo().Load("moltencore20");
        Assert.Equal("MoltenCore20", def.Name);
        Assert.Equal(20, def.AttendiesRequired);
        Assert.Equal(20, def.AttendyPerRole.Values.Sum());
        Assert.Equal(2, def.AttendyPerRole["Tank"]);
        Assert.True(def.DurationInHours > 0);
        Assert.NotNull(def.DateTime);
    }

    [Fact]
    public void UnknownEvent_ListsAvailableFiles()
    {
        var ex = Assert.Throws<ConfigurationException>(() => ShippedRepo().Load("Nope"));
        Assert.Contains("MoltenCore20", ex.Message);
        Assert.Contains("Nope.json", ex.Message);
    }

    [Fact]
    public void ListNames_ReturnsFileNamesForAutocomplete()
    {
        Assert.Equal(ShippedTemplates.Select(t => t.Name), ShippedRepo().ListNames());
    }

    [Fact]
    public void Validator_ReportsAllProblems()
    {
        var def = new EventDefinition
        {
            Name = "Other",
            DisplayName = "",
            AttendiesRequired = 10,
            DurationInHours = 0,
            AttendyPerRole = new() { ["Tank"] = 2, ["Wizard"] = 3, ["Heal"] = -1 },
        };
        var errors = EventDefinitionValidator.Validate(def, "File", TestSupport.Roles);

        Assert.Contains(errors, e => e.Contains("must equal the file name 'File'"));
        Assert.Contains(errors, e => e.Contains("displayName"));
        Assert.Contains(errors, e => e.Contains("durationInHours"));
        Assert.Contains(errors, e => e.Contains("'Wizard'") && e.Contains("does not exist"));
        Assert.Contains(errors, e => e.Contains("'Heal'") && e.Contains("positive"));
        Assert.Contains(errors, e => e.Contains("add up to 5") && e.Contains("10"));
    }

    [Fact]
    public void Validator_RejectsFixedRolesInAttendyPerRole_AndNonPositiveRequired()
    {
        var def = new EventDefinition
        {
            Name = "X", DisplayName = "X", AttendiesRequired = 0, DurationInHours = 1,
            AttendyPerRole = new() { ["Bench"] = 1 },
        };
        var errors = EventDefinitionValidator.Validate(def, "X", TestSupport.Roles);
        Assert.Contains(errors, e => e.Contains("attendiesRequired"));
        Assert.Contains(errors, e => e.Contains("'Bench'"));
    }

    [Fact]
    public void LoadFile_WithBrokenJson_GivesClearError()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "Bad.json");
        File.WriteAllText(path, "{ nope");
        var ex = Assert.Throws<ConfigurationException>(() => EventDefinitionRepository.LoadFile(path, "Bad", TestSupport.Roles));
        Assert.Contains("Bad.json: invalid JSON", ex.Message);
    }
}
