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
    [InlineData("crouch", false)]
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
    [InlineData("dive", "crouch", true)]
    [InlineData("idle", "crouch", false)]
    [InlineData("run", "crouch", false)]
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

    [Fact]
    public void InterElevatorStateEvent_RetainsProperties()
    {
        var evt = new DeadCellsMultiplayerMod.Interaction.InterElevatorStateEvent(
            userId: 2,
            anchorX: 120.0,
            anchorY: 240.0,
            sequence: 42,
            platformX: 120.0,
            platformY: 360.0,
            moving: true,
            levelId: "PrisonersQuarters");

        Assert.Equal(2, evt.UserId);
        Assert.Equal(120.0, evt.AnchorX);
        Assert.Equal(240.0, evt.AnchorY);
        Assert.Equal(42, evt.Sequence);
        Assert.Equal(120.0, evt.PlatformX);
        Assert.Equal(360.0, evt.PlatformY);
        Assert.True(evt.Moving);
        Assert.Equal("PrisonersQuarters", evt.LevelId);
    }

    [Theory]
    [InlineData("GTIME|123.456|1", 123.456, true)]
    [InlineData("GTIME|0|0", 0.0, false)]
    [InlineData("GTIME|45.2|false", 45.2, false)]
    [InlineData("GTIME|99.99|true", 99.99, true)]
    public void GameTimeProtocol_Parsing_MatchesExpectedValues(string line, double expectedTime, bool expectedStop)
    {
        var payload = line["GTIME|".Length..];
        var parts = payload.Split('|');
        Assert.True(parts.Length >= 2);
        Assert.True(double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedTime));
        var parsedStop = parts[1] == "1" || parts[1].Equals("true", System.StringComparison.OrdinalIgnoreCase);

        Assert.Equal(expectedTime, parsedTime, 3);
        Assert.Equal(expectedStop, parsedStop);
    }

    [Fact]
    public void EliteShrineEvent_WireEncoding_RoundTrips()
    {
        var ev = new DeadCellsMultiplayerMod.Interaction.InterEliteShrineEvent(2, 123.5, 456.75, "PrisonQuarters");
        Assert.Equal(2, ev.UserId);
        Assert.Equal(123.5, ev.X);
        Assert.Equal(456.75, ev.Y);
        Assert.Equal("PrisonQuarters", ev.LevelId);

        var wire = $"INTERSHRINE|{ev.UserId}|{ev.X.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{ev.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{ev.LevelId}";
        Assert.StartsWith("INTERSHRINE|", wire);

        var payload = wire["INTERSHRINE|".Length..];
        var parts = payload.Split('|');
        Assert.True(int.TryParse(parts[0], out var parsedUser));
        Assert.True(double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedX));
        Assert.True(double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedY));
        var parsedLevel = parts.Length >= 4 ? parts[3] : string.Empty;

        Assert.Equal(ev.UserId, parsedUser);
        Assert.Equal(ev.X, parsedX, 3);
        Assert.Equal(ev.Y, parsedY, 3);
        Assert.Equal(ev.LevelId, parsedLevel);
    }

    [Theory]
    [InlineData(3.5, false, 2.0)]
    [InlineData(3.0, false, 2.0)]
    [InlineData(2.5, false, 1.0)]
    [InlineData(2.0, false, 1.0)]
    [InlineData(1.5, false, 0.1)]
    [InlineData(1.0, false, 0.1)]
    [InlineData(0.5, false, 0.01)]
    [InlineData(0.0, false, 0.01)]
    [InlineData(-1.0, false, 0.01)]
    [InlineData(0.5, true, 0.4)]
    [InlineData(1.5, true, 0.4)]
    [InlineData(2.5, true, 1.0)]
    [InlineData(3.5, true, 2.0)]
    [InlineData(double.NaN, false, 0.1)]
    [InlineData(double.NaN, true, 0.4)]
    [InlineData(double.PositiveInfinity, false, 0.1)]
    public void CalculateLandingStunDuration_MatchesVanillaHeightThresholds(double height, bool isTransition, double expected)
    {
        var duration = CombatSyncHelpers.CalculateLandingStunDuration(height, isTransition);
        Assert.Equal(expected, duration, 3);
    }

    [Theory]
    [InlineData(null, true, false, CombatSyncHelpers.LocomotionSurface.Water)]
    [InlineData("PrisonersQuarters", true, false, CombatSyncHelpers.LocomotionSurface.Water)]
    [InlineData("Sewer", false, false, CombatSyncHelpers.LocomotionSurface.Pipe)]
    [InlineData("SewerOld", false, false, CombatSyncHelpers.LocomotionSurface.Pipe)]
    [InlineData("Cliff", false, false, CombatSyncHelpers.LocomotionSurface.Wood)]
    [InlineData("Greenhouse", false, false, CombatSyncHelpers.LocomotionSurface.Wood)]
    [InlineData("LighthouseBottom", false, false, CombatSyncHelpers.LocomotionSurface.Pipe)]
    [InlineData("LighthouseTop", false, false, CombatSyncHelpers.LocomotionSurface.Pipe)]
    [InlineData("Castle", false, true, CombatSyncHelpers.LocomotionSurface.Wood)]
    [InlineData("PrisonersQuarters", false, false, CombatSyncHelpers.LocomotionSurface.Stone)]
    [InlineData("", false, false, CombatSyncHelpers.LocomotionSurface.Stone)]
    public void ResolveLocomotionSurface_IdentifiesSurfaceType(string? biome, bool inWater, bool isWood, CombatSyncHelpers.LocomotionSurface expected)
    {
        var surface = CombatSyncHelpers.ResolveLocomotionSurface(biome, inWater, isWood);
        Assert.Equal(expected, surface);
    }

    [Theory]
    [InlineData(null, false, true, CombatSyncHelpers.ClimbingSurface.Vine)]
    [InlineData(null, true, false, CombatSyncHelpers.ClimbingSurface.Chain)]
    [InlineData("Swamp", false, false, CombatSyncHelpers.ClimbingSurface.Vine)]
    [InlineData("GardenerStage", false, false, CombatSyncHelpers.ClimbingSurface.Vine)]
    [InlineData("PurpleGarden", false, false, CombatSyncHelpers.ClimbingSurface.Vine)]
    [InlineData("ClockTower", false, false, CombatSyncHelpers.ClimbingSurface.Chain)]
    [InlineData("Astrolab", false, false, CombatSyncHelpers.ClimbingSurface.Chain)]
    [InlineData("Distillery", false, false, CombatSyncHelpers.ClimbingSurface.Chain)]
    [InlineData("PrisonersQuarters", false, false, CombatSyncHelpers.ClimbingSurface.Ladder)]
    [InlineData("", false, false, CombatSyncHelpers.ClimbingSurface.Ladder)]
    public void ResolveClimbingSurface_IdentifiesClimbingType(string? biome, bool isChain, bool isVine, CombatSyncHelpers.ClimbingSurface expected)
    {
        var surface = CombatSyncHelpers.ResolveClimbingSurface(biome, isChain, isVine);
        Assert.Equal(expected, surface);
    }

    [Theory]
    [InlineData("FireBomb", true)]
    [InlineData("Grenade", true)]
    [InlineData("ClusterGrenade", true)]
    [InlineData("ExplosiveDecoy", true)]
    [InlineData("IceBomb", true)]
    [InlineData("StunGrenade", true)]
    [InlineData("OilGrenade", true)]
    [InlineData("Phaser", false)]
    [InlineData("Grapple", false)]
    [InlineData("HeavyTurret", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsBombOrGrenadeKind_ClassifiesCorrectly(string? kind, bool expected)
    {
        var result = CombatSyncHelpers.IsBombOrGrenadeKind(kind);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void StepSkillProjectile_IntegratesPositionAndGravity()
    {
        double x = 100.0;
        double y = 200.0;
        double vx = 5.0;
        double vy = -3.0;
        double gravity = 0.5;

        // Step 1
        CombatSyncHelpers.StepSkillProjectile(ref x, ref y, ref vx, ref vy, gravity);
        Assert.Equal(105.0, x, 3);
        Assert.Equal(197.0, y, 3);
        Assert.Equal(5.0, vx, 3);
        Assert.Equal(-2.5, vy, 3);

        // Step 2
        CombatSyncHelpers.StepSkillProjectile(ref x, ref y, ref vx, ref vy, gravity);
        Assert.Equal(110.0, x, 3);
        Assert.Equal(194.5, y, 3);
        Assert.Equal(5.0, vx, 3);
        Assert.Equal(-2.0, vy, 3);

        // Step 3
        CombatSyncHelpers.StepSkillProjectile(ref x, ref y, ref vx, ref vy, gravity);
        Assert.Equal(115.0, x, 3);
        Assert.Equal(192.5, y, 3);
        Assert.Equal(5.0, vx, 3);
        Assert.Equal(-1.5, vy, 3);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(8, true)]
    [InlineData(9, true)]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(4, false)]
    public void IsSolidTileValue_RecognizesSolidAndOneWayBits(int bits, bool expected)
    {
        Assert.Equal(expected, CombatSyncHelpers.IsSolidTileValue(bits));
    }

    [Fact]
    public void IsSolidTile_ChecksBoundsAndBitmaskInMemory()
    {
        int wid = 4;
        int hei = 4;
        var bytes = System.Runtime.InteropServices.Marshal.AllocHGlobal(wid * hei * 4);
        try
        {
            // Zero-initialize
            for (int i = 0; i < wid * hei; i++)
            {
                System.Runtime.InteropServices.Marshal.WriteInt32(bytes, i * 4, 0);
            }

            // Set (1, 1) = solid wall (bit 1)
            System.Runtime.InteropServices.Marshal.WriteInt32(bytes, (1 * wid + 1) * 4, 1);
            // Set (2, 2) = wood platform (bit 8)
            System.Runtime.InteropServices.Marshal.WriteInt32(bytes, (2 * wid + 2) * 4, 8);

            Assert.True(CombatSyncHelpers.IsSolidTile(bytes, wid, hei, 1, 1));
            Assert.True(CombatSyncHelpers.IsSolidTile(bytes, wid, hei, 2, 2));
            Assert.False(CombatSyncHelpers.IsSolidTile(bytes, wid, hei, 0, 0));
            Assert.False(CombatSyncHelpers.IsSolidTile(bytes, wid, hei, 3, 3));

            // Out-of-bounds coordinates return true (solid boundary)
            Assert.True(CombatSyncHelpers.IsSolidTile(bytes, wid, hei, -1, 0));
            Assert.True(CombatSyncHelpers.IsSolidTile(bytes, wid, hei, 4, 0));
            Assert.True(CombatSyncHelpers.IsSolidTile(bytes, wid, hei, 0, -1));
            Assert.True(CombatSyncHelpers.IsSolidTile(bytes, wid, hei, 0, 4));

            // Null pointer returns false
            Assert.False(CombatSyncHelpers.IsSolidTile(System.IntPtr.Zero, wid, hei, 1, 1));
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(bytes);
        }
    }

    [Theory]
    [InlineData("dive", true)]
    [InlineData("downAtk", true)]
    [InlineData("jump", true)]
    [InlineData("fall", true)]
    [InlineData("run", false)]
    [InlineData("idle", false)]
    public void IsAirborneAnim_IncludesDiveAndDownAttack(string anim, bool expected)
    {
        Assert.Equal(expected, CombatSyncHelpers.IsAirborneAnim(anim));
    }

    [Theory]
    [InlineData(8, true)]
    [InlineData(9, true)]
    [InlineData(12, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(4, false)]
    public void IsWoodTileValue_RecognizesWoodBit(int bits, bool expected)
    {
        Assert.Equal(expected, CombatSyncHelpers.IsWoodTileValue(bits));
    }

    [Fact]
    public void IsWoodTile_ChecksBoundsAndBitmaskInMemory()
    {
        int wid = 3;
        int hei = 3;
        var bytes = System.Runtime.InteropServices.Marshal.AllocHGlobal(wid * hei * 4);
        try
        {
            for (int i = 0; i < wid * hei; i++)
            {
                System.Runtime.InteropServices.Marshal.WriteInt32(bytes, i * 4, 0);
            }

            // Set (1, 1) = wood platform (bit 8)
            System.Runtime.InteropServices.Marshal.WriteInt32(bytes, (1 * wid + 1) * 4, 8);
            // Set (0, 1) = solid stone wall (bit 1, not wood)
            System.Runtime.InteropServices.Marshal.WriteInt32(bytes, (1 * wid + 0) * 4, 1);

            Assert.True(CombatSyncHelpers.IsWoodTile(bytes, wid, hei, 1, 1));
            Assert.False(CombatSyncHelpers.IsWoodTile(bytes, wid, hei, 0, 1));
            Assert.False(CombatSyncHelpers.IsWoodTile(bytes, wid, hei, 2, 2));

            // Out-of-bounds coordinates return false
            Assert.False(CombatSyncHelpers.IsWoodTile(bytes, wid, hei, -1, 0));
            Assert.False(CombatSyncHelpers.IsWoodTile(bytes, wid, hei, 3, 0));

            // Null pointer returns false
            Assert.False(CombatSyncHelpers.IsWoodTile(System.IntPtr.Zero, wid, hei, 1, 1));
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(bytes);
        }
    }

    [Theory]
    [InlineData("turret", true)]
    [InlineData("heavy_turret", true)]
    [InlineData("crossbowmatic", true)]
    [InlineData("sinew_slicer", true)]
    [InlineData("slicer", true)]
    [InlineData("bear_trap", true)]
    [InlineData("trap", true)]
    [InlineData("deployable_cleaver", true)]
    [InlineData("crusher", true)]
    [InlineData("sword", false)]
    [InlineData("bow", false)]
    [InlineData("shield", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsDeployableSkillKind_ClassifiesDeployablesCorrectly(string? kind, bool expected)
    {
        Assert.Equal(expected, CombatSyncHelpers.IsDeployableSkillKind(kind));
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.01, 0.0)]
    [InlineData(-0.04, 0.0)]
    [InlineData(0.05, 1.2)] // 0.05 * 24.0 = 1.2
    [InlineData(1.0, 24.0)]
    [InlineData(-1.0, -24.0)]
    [InlineData(2.0, 2.0)] // > 1.5 is already in pixels
    public void VelocityToPixels_AppliesDeadbandAndPixelScaling(double input, double expected)
    {
        var result = DeadCellsMultiplayerMod.Mobs.MobsSynchronization.MobSnapshotBuffer.VelocityToPixels(input);
        Assert.Equal(expected, result, precision: 2);
    }
}

