using DungeonForge.Models;
using DungeonForge.Services;

namespace DungeonForge.Tests;

public class SignupLogicTests
{
    private static readonly DateTimeOffset T0 = TestSupport.Now;

    [Fact]
    public void Apply_AddsAndMovesUser_OneRolePerUser()
    {
        var e = TestSupport.Event20();

        var first = SignupLogic.Apply(e, 1, "Tank", T0);
        Assert.True(first.Changed);
        Assert.Null(first.PreviousRoleKey);

        var same = SignupLogic.Apply(e, 1, "tank", T0);
        Assert.False(same.Changed);

        var move = SignupLogic.Apply(e, 1, "Heal", T0.AddMinutes(1));
        Assert.True(move.Changed);
        Assert.Equal("Tank", move.PreviousRoleKey);

        Assert.Single(e.Signups);
        Assert.Equal(0, SignupLogic.Count(e, "Tank"));
        Assert.Equal(1, SignupLogic.Count(e, "Heal"));
    }

    [Fact]
    public void Remove_OnlyWorksForCurrentRole()
    {
        var e = TestSupport.Event20();
        SignupLogic.Apply(e, 1, "Tank", T0);
        SignupLogic.Apply(e, 1, "Heal", T0); // moved; the old Tank reaction is removed afterwards by the bot

        Assert.False(SignupLogic.Remove(e, 1, "Tank")); // stale removal event must be ignored
        Assert.Single(e.Signups);
        Assert.True(SignupLogic.Remove(e, 1, "Heal"));
        Assert.Empty(e.Signups);
        Assert.False(SignupLogic.Remove(e, 1, "Heal"));
    }

    [Fact]
    public void FixedRoles_DoNotCountTowardsAttendance()
    {
        var e = TestSupport.Event20();
        for (ulong u = 1; u <= 17; u++) SignupLogic.Apply(e, u, u % 2 == 0 ? "Melee-DPS" : "Range-DPS", T0);
        SignupLogic.Apply(e, 100, "Bench", T0);
        SignupLogic.Apply(e, 101, "Cant-attend", T0);

        Assert.Equal(17, SignupLogic.CountAttending(e));
        Assert.False(SignupLogic.IsFull(e));
        Assert.Equal(1, SignupLogic.Count(e, "Bench"));

        for (ulong u = 200; u < 203; u++) SignupLogic.Apply(e, u, "Heal", T0);
        Assert.Equal(20, SignupLogic.CountAttending(e));
        Assert.True(SignupLogic.IsFull(e));
    }

    private static IReadOnlyDictionary<string, IReadOnlyCollection<ulong>> Reactions(params (string Role, ulong[] Users)[] items) =>
        items.ToDictionary(i => i.Role, i => (IReadOnlyCollection<ulong>)i.Users);

    private static readonly string[] Order = { "Tank", "Heal", "Melee-DPS", "Range-DPS", "Bench", "Cant-attend" };

    [Fact]
    public void Reconcile_AppliesOfflineChanges()
    {
        var e = TestSupport.Event20();
        SignupLogic.Apply(e, 1, "Tank", T0);        // still reacted -> kept (timestamp kept)
        SignupLogic.Apply(e, 2, "Heal", T0);        // removed reaction while offline -> dropped
        SignupLogic.Apply(e, 3, "Melee-DPS", T0);   // reacted with another role while offline -> moved? (no: still has stored role too)

        var reactions = Reactions(
            ("Tank", new ulong[] { 1 }),
            ("Melee-DPS", new ulong[] { 3 }),
            ("Range-DPS", new ulong[] { 3, 4 }),     // 3 holds two reactions -> keep stored one, remove other
            ("Bench", new ulong[] { 5 }));           // 5 signed up while offline

        var plan = SignupLogic.Reconcile(e, reactions, Order, _ => true, T0.AddHours(1));

        Assert.Equal(new ulong[] { 1, 3, 4, 5 }, plan.Signups.Select(s => s.UserId).Order());
        Assert.Equal("Melee-DPS", plan.Signups.Single(s => s.UserId == 3).RoleKey);
        Assert.Equal("Range-DPS", plan.Signups.Single(s => s.UserId == 4).RoleKey);
        Assert.Equal("Bench", plan.Signups.Single(s => s.UserId == 5).RoleKey);
        Assert.Equal(T0, plan.Signups.Single(s => s.UserId == 1).SignedUpAt);
        Assert.Equal(new[] { new SignupLogic.ReactionRemoval(3, "Range-DPS") }, plan.ReactionsToRemove);
    }

    [Fact]
    public void Reconcile_RemovesReactionsOfIneligibleUsers_AndPicksFirstRoleForMultiReactors()
    {
        var e = TestSupport.Event20();
        var reactions = Reactions(
            ("Tank", new ulong[] { 7, 8 }),
            ("Heal", new ulong[] { 8 }));

        var plan = SignupLogic.Reconcile(e, reactions, Order, u => u != 7, T0);

        Assert.Equal(new[] { 8UL }, plan.Signups.Select(s => s.UserId));
        Assert.Equal("Tank", plan.Signups[0].RoleKey);
        Assert.Contains(new SignupLogic.ReactionRemoval(7, "Tank"), plan.ReactionsToRemove);
        Assert.Contains(new SignupLogic.ReactionRemoval(8, "Heal"), plan.ReactionsToRemove);
        Assert.Equal(2, plan.ReactionsToRemove.Count);
    }
}
