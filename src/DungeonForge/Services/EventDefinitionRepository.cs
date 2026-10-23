using System.Text.Json;
using System.Text.Json.Nodes;
using DungeonForge.Configuration;
using DungeonForge.Models;

namespace DungeonForge.Services;

/// <summary>Reads and validates dungeon/raid templates from the EventConfigurations directory and its subfolders.</summary>
public sealed class EventDefinitionRepository
{
    private readonly BotConfiguration _config;

    public EventDefinitionRepository(BotConfiguration config) => _config = config;

    public string Directory => _config.EventConfigurationsDirectory;

    /// <summary>Names (file names without ".json") of all event files, sorted. Duplicate names are listed once.</summary>
    public IReadOnlyList<string> ListNames() =>
        EnumerateFiles()
            .Select(f => f.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Loads and validates one event file. Throws <see cref="ConfigurationException"/> with clear messages.</summary>
    public EventDefinition Load(string name)
    {
        var matches = EnumerateFiles()
            .Where(f => f.Name.Equals(name?.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count > 1)
            throw new ConfigurationException(DuplicateError(matches));
        if (matches.Count == 0)
        {
            var available = ListNames();
            throw new ConfigurationException(
                $"Unknown event '{name}': no file '{name}.json' in EventConfigurations or its subfolders. " +
                (available.Count == 0 ? "There are no event files." : $"Available: {string.Join(", ", available)}."));
        }

        var file = matches[0];
        return LoadFile(file.Path, file.Name, _config.SubscriptionRoles, ParentFolder(file.Path));
    }

    /// <summary>Loads every event file; invalid ones are reported in <c>errors</c> instead of throwing.</summary>
    public (IReadOnlyList<EventDefinition> Valid, IReadOnlyList<string> Errors) LoadAll()
    {
        var valid = new List<EventDefinition>();
        var errors = new List<string>();
        foreach (var group in EnumerateFiles().GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            var files = group.ToList();
            if (files.Count > 1)
            {
                errors.Add(DuplicateError(files));
                continue;
            }

            var file = files[0];
            try
            {
                valid.Add(LoadFile(file.Path, file.Name, _config.SubscriptionRoles, ParentFolder(file.Path)));
            }
            catch (ConfigurationException ex)
            {
                errors.AddRange(ex.Errors);
            }
        }

        return (valid, errors);
    }

    public static EventDefinition LoadFile(
        string path,
        string expectedName,
        IReadOnlyList<RoleDefinition> subscriptionRoles,
        string? parentFolder = null)
    {
        var file = Path.GetFileName(path);
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException ex)
        {
            throw new ConfigurationException($"{file}: invalid JSON ({ex.Message})");
        }

        if (node is null)
            throw new ConfigurationException($"{file}: file is empty.");
        if (node is not JsonObject obj)
            throw new ConfigurationException($"{file}: invalid JSON (expected an object).");

        var typeError = ReadType(obj, out var type);
        var bossParseErrors = ReadBosses(obj, out var bosses);

        EventDefinition? def;
        try
        {
            def = obj.Deserialize<EventDefinition>(JsonDefaults.ConfigFiles);
        }
        catch (JsonException ex)
        {
            throw new ConfigurationException($"{file}: invalid JSON ({ex.Message})");
        }

        if (def is null)
            throw new ConfigurationException($"{file}: file is empty.");

        def.Type = type;
        def.Bosses = bosses;

        var errors = EventDefinitionValidator.Validate(def, expectedName, subscriptionRoles, parentFolder, typeError, bossParseErrors)
            .Select(e => $"{file}: {e}")
            .ToList();
        if (errors.Count > 0)
            throw new ConfigurationException(errors);
        return def;
    }

    /// <summary>
    /// Removes <c>type</c> so an unknown value does not fail JSON parsing, and returns a validation message when
    /// the value is present but not Raid or Dungeon. A missing property returns null (the validator reports it).
    /// </summary>
    private static string? ReadType(JsonObject obj, out EventType? type)
    {
        type = null;
        var keys = new List<string>();
        foreach (var property in obj)
        {
            if (property.Key.Equals("type", StringComparison.OrdinalIgnoreCase))
                keys.Add(property.Key);
        }

        if (keys.Count == 0)
            return null;

        var node = obj[keys[0]];
        foreach (var key in keys)
            obj.Remove(key);

        if (node is null)
            return "\"type\" is missing.";

        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            if (string.IsNullOrWhiteSpace(text))
                return "\"type\" is missing.";
            if (!EventTypeJsonConverter.TryParse(text, out var parsed))
                return $"\"type\" is '{text.Trim()}' but must be \"Raid\" or \"Dungeon\".";
            type = parsed;
            return null;
        }

        return $"\"type\" is '{node.ToJsonString()}' but must be \"Raid\" or \"Dungeon\".";
    }

    /// <summary>
    /// Removes <c>bosses</c> so a non-integer <c>points</c> value becomes a validation error instead of a JSON failure.
    /// A missing property leaves <paramref name="bosses"/> null (absent).
    /// </summary>
    private static List<string> ReadBosses(JsonObject obj, out List<BossDefinition>? bosses)
    {
        bosses = null;
        var keys = new List<string>();
        foreach (var property in obj)
        {
            if (property.Key.Equals("bosses", StringComparison.OrdinalIgnoreCase))
                keys.Add(property.Key);
        }

        if (keys.Count == 0)
            return new List<string>();

        var node = obj[keys[0]];
        foreach (var key in keys)
            obj.Remove(key);

        if (keys.Count > 1)
            return new List<string> { "\"bosses\" is specified more than once." };

        if (node is null)
            return new List<string>();

        if (node is not JsonArray array)
            return new List<string> { "\"bosses\" must be an array of objects with a \"name\" and an optional integer \"points\"." };

        var list = new List<BossDefinition>();
        var errors = new List<string>();
        var index = 0;
        foreach (var item in array)
        {
            index++;
            if (item is not JsonObject bossObj)
            {
                errors.Add($"\"bosses\" entry #{index} must be an object with a \"name\".");
                continue;
            }

            var nameNode = Take(bossObj, "name");
            var pointsNode = Take(bossObj, "points");
            if (nameNode is not JsonValue nameValue || !nameValue.TryGetValue<string>(out var name) || string.IsNullOrWhiteSpace(name))
            {
                errors.Add($"\"bosses\" entry #{index} has no \"name\".");
                continue;
            }

            name = name.Trim();
            int? points = null;
            if (pointsNode is not null)
            {
                if (pointsNode is JsonValue pointsValue && pointsValue.TryGetValue<int>(out var parsed))
                    points = parsed;
                else
                    errors.Add($"boss '{name}' has \"points\" that must be an integer greater than 0.");
            }

            list.Add(new BossDefinition { Name = name, Points = points });
        }

        bosses = list;
        return errors;
    }

    private static JsonNode? Take(JsonObject obj, string name)
    {
        string? found = null;
        foreach (var property in obj)
        {
            if (property.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                found = property.Key;
                break;
            }
        }

        if (found is null)
            return null;

        var node = obj[found];
        obj.Remove(found);
        return node;
    }

    private List<TemplateFile> EnumerateFiles()
    {
        if (!System.IO.Directory.Exists(Directory))
            return new List<TemplateFile>();

        return System.IO.Directory.EnumerateFiles(Directory, "*.json", SearchOption.AllDirectories)
            .Select(path => new TemplateFile(Path.GetFileNameWithoutExtension(path) ?? "", path))
            .Where(file => file.Name.Length > 0)
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string DuplicateError(IReadOnlyList<TemplateFile> files)
    {
        var ordered = files.OrderBy(file => Relative(file.Path), StringComparer.OrdinalIgnoreCase).ToList();
        var paths = ordered.Select(file => Relative(file.Path)).ToList();
        var listed = paths.Count == 1
            ? paths[0]
            : string.Join(", ", paths.Take(paths.Count - 1)) + " and " + paths[^1];
        return $"Duplicate event name '{ordered[0].Name}': {listed}.";
    }

    private string Relative(string path) =>
        Path.GetRelativePath(Directory, path).Replace('\\', '/');

    private static string ParentFolder(string path)
    {
        var directory = Path.GetDirectoryName(path);
        return string.IsNullOrEmpty(directory) ? "" : Path.GetFileName(directory);
    }

    private readonly record struct TemplateFile(string Name, string Path);
}

public static class EventDefinitionValidator
{
    /// <summary>Returns a list of human readable problems (empty when valid).</summary>
    public static List<string> Validate(
        EventDefinition def,
        string expectedName,
        IReadOnlyList<RoleDefinition> subscriptionRoles,
        string? parentFolder = null,
        string? typeError = null,
        IReadOnlyList<string>? bossParseErrors = null)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(def.Name))
            errors.Add("\"name\" is missing.");
        else if (!def.Name.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
            errors.Add($"\"name\" is '{def.Name}' but must equal the file name '{expectedName}'.");

        if (typeError is not null)
            errors.Add(typeError);
        else if (def.Type is null)
            errors.Add("\"type\" is missing.");
        else if (ExpectedFolderType(parentFolder) is EventType expected && def.Type != expected)
            errors.Add($"\"type\" is '{def.Type}' but the file is in the {parentFolder} folder, so it must be \"{expected}\".");

        if (string.IsNullOrWhiteSpace(def.DisplayName))
            errors.Add("\"displayName\" is missing.");

        if (def.AttendiesRequired <= 0)
            errors.Add($"\"attendiesRequired\" must be a positive number (is {def.AttendiesRequired}).");

        if (!(def.DurationInHours > 0) || double.IsInfinity(def.DurationInHours))
            errors.Add($"\"durationInHours\" must be a positive number (is {def.DurationInHours}).");

        ValidateBosses(def, errors, bossParseErrors);

        if (def.AttendyPerRole is null || def.AttendyPerRole.Count == 0)
        {
            errors.Add("\"attendyPerRole\" is missing or empty.");
            return errors;
        }

        var known = subscriptionRoles.Select(r => r.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sum = 0;
        foreach (var (key, count) in def.AttendyPerRole)
        {
            if (!known.Contains(key))
                errors.Add($"role '{key}' in \"attendyPerRole\" does not exist in subscription-roles.json (known roles: {string.Join(", ", known)}).");
            if (!seen.Add(key))
                errors.Add($"role '{key}' appears more than once in \"attendyPerRole\".");
            if (count <= 0)
                errors.Add($"role '{key}' in \"attendyPerRole\" must have a positive count (is {count}).");
            else
                sum += count;
        }

        if (def.AttendiesRequired > 0 && sum != def.AttendiesRequired)
            errors.Add($"the role counts in \"attendyPerRole\" add up to {sum}, but \"attendiesRequired\" is {def.AttendiesRequired}.");

        return errors;
    }

    private static void ValidateBosses(EventDefinition def, List<string> errors, IReadOnlyList<string>? bossParseErrors)
    {
        if (bossParseErrors is not null)
            errors.AddRange(bossParseErrors);

        var bosses = def.Bosses;
        var absentOrEmpty = bosses is null || bosses.Count == 0;
        var parsedCleanly = bossParseErrors is null || bossParseErrors.Count == 0;

        if (def.Type == EventType.Dungeon && !absentOrEmpty)
            errors.Add("\"bosses\" must be absent or empty for a Dungeon.");
        else if (def.Type == EventType.Raid && absentOrEmpty && parsedCleanly)
            errors.Add("\"bosses\" is required for a Raid and must list at least one boss.");

        if (bosses is null)
            return;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var boss in bosses)
        {
            if (string.IsNullOrWhiteSpace(boss.Name))
            {
                errors.Add("a boss in \"bosses\" has no \"name\".");
                continue;
            }

            var name = boss.Name.Trim();
            if (!seen.Add(name))
                errors.Add($"boss '{name}' appears more than once in \"bosses\".");
            if (boss.Points is int points && points <= 0)
                errors.Add($"boss '{name}' has \"points\" {points} but it must be an integer greater than 0.");
        }
    }

    /// <summary>Raid when the file lives in a Raids folder, Dungeon for Dungeons, otherwise no folder rule.</summary>
    private static EventType? ExpectedFolderType(string? folder)
    {
        if (string.IsNullOrEmpty(folder))
            return null;
        if (folder.Equals("Raids", StringComparison.OrdinalIgnoreCase))
            return EventType.Raid;
        if (folder.Equals("Dungeons", StringComparison.OrdinalIgnoreCase))
            return EventType.Dungeon;
        return null;
    }
}
