namespace DungeonForge.Models;

/// <summary>A selectable sign-up role (entry of subscription-roles.json or fixed-subscription-roles.json).</summary>
public sealed class RoleDefinition
{
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";

    /// <summary>Unicode emoji (e.g. "🛡️") or a custom emoji in Discord markup (e.g. "&lt;:tank:123456789012345678&gt;").</summary>
    public string Emoji { get; set; } = "";
}
