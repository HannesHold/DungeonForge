using System.Text.Json;
using DungeonForge.Configuration;
using DungeonForge.Models;

namespace DungeonForge.Services;

/// <summary>Reads and validates dungeon/raid templates from the EventConfigurations directory.</summary>
public sealed class EventDefinitionRepository
{
    private readonly BotConfiguration _config;

    public EventDefinitionRepository(BotConfiguration config) => _config = config;

    public string Directory => _config.EventConfigurationsDirectory;

    /// <summary>Names (file names without ".json") of all event files, sorted.</summary>
    public IReadOnlyList<string> ListNames()
    {
        if (!System.IO.Directory.Exists(Directory)) return Array.Empty<string>();
        return System.IO.Directory.EnumerateFiles(Directory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Loads and validates one event file. Throws <see cref="ConfigurationException"/> with clear messages.</summary>
    public EventDefinition Load(string name)
    {
        var fileName = ListNames().FirstOrDefault(n => n.Equals(name?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (fileName is null)
        {
            var available = ListNames();
            throw new ConfigurationException(
                $"Unknown event '{name}': no file '{name}.json' in EventConfigurations. " +
                (available.Count == 0 ? "There are no event files." : $"Available: {string.Join(", ", available)}."));
        }

        return LoadFile(Path.Combine(Directory, fileName + ".json"), fileName, _config.SubscriptionRoles);
    }

    /// <summary>Loads every event file; invalid ones are reported in <c>errors</c> instead of throwing.</summary>
    public (IReadOnlyList<EventDefinition> Valid, IReadOnlyList<string> Errors) LoadAll()
    {
        var valid = new List<EventDefinition>();
        var errors = new List<string>();
        foreach (var name in ListNames())
        {
            try { valid.Add(Load(name)); }
            catch (ConfigurationException ex) { errors.AddRange(ex.Errors); }
        }
        return (valid, errors);
    }

    public static EventDefinition LoadFile(string path, string expectedName, IReadOnlyList<RoleDefinition> subscriptionRoles)
    {
        var file = Path.GetFileName(path);
        EventDefinition? def;
        try
        {
            def = JsonSerializer.Deserialize<EventDefinition>(File.ReadAllText(path), JsonDefaults.ConfigFiles);
        }
        catch (JsonException ex)
        {
            throw new ConfigurationException($"{file}: invalid JSON ({ex.Message})");
        }

        if (def is null) throw new ConfigurationException($"{file}: file is empty.");

        var errors = EventDefinitionValidator.Validate(def, expectedName, subscriptionRoles)
            .Select(e => $"{file}: {e}")
            .ToList();
        if (errors.Count > 0) throw new ConfigurationException(errors);
        return def;
    }
}

public static class EventDefinitionValidator
{
    /// <summary>Returns a list of human readable problems (empty when valid).</summary>
    public static List<string> Validate(EventDefinition def, string expectedName, IReadOnlyList<RoleDefinition> subscriptionRoles)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(def.Name))
            errors.Add("\"name\" is missing.");
        else if (!def.Name.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
            errors.Add($"\"name\" is '{def.Name}' but must equal the file name '{expectedName}'.");

        if (string.IsNullOrWhiteSpace(def.DisplayName))
            errors.Add("\"displayName\" is missing.");

        if (def.AttendiesRequired <= 0)
            errors.Add($"\"attendiesRequired\" must be a positive number (is {def.AttendiesRequired}).");

        if (!(def.DurationInHours > 0) || double.IsInfinity(def.DurationInHours))
            errors.Add($"\"durationInHours\" must be a positive number (is {def.DurationInHours}).");

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
}
