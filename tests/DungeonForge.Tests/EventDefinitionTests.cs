using DungeonForge.Configuration;
using DungeonForge.Models;
using DungeonForge.Services;

namespace DungeonForge.Tests;

public class EventDefinitionTests
{
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
        var (valid, errors) = ShippedRepo().LoadAll();
        Assert.Empty(errors);
        Assert.Equal(new[] { 5, 10, 20 }, valid.Select(v => v.AttendiesRequired).Order());
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
        Assert.Equal(new[] { "Dungeon10", "Dungeon5", "MoltenCore20" }, ShippedRepo().ListNames());
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
