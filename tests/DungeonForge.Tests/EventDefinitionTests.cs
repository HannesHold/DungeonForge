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
        ("BlackfathomDeeps5", "Blackfathom Deeps", 5, 1.5, 1, 1, 1, 2),
        ("BlackrockDepths5", "Blackrock Depths", 5, 3, 1, 1, 1, 2),
        ("BlackrockSpire10", "Blackrock Spire", 10, 3, 2, 2, 3, 3),
        ("BlackwingLair40", "Blackwing Lair", 40, 4, 4, 9, 13, 14),
        ("CityOfDalaran5", "City of Dalaran", 5, 1.5, 1, 1, 1, 2),
        ("Deadmines5", "Deadmines", 5, 1, 1, 1, 1, 2),
        ("DireMaul5", "Dire Maul", 5, 2, 1, 1, 1, 2),
        ("ExcavationSiteWetlands5", "Excavation Site: Wetlands", 5, 1, 1, 1, 1, 2),
        ("Gnomeregan5", "Gnomeregan", 5, 1.5, 1, 1, 1, 2),
        ("HallOfThanes5", "The Hall of Thanes", 5, 1, 1, 1, 1, 2),
        ("Maraudon5", "Maraudon", 5, 2, 1, 1, 1, 2),
        ("MoltenCore40", "Molten Core", 40, 4, 4, 9, 13, 14),
        ("Naxxramas40", "Naxxramas", 40, 6, 4, 9, 13, 14),
        ("OnyxiasLair40", "Onyxia's Lair", 40, 2, 4, 9, 13, 14),
        ("RagefireChasm5", "Ragefire Chasm", 5, 1, 1, 1, 1, 2),
        ("RazorfenDowns5", "Razorfen Downs", 5, 1.5, 1, 1, 1, 2),
        ("RazorfenKraul5", "Razorfen Kraul", 5, 1.5, 1, 1, 1, 2),
        ("RuinsOfAhnQiraj20", "Ruins of Ahn'Qiraj", 20, 3, 2, 4, 7, 7),
        ("RuinsOfLordaeron5", "Ruins of Lordaeron", 5, 1, 1, 1, 1, 2),
        ("ScarletMonastery5", "Scarlet Monastery", 5, 2, 1, 1, 1, 2),
        ("Scholomance5", "Scholomance", 5, 2, 1, 1, 1, 2),
        ("ShadowfangKeep5", "Shadowfang Keep", 5, 1.5, 1, 1, 1, 2),
        ("StormwindStockade5", "Stormwind Stockade", 5, 1, 1, 1, 1, 2),
        ("Stratholme5", "Stratholme", 5, 2, 1, 1, 1, 2),
        ("SunkenTemple5", "Sunken Temple", 5, 2, 1, 1, 1, 2),
        ("Uldaman5", "Uldaman", 5, 1.5, 1, 1, 1, 2),
        ("WailingCaverns5", "Wailing Caverns", 5, 1.5, 1, 1, 1, 2),
        ("ZulFarrak5", "Zul'Farrak", 5, 1.5, 1, 1, 1, 2),
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
        Assert.Equal(30, valid.Count);
        Assert.Equal(22, valid.Count(v => v.AttendiesRequired == 5));
        Assert.Equal(1, valid.Count(v => v.AttendiesRequired == 10));
        Assert.Equal(2, valid.Count(v => v.AttendiesRequired == 20));
        Assert.Equal(5, valid.Count(v => v.AttendiesRequired == 40));
        Assert.Contains(valid, v => v.DurationInHours == 1.5);

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
    public void MoltenCore40_HasExpectedContent_AndLookupIsCaseInsensitive()
    {
        var def = ShippedRepo().Load("moltencore40");
        Assert.Equal("MoltenCore40", def.Name);
        Assert.Equal("Molten Core", def.DisplayName);
        Assert.Equal(40, def.AttendiesRequired);
        Assert.Equal(40, def.AttendyPerRole.Values.Sum());
        Assert.Equal(4, def.AttendyPerRole["Tank"]);
        Assert.Equal(9, def.AttendyPerRole["Heal"]);
        Assert.Equal(13, def.AttendyPerRole["Melee-DPS"]);
        Assert.Equal(14, def.AttendyPerRole["Range-DPS"]);
        Assert.Equal(4, def.DurationInHours);
        Assert.NotNull(def.DateTime);
    }

    [Fact]
    public void UnknownEvent_ListsAvailableFiles()
    {
        var ex = Assert.Throws<ConfigurationException>(() => ShippedRepo().Load("Nope"));
        Assert.Contains("MoltenCore40", ex.Message);
        Assert.Contains("Nope.json", ex.Message);
    }

    [Fact]
    public void ShippedTemplates_AreCopiedToBuildOutput()
    {
        var csproj = File.ReadAllText(Path.Combine(TestSupport.ProjectDir, "DungeonForge.csproj"));
        Assert.Contains(@"EventConfigurations\**\*.json", csproj);
        Assert.Contains("CopyToOutputDirectory", csproj);

        var sourceNames = Directory.GetFiles(Path.Combine(TestSupport.ProjectDir, "EventConfigurations"), "*.json")
            .Select(Path.GetFileName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(ShippedTemplates.Select(t => t.Name + ".json"), sourceNames);

        var outputDirs = Directory.GetDirectories(
            Path.Combine(TestSupport.ProjectDir, "bin"), "net10.0", SearchOption.AllDirectories);
        Assert.NotEmpty(outputDirs);
        Assert.Contains(outputDirs, dir =>
        {
            var copied = Path.Combine(dir, "EventConfigurations");
            if (!Directory.Exists(copied)) return false;
            var names = Directory.GetFiles(copied, "*.json")
                .Select(Path.GetFileName)
                .ToHashSet(StringComparer.Ordinal);
            return sourceNames.All(names.Contains);
        });
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
