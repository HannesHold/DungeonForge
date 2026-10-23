namespace DungeonForge.Bot;

/// <summary>
/// Marks a slash command (or a whole command module) as requiring the df-manager role.
/// <see cref="CommandHelpFormatter"/> reads this so the help text stays in step with the commands.
/// The command methods still enforce the channel and role themselves.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class ManagerOnlyAttribute : Attribute
{
}
