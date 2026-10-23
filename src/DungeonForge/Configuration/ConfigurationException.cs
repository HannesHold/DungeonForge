namespace DungeonForge.Configuration;

/// <summary>Thrown when a configuration file is missing, malformed or invalid. Carries every problem found.</summary>
public sealed class ConfigurationException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public ConfigurationException(string error) : this(new[] { error }) { }

    public ConfigurationException(IEnumerable<string> errors)
        : base(string.Join(Environment.NewLine, errors))
    {
        Errors = errors.ToList();
    }
}
