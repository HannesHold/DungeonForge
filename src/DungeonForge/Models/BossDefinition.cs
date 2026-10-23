namespace DungeonForge.Models;

/// <summary>
/// One boss on a raid template, in encounter order. <see cref="Points"/> is an optional per-boss override;
/// when it is null the global <c>pointsPerBossKill</c> setting is used.
/// </summary>
public sealed class BossDefinition
{
    public string Name { get; set; } = "";

    /// <summary>Optional override of the global points per kill. Null uses <c>pointsPerBossKill</c>.</summary>
    public int? Points { get; set; }
}
