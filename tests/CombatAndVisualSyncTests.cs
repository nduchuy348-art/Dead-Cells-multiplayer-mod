using DeadCellsMultiplayerMod.Ghost;
using DeadCellsMultiplayerMod.Tools;
using Xunit;

namespace DeadCellsMultiplayerMod.ProtocolReplay.Tests;

public sealed class CombatAndVisualSyncTests
{
    [Fact]
    public void RemoteAffectEvent_RetainsProperties()
    {

        var evt = new NetNode.RemoteAffectEvent(userId: 5, affectId: 42, sec: 3.5, isRemove: false);
        Assert.Equal(5, evt.UserId);
        Assert.Equal(42, evt.AffectId);
        Assert.Equal(3.5, evt.Sec);
        Assert.False(evt.IsRemove);

        var remEvt = new NetNode.RemoteAffectEvent(userId: 5, affectId: 42, sec: 0.0, isRemove: true);
        Assert.True(remEvt.IsRemove);
    }

    [Theory]
    [InlineData("punch", true)]
    [InlineData("punch_01", true)]
    [InlineData("kick", true)]
    [InlineData("slash", true)]
    [InlineData("fist_combo", true)]
    [InlineData("w_sword_atk1", true)]
    [InlineData("attack_heavy", true)]
    [InlineData("strike_down", true)]
    [InlineData("swing_left", true)]
    [InlineData("smash", true)]
    [InlineData("thrust", true)]
    [InlineData("stab", true)]
    [InlineData("cast_spell", true)]
    [InlineData("throw_grenade", true)]
    [InlineData("fire_arrow", true)]
    [InlineData("dive_slam", true)]
    [InlineData("hit_react", true)]
    [InlineData("idle", false)]
    [InlineData("run", false)]
    [InlineData("jump", false)]
    [InlineData("fall", false)]
    [InlineData("crouch", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void AttackAnimationVocabulary_RecognizesUnarmedAndWeaponAttacks(string? anim, bool expected)
    {
        var result = CombatSyncHelpers.IsAttackAnim(anim!);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("idle", true)]
    [InlineData("run", true)]
    [InlineData("walk", true)]
    [InlineData("jump", true)]
    [InlineData("fall", true)]
    [InlineData("climb", true)]
    [InlineData("ladder", true)]
    [InlineData("crouch", true)]
    [InlineData("land", false)]
    [InlineData("jumpLand", false)]
    [InlineData("fallLand", false)]
    [InlineData("hardLand", false)]
    [InlineData("slash", false)]
    [InlineData("atk", false)]
    [InlineData("guard", false)]
    [InlineData("defend", false)]
    [InlineData("shield", false)]
    [InlineData("parry", false)]
    [InlineData("block", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void RemoteAnimationLooping_ExcludesLandingAndAttacks(string? anim, bool expected)
    {
        var result = CombatSyncHelpers.ShouldLoopRemoteAnim(anim!);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("jump", "run", true)]
    [InlineData("jump", "idle", true)]
    [InlineData("fall", "run", true)]
    [InlineData("fall", "idle", true)]
    [InlineData("fall", "crouch", true)]
    [InlineData("fall", "walk", true)]
    [InlineData("fall", "remain", true)]
    [InlineData("jumpDown", "run", true)]
    [InlineData("any", "land", true)]
    [InlineData("any", "hardLand", true)]
    [InlineData("any", "jumpLand", true)]
    [InlineData("run", "run", false)]
    [InlineData("idle", "idle", false)]
    [InlineData("run", "jump", false)]
    [InlineData("jump", "fall", false)]
    [InlineData(null, "run", false)]
    [InlineData("jump", null, false)]
    [InlineData(null, null, false)]
    public void IsLandingTransition_DetectsAirborneToGroundedAndLandAnim(string? prev, string? next, bool expected)
    {
        var result = CombatSyncHelpers.IsLandingTransition(prev, next);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CalculateSpatializedVolume_SuppressesDistantSounds()
    {
        // Co-located: 100% volume
        var full = CombatSyncHelpers.CalculateSpatializedVolume(10, 10, 10, 10, 1.0, 26.0);
        Assert.NotNull(full);
        Assert.Equal(1.0, full.Value, 2);

        // Half distance (13 cases away): (1 - 13/26)^2 = 0.25
        var half = CombatSyncHelpers.CalculateSpatializedVolume(0, 0, 13, 0, 1.0, 26.0);
        Assert.NotNull(half);
        Assert.Equal(0.25, half.Value, 2);

        // Far away (30 cases away): completely suppressed (null)
        var distant = CombatSyncHelpers.CalculateSpatializedVolume(0, 0, 30, 0, 1.0, 26.0);
        Assert.Null(distant);

        // Exactly at boundary: suppressed
        var boundary = CombatSyncHelpers.CalculateSpatializedVolume(0, 0, 26, 0, 1.0, 26.0);
        Assert.Null(boundary);
    }

    [Fact]
    public void SmoothStepPosition_SnapsOnTeleportAndSmoothsOnStep()
    {
        // Normal small step (delta = 10 <= 32): smooths by 65%
        var smooth = CombatSyncHelpers.SmoothStepPosition(100.0, 110.0, 0.65, 120.0);
        Assert.Equal(106.5, smooth, 2);

        // Larger gap with lag (delta = 50 > 32): dynamic acceleration = min(0.90, 0.65 + 18/100) = 0.83
        var accelerated = CombatSyncHelpers.SmoothStepPosition(100.0, 150.0, 0.65, 120.0);
        Assert.Equal(141.5, accelerated, 2);

        // Very close (<0.25): snaps to target
        var close = CombatSyncHelpers.SmoothStepPosition(100.0, 100.1, 0.65, 120.0);
        Assert.Equal(100.1, close, 2);

        // Teleport (>120): snaps immediately to target
        var teleport = CombatSyncHelpers.SmoothStepPosition(100.0, 300.0, 0.65, 120.0);
        Assert.Equal(300.0, teleport, 2);

        // NaN/Infinity robustness
        Assert.Equal(200.0, CombatSyncHelpers.SmoothStepPosition(double.NaN, 200.0));
        Assert.Equal(100.0, CombatSyncHelpers.SmoothStepPosition(100.0, double.NaN));
    }

    [Fact]
    public void CalculateSpatializedVolume_HandlesEdgeCasesAndInvalidInputs()
    {
        // Zero or negative max distance
        Assert.Null(CombatSyncHelpers.CalculateSpatializedVolume(0, 0, 1, 1, 1.0, 0));
        Assert.Null(CombatSyncHelpers.CalculateSpatializedVolume(0, 0, 1, 1, 1.0, -10));

        // Negative, zero, or NaN volume
        Assert.Null(CombatSyncHelpers.CalculateSpatializedVolume(0, 0, 0, 0, -0.5));
        Assert.Null(CombatSyncHelpers.CalculateSpatializedVolume(0, 0, 0, 0, 0.0));
        Assert.Null(CombatSyncHelpers.CalculateSpatializedVolume(0, 0, 0, 0, double.NaN));

        // NaN coordinates
        Assert.Null(CombatSyncHelpers.CalculateSpatializedVolume(double.NaN, 0, 0, 0, 1.0));
    }
}