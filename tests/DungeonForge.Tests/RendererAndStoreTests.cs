using DungeonForge.Configuration;
using DungeonForge.Models;
using DungeonForge.Services;

namespace DungeonForge.Tests;

public class RendererAndStoreTests
{
    private static readonly DateTimeOffset T0 = TestSupport.Now;

    [Fact]
    public void Embed_ShowsAllRequiredInformation()
    {
        var e = TestSupport.Event40();
        for (ulong u = 1; u <= 2; u++) SignupLogic.Apply(e, u, "Tank", T0);
        SignupLogic.Apply(e, 10, "Heal", T0);
        SignupLogic.Apply(e, 20, "Bench", T0);
        SignupLogic.Apply(e, 30, "Cant-attend", T0);

        var embed = new EventRenderer(TestSupport.Config()).BuildEmbed(e);

        Assert.Equal("Molten Core", embed.Title);
        var unix = e.StartTime.ToUnixTimeSeconds();
        Assert.Contains($"<t:{unix}:F>", embed.Description);
        Assert.Contains($"<t:{unix}:R>", embed.Description);
        Assert.Contains("24.10.2026 18:00 (Europe/Vienna)", embed.Description);
        Assert.Contains("Duration: 4 h", embed.Description);
        Assert.Contains("**3/40**", embed.Description);   // Bench / Cant attend are not counted

        var fields = embed.Fields.ToDictionary(f => f.Name, f => f.Value);
        Assert.Equal(6, fields.Count);
        Assert.Equal("<@1>\n<@2>", fields["🛡️ Tank (2/4)"]);
        Assert.Equal("<@10>", fields["💚 Heal (1/9)"]);
        Assert.Equal("—", fields["⚔️ Melee-DPS (0/13)"]);
        Assert.Equal("—", fields["🏹 Range-DPS (0/14)"]);
        Assert.Equal("<@20>", fields["🪑 Bench (1)"]);
        Assert.Equal("<@30>", fields["❌ Cant attend (1)"]);
    }

    [Fact]
    public void Embed_RendersFractionalDurationAsHours()
    {
        var e = TestSupport.Event40();
        e.DurationInHours = 1.5;
        var embed = new EventRenderer(TestSupport.Config()).BuildEmbed(e);
        Assert.Contains("Duration: 1.5 h", embed.Description);
        Assert.DoesNotContain("1.50", embed.Description);
    }

    [Fact]
    public void Embed_ShowsFullAndCancelledState()
    {
        var e = TestSupport.Event40();
        for (ulong u = 1; u <= 40; u++) SignupLogic.Apply(e, u, "Range-DPS", T0);
        var renderer = new EventRenderer(TestSupport.Config());

        Assert.Contains("**40/40** ✅ full", renderer.BuildEmbed(e).Description);
        var cancelled = renderer.BuildEmbed(e, cancelled: true);
        Assert.Contains("cancelled", cancelled.Description);
        Assert.DoesNotContain("✅ full", cancelled.Description);
    }

    [Fact]
    public void Embed_RespectsDiscordFieldLimits()
    {
        var e = TestSupport.Event40();
        e.AttendiesRequired = 200;
        e.AttendyPerRole = new(StringComparer.OrdinalIgnoreCase) { ["Tank"] = 200 };
        for (ulong u = 0; u < 200; u++) SignupLogic.Apply(e, 100000000000000000UL + u, "Tank", T0.AddSeconds(u));

        var embed = new EventRenderer(TestSupport.Config()).BuildEmbed(e);
        Assert.All(embed.Fields, f => Assert.True(f.Value.Length <= 1024));
        Assert.Contains("more", embed.Fields[0].Value);
    }

    [Fact]
    public void RolesForEvent_OnlyOffersRolesWithRequiredSlots_PlusFixedRoles()
    {
        var e = TestSupport.Event40();
        e.AttendyPerRole = new(StringComparer.OrdinalIgnoreCase) { ["Tank"] = 1, ["Heal"] = 1 };
        var keys = new EventRenderer(TestSupport.Config()).RolesForEvent(e).Select(r => r.Key);
        Assert.Equal(new[] { "Tank", "Heal", "Bench", "Cant-attend" }, keys);
    }

    [Fact]
    public void ThreadName_UsesDisplayNameAndLocalTime_AndFitsDiscordLimit()
    {
        var renderer = new EventRenderer(TestSupport.Config());
        var e = TestSupport.Event40();
        Assert.Equal("Molten Core – 24.10.2026 18:00", renderer.ThreadName(e));

        e.DisplayName = new string('x', 300);
        Assert.Equal(100, renderer.ThreadName(e).Length);
    }

    [Fact]
    public async Task Store_PersistsAndReloadsEventsIncludingSignups()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "Data", "events.json");
        var store = new EventStore(path);
        var e = TestSupport.Event40();
        e.ChannelId = 5; e.ThreadId = 777; e.MessageId = 9_000_000_000_000_000_001UL;
        SignupLogic.Apply(e, 18446744073709551000UL, "Tank", T0);
        store.Add(e);
        await store.SaveAsync();

        var reloaded = new EventStore(path);
        await reloaded.LoadAsync();

        var r = Assert.Single(reloaded.Events);
        Assert.Same(r, reloaded.FindByMessageId(9_000_000_000_000_000_001UL));
        Assert.Equal(777UL, r.ThreadId);
        Assert.Equal(e.StartTime, r.StartTime);
        Assert.Equal(40, r.AttendiesRequired);
        Assert.Equal(4, r.AttendyPerRole["Tank"]);
        var s = Assert.Single(r.Signups);
        Assert.Equal(18446744073709551000UL, s.UserId);
        Assert.Equal("Tank", s.RoleKey);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task Store_WithCorruptFile_StartsEmptyAndKeepsBackup()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "events.json");
        await File.WriteAllTextAsync(path, "{ not json");
        var store = new EventStore(path);
        await store.LoadAsync();
        Assert.Empty(store.Events);
        Assert.Single(Directory.GetFiles(dir, "events.json.corrupt-*"));
    }
}
