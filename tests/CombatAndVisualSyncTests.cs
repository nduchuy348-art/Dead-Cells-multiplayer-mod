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

    [Fact]
    public void ShieldRaiseSpatialization_MutesDistantShieldRaiseAndScalesNearby()
    {
        // 50 cases away (remote player far away): completely muted (null)
        var distantShield = CombatSyncHelpers.CalculateSpatializedVolume(0, 0, 50, 0, 1.0, CombatSyncHelpers.DefaultMaxSoundDistanceCases);
        Assert.Null(distantShield);

        // 30 cases away (beyond max distance of 26 cases): completely muted (null)
        var beyondMax = CombatSyncHelpers.CalculateSpatializedVolume(100, 200, 100, 230, 1.0, CombatSyncHelpers.DefaultMaxSoundDistanceCases);
        Assert.Null(beyondMax);

        // Right next to each other (co-located): full volume
        var adjacentShield = CombatSyncHelpers.CalculateSpatializedVolume(10, 10, 10, 10, 1.0, CombatSyncHelpers.DefaultMaxSoundDistanceCases);
        Assert.NotNull(adjacentShield);
        Assert.Equal(1.0, adjacentShield.Value, 2);

        // Intermediate distance (e.g. 13 cases): attenuated smoothly
        var midShield = CombatSyncHelpers.CalculateSpatializedVolume(0, 0, 13, 0, 1.0, CombatSyncHelpers.DefaultMaxSoundDistanceCases);
        Assert.NotNull(midShield);
        Assert.Equal(0.25, midShield.Value, 2);
    }

    [Fact]
    public void MobSmoothStepPosition_SmoothsWithMobInterpolationAlpha()
    {
        const double MobAlpha = 0.72;
        const double MobSnapDistance = 24.0 * 10.0; // 240 px
        const double MobCloseThreshold = 0.20;

        // Normal walk step (delta = 4 px): smooths by 72%
        var step = CombatSyncHelpers.SmoothStepPosition(100.0, 104.0, MobAlpha, MobSnapDistance, MobCloseThreshold);
        Assert.Equal(102.88, step, 2);

        // Sub-pixel jitter (< 0.20 px): snaps to target to eliminate jitter
        var micro = CombatSyncHelpers.SmoothStepPosition(100.0, 100.15, MobAlpha, MobSnapDistance, MobCloseThreshold);
        Assert.Equal(100.15, micro, 2);

        // Teleport / Warp (> 240 px): snaps directly to target
        var warp = CombatSyncHelpers.SmoothStepPosition(100.0, 400.0, MobAlpha, MobSnapDistance, MobCloseThreshold);
        Assert.Equal(400.0, warp, 2);

        // Lag acceleration when delta > 32 px: dynamic factor increases
        var lagStep = CombatSyncHelpers.SmoothStepPosition(100.0, 160.0, MobAlpha, MobSnapDistance, MobCloseThreshold);
        // delta = 60 > 32 -> factor = min(0.90, 0.72 + (60 - 32)/100) = 0.90 -> 100 + 60 * 0.90 = 154.0
        Assert.Equal(154.0, lagStep, 2);
    }

    [Theory]
    [InlineData("land", false)]
    [InlineData("jumpLand", false)]
    [InlineData("hardLand", true)]
    [InlineData("heavyLand", true)]
    [InlineData("dive", true)]
    [InlineData("downAtk", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsHardLanding_DistinguishesSoftAndHardLandings(string? anim, bool expected)
    {
        Assert.Equal(expected, CombatSyncHelpers.IsHardLanding(anim));
    }

    [Theory]
    [InlineData("idle", "jump", true)]
    [InlineData("run", "jump", true)]
    [InlineData("run", "leap", true)]
    [InlineData("fall", "jump", false)] // Already airborne
    [InlineData("jump", "jump", false)] // Already airborne
    [InlineData(null, "jump", true)]
    [InlineData("idle", "run", false)]
    [InlineData("run", null, false)]
    public void IsJumpTransition_DetectsNewJumpFromGround(string? prev, string? next, bool expected)
    {
        Assert.Equal(expected, CombatSyncHelpers.IsJumpTransition(prev, next));
    }

    [Theory]
    [InlineData("idle", "roll", true)]
    [InlineData("run", "dodge", true)]
    [InlineData("roll", "roll", false)] // Already rolling
    [InlineData("dodge", "dodge", false)] // Already dodging
    [InlineData("idle", "run", false)]
    [InlineData(null, "roll", true)]
    [InlineData("run", null, false)]
    public void IsRollTransition_DetectsRollOrDodgeStart(string? prev, string? next, bool expected)
    {
        Assert.Equal(expected, CombatSyncHelpers.IsRollTransition(prev, next));
    }

    [Theory]
    [InlineData("jump", "wallClimb", true)]
    [InlineData("fall", "wallGrab", true)]
    [InlineData("wallClimb", "wallClimb", false)] // Already on wall
    [InlineData("run", "idle", false)]
    public void IsWallTransition_DetectsWallInteraction(string? prev, string? next, bool expected)
    {
        Assert.Equal(expected, CombatSyncHelpers.IsWallTransition(prev, next));
    }

    [Theory]
    [InlineData("jump", "dive", true)]
    [InlineData("fall", "downAtk", true)]
    [InlineData("fall", "fastFall", true)]
    [InlineData("dive", "dive", false)] // Already diving
    [InlineData("run", "jump", false)]
    public void IsDiveTransition_DetectsDiveAttackStart(string? prev, string? next, bool expected)
    {
        Assert.Equal(expected, CombatSyncHelpers.IsDiveTransition(prev, next));
    }

    [Fact]
    public void ResolveWeaponSlashFx_MapsStartSwordStrikesToCrescentWaveTrails()
    {
        // Starter Sword Strike 1: fxAtkBackstabberA / atkBackStabberA -> fxSaberA with white/orange gradient
        var s1 = CombatSyncHelpers.ResolveWeaponSlashFx("fxAtkBackstabberA", "atkBackStabberA", 7901874, 7901874);
        Assert.Equal("fxSaberA", s1.resolvedFxId);
        Assert.Equal(16777215, s1.innerColor);
        Assert.Equal(16758355, s1.outerColor);

        // Starter Sword Strike 2: fxAtkBackstabberB / atkBackStabberB -> fxSaberB with white/orange gradient
        var s2 = CombatSyncHelpers.ResolveWeaponSlashFx("fxAtkBackstabberB", "atkBackStabberB", 7901874, 7901874);
        Assert.Equal("fxSaberB", s2.resolvedFxId);
        Assert.Equal(16777215, s2.innerColor);
        Assert.Equal(16758355, s2.outerColor);

        // Deduce from animId when fxId is null or empty
        var s1AnimOnly = CombatSyncHelpers.ResolveWeaponSlashFx(null, "atkBackStabberA");
        Assert.Equal("fxSaberA", s1AnimOnly.resolvedFxId);

        var s2AnimOnly = CombatSyncHelpers.ResolveWeaponSlashFx("", "atkBackStabberB");
        Assert.Equal("fxSaberB", s2AnimOnly.resolvedFxId);
    }

    [Fact]
    public void ResolveWeaponSlashFx_PreservesOtherWeaponsUnchanged()
    {
        // BroadSword
        var broad = CombatSyncHelpers.ResolveWeaponSlashFx("fxAtkBroadSwordA", "atkBroadSwordA", 7762137, 7762137);
        Assert.Equal("fxAtkBroadSwordA", broad.resolvedFxId);

        // BaseballBat
        var bat = CombatSyncHelpers.ResolveWeaponSlashFx("baseballBatAtkA", "AtkBaseballBatA", 100, 200);
        Assert.Equal("baseballBatAtkA", bat.resolvedFxId);
        Assert.Equal(100, bat.innerColor);
        Assert.Equal(200, bat.outerColor);

        // Null safe
        var empty = CombatSyncHelpers.ResolveWeaponSlashFx(null, null);
        Assert.Null(empty.resolvedFxId);
    }

    [Fact]
    public void ResolveWeaponSlashFx_PreservesCustomColors_ForNonStarterSwords()
    {
        // Weapons sharing atkBackStabber animations with custom colors (e.g., QuickSword 16367179) preserve their color
        var quickSword = CombatSyncHelpers.ResolveWeaponSlashFx("fxAtkBackstabberA", "atkBackStabberA", 16367179, 16367179);
        Assert.Equal("fxSaberA", quickSword.resolvedFxId);
        Assert.Equal(16367179, quickSword.innerColor);
        Assert.Equal(16367179, quickSword.outerColor);

        var lowHealth = CombatSyncHelpers.ResolveWeaponSlashFx("fxAtkBackstabberB", "atkBackStabberB", 10084688, 10084688);
        Assert.Equal("fxSaberB", lowHealth.resolvedFxId);
        Assert.Equal(10084688, lowHealth.innerColor);
        Assert.Equal(10084688, lowHealth.outerColor);
    }

    [Theory]
    [InlineData("pipe", true)]
    [InlineData("chute", true)]
    [InlineData("pipe_exit", true)]
    [InlineData("fall", true)]
    [InlineData("jump", true)]
    [InlineData("idle", false)]
    [InlineData("run", false)]
    public void IsAirborneAnim_RecognizesPassageAndFallTransitions(string? anim, bool expected)
    {
        Assert.Equal(expected, CombatSyncHelpers.IsAirborneAnim(anim));
    }

    [Theory]
    [InlineData(null, "crouch", true)]
    [InlineData("", "crouch", true)]
    [InlineData("fall", "crouch", true)]
    [InlineData("pipe", "crouch", true)]
    [InlineData("chute", "idle", true)]
    [InlineData("fall", "idle", true)]
    [InlineData(null, "land", true)]
    [InlineData("jump", "land", true)]
    [InlineData("pipe", "land", true)]
    [InlineData("idle", "run", false)]
    [InlineData("run", "idle", false)]
    public void IsLandingTransition_RecognizesCrouchAndPassageLandings(string? prevAnim, string? newAnim, bool expected)
    {
        Assert.Equal(expected, CombatSyncHelpers.IsLandingTransition(prevAnim, newAnim));
    }

    [Fact]
    public void ReviveConstants_HaveGenerousTolerancesForNetworkCoop()
    {
        Assert.True(Core.Constants.GameConstants.ReviveUseDistancePx >= 72.0);
        Assert.True(Core.Constants.GameConstants.ReviveRemotePositionValidationPx >= 240.0);
        Assert.True(Core.Constants.GameConstants.RemoteDownedStateStaleSeconds >= 60.0);
    }
}


