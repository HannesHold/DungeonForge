using System.Text.Json;
using DungeonForge.Configuration;
using DungeonForge.Models;
using DungeonForge.Services;

namespace DungeonForge.Tests;

public class EventDefinitionTests
{
    /// <summary>Raid template names. Every other shipped file is a dungeon.</summary>
    private static readonly HashSet<string> RaidNames = new(StringComparer.Ordinal)
    {
        "AhnQirajTemple40",
        "BlackwingLair40",
        "MoltenCore40",
        "Naxxramas40",
        "OnyxiasLair40",
        "RuinsOfAhnQiraj20",
        "ZulGurub20",
    };

    /// <summary>Every file shipped in EventConfigurations, in file-name (ordinal, case-insensitive) order.</summary>
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

        var eventsDir = Path.Combine(TestSupport.ProjectDir, "EventConfigurations");
        var onDisk = Directory.GetFiles(eventsDir, "*.json", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(ShippedTemplates.Select(t => t.Name), onDisk);
        Assert.Equal(ShippedTemplates.Select(t => t.Name), valid.Select(v => v.Name));
        Assert.Equal(30, valid.Count);
        Assert.Empty(Directory.GetFiles(eventsDir, "*.json", SearchOption.TopDirectoryOnly));
        Assert.Equal(7, Directory.GetFiles(Path.Combine(eventsDir, "Raids"), "*.json").Length);
        Assert.Equal(23, Directory.GetFiles(Path.Combine(eventsDir, "Dungeons"), "*.json").Length);
        Assert.Equal(7, valid.Count(v => v.Type == EventType.Raid));
        Assert.Equal(23, valid.Count(v => v.Type == EventType.Dungeon));
        Assert.Equal(22, valid.Count(v => v.AttendiesRequired == 5));
        Assert.Equal(1, valid.Count(v => v.AttendiesRequired == 10));
        Assert.Equal(2, valid.Count(v => v.AttendiesRequired == 20));
        Assert.Equal(5, valid.Count(v => v.AttendiesRequired == 40));
        Assert.Contains(valid, v => v.DurationInHours == 1.5);

        foreach (var spec in ShippedTemplates)
        {
            var def = valid.Single(v => v.Name == spec.Name);
            var path = Directory.GetFiles(eventsDir, spec.Name + ".json", SearchOption.AllDirectories).Single();
            var folder = Path.GetFileName(Path.GetDirectoryName(path));
            var expectedType = RaidNames.Contains(spec.Name) ? EventType.Raid : EventType.Dungeon;
            Assert.Equal(expectedType == EventType.Raid ? "Raids" : "Dungeons", folder);
            Assert.Equal(expectedType, def.Type);
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
        Assert.Equal(EventType.Raid, def.Type);
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

        var eventsDir = Path.Combine(TestSupport.ProjectDir, "EventConfigurations");
        var sourceNames = Directory.GetFiles(eventsDir, "*.json", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(ShippedTemplates.Select(t => t.Name + ".json"), sourceNames);
        var sourceRelative = Directory.GetFiles(eventsDir, "*.json", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(eventsDir, path).Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);

        var outputDirs = Directory.GetDirectories(
            Path.Combine(TestSupport.ProjectDir, "bin"), "net10.0", SearchOption.AllDirectories);
        Assert.NotEmpty(outputDirs);
        Assert.Contains(outputDirs, dir =>
        {
            var copied = Path.Combine(dir, "EventConfigurations");
            var raids = Path.Combine(copied, "Raids");
            var dungeons = Path.Combine(copied, "Dungeons");
            if (!Directory.Exists(raids) || !Directory.Exists(dungeons)) return false;
            var relative = Directory.GetFiles(copied, "*.json", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(copied, path).Replace('\\', '/'))
                .ToHashSet(StringComparer.Ordinal);
            return sourceRelative.All(relative.Contains)
                && Directory.GetFiles(raids, "*.json").Length == 7
                && Directory.GetFiles(dungeons, "*.json").Length == 23;
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
            Type = EventType.Raid,
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
            Name = "X", Type = EventType.Dungeon, DisplayName = "X", AttendiesRequired = 0, DurationInHours = 1,
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

    [Fact]
    public void Type_JsonConverter_IsCaseInsensitive_AndRejectsUnknownOrNumericValues()
    {
        var raid = JsonSerializer.Deserialize<EventDefinition>("""{"type":"raid"}""", JsonDefaults.ConfigFiles);
        Assert.Equal(EventType.Raid, raid!.Type);
        var dungeon = JsonSerializer.Deserialize<EventDefinition>("""{"type":"DUNGEON"}""", JsonDefaults.ConfigFiles);
        Assert.Equal(EventType.Dungeon, dungeon!.Type);
        Assert.Null(JsonSerializer.Deserialize<EventDefinition>("""{"name":"X"}""", JsonDefaults.ConfigFiles)!.Type);

        var number = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<EventDefinition>("""{"type":1}""", JsonDefaults.ConfigFiles));
        Assert.Contains("type", number.Message);
        var unknown = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<EventDefinition>("""{"type":"Battleground"}""", JsonDefaults.ConfigFiles));
        Assert.Contains("Battleground", unknown.Message);
    }

    [Fact]
    public void LoadFile_MissingType_IsAValidationError()
    {
        var json = TemplateJson("Ok", "Raid").Replace("\"type\": \"Raid\",\n", "");
        var path = WriteJson("Ok.json", json);
        var ex = Assert.Throws<ConfigurationException>(() => EventDefinitionRepository.LoadFile(path, "Ok", TestSupport.Roles));
        Assert.Contains("\"type\" is missing", ex.Message);
        Assert.DoesNotContain("invalid JSON", ex.Message);
    }

    [Fact]
    public void LoadFile_UnknownType_IsAValidationError_AndOtherProblemsAreStillReported()
    {
        var path = WriteJson("Ok.json", TemplateJson("Ok", "Battleground", displayName: ""));
        var ex = Assert.Throws<ConfigurationException>(() => EventDefinitionRepository.LoadFile(path, "Ok", TestSupport.Roles));
        Assert.Contains("\"type\" is 'Battleground' but must be \"Raid\" or \"Dungeon\".", ex.Message);
        Assert.Contains("displayName", ex.Message);

        var numeric = WriteJson("Num.json", TemplateJson("Num", type: null).Replace("\"type\": null", "\"type\": 1"));
        var numericError = Assert.Throws<ConfigurationException>(() =>
            EventDefinitionRepository.LoadFile(numeric, "Num", TestSupport.Roles));
        Assert.Contains("\"type\" is '1'", numericError.Message);
    }

    [Fact]
    public void LoadFile_TypeIsCaseInsensitive_AndMustMatchRaidsOrDungeonsFolder()
    {
        var raid = WriteJson("Ok.json", TemplateJson("Ok", "raid"));
        var loaded = EventDefinitionRepository.LoadFile(raid, "Ok", TestSupport.Roles, "Raids");
        Assert.Equal(EventType.Raid, loaded.Type);

        var mismatch = WriteJson("Nope.json", TemplateJson("Nope", "Dungeon"));
        var ex = Assert.Throws<ConfigurationException>(() =>
            EventDefinitionRepository.LoadFile(mismatch, "Nope", TestSupport.Roles, "Raids"));
        Assert.Contains("\"type\" is 'Dungeon'", ex.Message);
        Assert.Contains("Raids", ex.Message);
        Assert.Contains("\"Raid\"", ex.Message);

        var root = EventDefinitionRepository.LoadFile(raid, "Ok", TestSupport.Roles, "EventConfigurations");
        Assert.Equal(EventType.Raid, root.Type);
    }

    [Fact]
    public void Repository_FindsTemplatesInSubfolders_AndRejectsDuplicateNames()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var events = Path.Combine(root, "EventConfigurations");
        WriteJson(Path.Combine(events, "Loose.json"), TemplateJson("Loose", "dungeon"));
        WriteJson(Path.Combine(events, "Dungeons", "Classic", "Deep.json"), TemplateJson("Deep", "Dungeon"));
        WriteJson(Path.Combine(events, "Raids", "Twin.json"), TemplateJson("Twin", "Raid"));
        WriteJson(Path.Combine(events, "Dungeons", "Twin.json"), TemplateJson("Twin", "Dungeon"));
        WriteJson(Path.Combine(events, "Raids", "Wrong.json"), TemplateJson("Wrong", "Dungeon"));

        var repo = RepoAt(root);
        Assert.Equal(EventType.Dungeon, repo.Load("loose").Type);
        Assert.Equal("Deep", repo.Load("Deep").Name);

        var duplicate = Assert.Throws<ConfigurationException>(() => repo.Load("Twin"));
        Assert.Contains("Duplicate event name 'Twin'", duplicate.Message);
        Assert.Contains("Dungeons/Twin.json", duplicate.Message);
        Assert.Contains("Raids/Twin.json", duplicate.Message);

        var (valid, errors) = repo.LoadAll();
        Assert.Contains(valid, template => template.Name == "Loose");
        Assert.Contains(valid, template => template.Name == "Deep");
        Assert.DoesNotContain(valid, template => template.Name == "Twin");
        Assert.Contains(errors, error => error.Contains("Duplicate event name 'Twin'")
            && error.Contains("Dungeons/Twin.json") && error.Contains("Raids/Twin.json"));

        var mismatch = Assert.Throws<ConfigurationException>(() => repo.Load("Wrong"));
        Assert.Contains("Raids", mismatch.Message);
        Assert.Contains("\"type\" is 'Dungeon'", mismatch.Message);
    }

    [Fact]
    public void Autocomplete_OrdersRaidsThenDungeons_FiltersWhatWasTyped_AndCapsAt25()
    {
        var templates = ShippedRepo().LoadAll().Valid;
        var all = EventTemplateAutocomplete.Suggest(templates, "");
        Assert.Equal(EventTemplateAutocomplete.DiscordChoiceLimit, all.Count);
        Assert.Equal("AhnQirajTemple40", all[0].Value);
        Assert.Equal("Ahn'Qiraj Temple (Raid, 40)", all[0].Label);
        Assert.All(all.Take(7), choice => Assert.Contains("(Raid, ", choice.Label));
        Assert.All(all.Skip(7), choice => Assert.Contains("(Dungeon, ", choice.Label));
        Assert.Equal("Blackfathom Deeps (Dungeon, 5)", all[7].Label);
        Assert.Equal("Stratholme5", all[^1].Value);
        Assert.DoesNotContain(all, choice => choice.Value == "SunkenTemple5");

        var raidLabels = all.Take(7).Select(choice => choice.Label).ToList();
        Assert.Equal(raidLabels.OrderBy(label => label, StringComparer.OrdinalIgnoreCase), raidLabels);

        var molten = Assert.Single(EventTemplateAutocomplete.Suggest(templates, "molten"));
        Assert.Equal(("Molten Core (Raid, 40)", "MoltenCore40"), molten);

        var sunken = Assert.Single(EventTemplateAutocomplete.Suggest(templates, "Sunken"));
        Assert.Equal(("Sunken Temple (Dungeon, 5)", "SunkenTemple5"), sunken);

        Assert.Equal(
            new[] { "BlackrockDepths5", "BlackrockSpire10" },
            EventTemplateAutocomplete.Suggest(templates, "blackrock").Select(choice => choice.Value));

        var raids = EventTemplateAutocomplete.Suggest(templates, "Raid");
        Assert.Equal(7, raids.Count);
        Assert.All(raids, choice => Assert.Contains("(Raid, ", choice.Label));
    }

    private static EventDefinitionRepository RepoAt(string root) =>
        new(new BotConfiguration(new GlobalConfig(), TestSupport.Roles, TestSupport.FixedRoles, TimeZoneInfo.Utc, root));

    private static string TemplateJson(string name, string? type, string? displayName = null)
    {
        displayName ??= name + " display";
        var typeJson = type is null ? "null" : $"\"{type}\"";
        return $$"""
            {
              "name": "{{name}}",
              "type": {{typeJson}},
              "displayName": "{{displayName}}",
              "attendiesRequired": 2,
              "attendyPerRole": { "Tank": 1, "Heal": 1 },
              "durationInHours": 2
            }
            """;
    }

    private static string WriteJson(string path, string json)
    {
        if (!Path.IsPathRooted(path))
            path = Path.Combine(Directory.CreateTempSubdirectory().FullName, path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        return path;
    }
}
