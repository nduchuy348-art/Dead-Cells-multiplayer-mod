using System;
using DeadCellsMultiplayerMod.Ghost;
using DeadCellsMultiplayerMod.Interaction;
using DeadCellsMultiplayerMod.Mobs.MobsSynchronization;
using Xunit;

namespace DeadCellsMultiplayerMod.ProtocolReplay.Tests;

/// <summary>
/// Regression test suite verifying fixes for co-op multiplayer issues:
/// - Deadband velocity thresholding in MobSnapshotBuffer (Issue 5)
/// - Hard landing vs crouch false stomp suppression (Issue 6)
/// - Deployable skill projectile and turret tracking (Issue 11/12)
/// - Animation classification and interpolation stability
/// </summary>
public sealed class CoopBugfixRegressionTests
{
    [Theory]
    [InlineData("crouch", false)]
    [InlineData("crouch_down", false)]
    [InlineData("CROUCH", false)]
    [InlineData("stand_to_crouch", false)]
    [InlineData("hardLand", true)]
    [InlineData("heavyLand", true)]
    [InlineData("dive", true)]
    [InlineData("dive_slam", true)]
    [InlineData("downAtk", true)]
    [InlineData("hero_downAtk_end", true)]
    [InlineData("land", false)]
    [InlineData("jumpLand", false)]
    [InlineData("run", false)]
    [InlineData("idle", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void IsHardLanding_SuppressesCrouchAndPreservesRealStomps(string? anim, bool expected)
    {
        var result = CombatSyncHelpers.IsHardLanding(anim);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.01, 0.0)]
    [InlineData(-0.01, 0.0)]
    [InlineData(0.0499, 0.0)]
    [InlineData(-0.0499, 0.0)]
    [InlineData(0.05, 1.2)] // 0.05 * 24.0 = 1.2
    [InlineData(-0.05, -1.2)]
    [InlineData(0.1, 2.4)]
    [InlineData(1.0, 24.0)]
    [InlineData(-1.0, -24.0)]
    [InlineData(1.5, 36.0)]
    [InlineData(1.5001, 1.5001)] // > 1.5 is already in pixel coordinates
    [InlineData(2.0, 2.0)]
    [InlineData(100.0, 100.0)]
    [InlineData(-50.0, -50.0)]
    [InlineData(double.NaN, 0.0)]
    [InlineData(double.PositiveInfinity, 0.0)]
    [InlineData(double.NegativeInfinity, 0.0)]
    public void VelocityToPixels_AppliesDeadbandAndScale(double input, double expected)
    {
        var result = MobSnapshotBuffer.VelocityToPixels(input);
        Assert.Equal(expected, result, precision: 3);
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
    [InlineData("deploy_mine", true)]
    [InlineData("crusher", true)]
    [InlineData("sword", false)]
    [InlineData("broadsword", false)]
    [InlineData("bow", false)]
    [InlineData("shield", false)]
    [InlineData("grenade", false)]
    [InlineData("fire_grenade", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void IsDeployableSkillKind_CorrectlyDistinguishesDeployables(string? kind, bool expected)
    {
        var result = CombatSyncHelpers.IsDeployableSkillKind(kind);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void MobSnapshotBuffer_HermiteWithDeadbandSuppressedDrift()
    {
        var buffer = new MobSnapshotBuffer();

        // Feed snapshots with micro-jitter (vel < 0.05) that should be deadband-suppressed
        buffer.Add(new MobSnapshot(
            receivedFrame: 0.0,
            x: 100.0,
            y: 200.0,
            dx: 0.03, // < 0.05 deadband -> 0
            dy: -0.02, // < 0.05 deadband -> 0
            dir: 1,
            life: 50,
            maxLife: 50,
            animPayload: "idle",
            statePayload: ""));

        buffer.Add(new MobSnapshot(
            receivedFrame: 10.0,
            x: 100.0,
            y: 200.0,
            dx: -0.04, // < 0.05 deadband -> 0
            dy: 0.01,  // < 0.05 deadband -> 0
            dir: 1,
            life: 50,
            maxLife: 50,
            animPayload: "idle",
            statePayload: ""));

        // Sampling at mid-frame should produce stationary position without micro-velocity drift
        bool ok = buffer.SampleHermite(
            renderFrame: 5.0,
            out var x,
            out var y,
            out var dir,
            out var anim,
            out var snap,
            out var vertSnap);

        Assert.True(ok);
        Assert.Equal(100.0, x, precision: 3);
        Assert.Equal(200.0, y, precision: 3);
        Assert.Equal(1, dir);
        Assert.Equal("idle", anim);
        Assert.False(snap);
        Assert.False(vertSnap);
    }

    [Fact]
    public void StepSkillProjectile_UpdatesPositionAlongVelocity()
    {
        double x = 100.0;
        double y = 50.0;
        double vx = 10.0;
        double vy = -5.0;
        double gravity = 0.5;

        CombatSyncHelpers.StepSkillProjectile(ref x, ref y, ref vx, ref vy, gravity);

        Assert.Equal(110.0, x, precision: 3); // 100 + 10
        Assert.Equal(45.0, y, precision: 3);  // 50 + (-5)
        Assert.Equal(10.0, vx, precision: 3);
        Assert.Equal(-4.5, vy, precision: 3); // -5 + 0.5
    }

    [Fact]
    public void MobSnapshotBuffer_ExtrapolationBeyondLatestSnapshot_ClampsCleanly()
    {
        var buffer = new MobSnapshotBuffer();
        buffer.Add(new MobSnapshot(
            receivedFrame: 0.0,
            x: 100.0,
            y: 200.0,
            dx: 1.0,
            dy: 0.0,
            dir: 1,
            life: 50,
            maxLife: 50,
            animPayload: "run",
            statePayload: ""));

        buffer.Add(new MobSnapshot(
            receivedFrame: 10.0,
            x: 120.0,
            y: 200.0,
            dx: 1.0,
            dy: 0.0,
            dir: 1,
            life: 50,
            maxLife: 50,
            animPayload: "run",
            statePayload: ""));

        // When render frame is far past the latest snapshot (e.g. frame 25 vs frame 10),
        // extrapolation is capped to elapsed = 4.0 frames (4 * 24px = 96px).
        // Position clamps to 120 + 96 = 216 and does NOT diverge to infinity.
        bool ok = buffer.SampleHermite(
            renderFrame: 25.0,
            out var x,
            out var y,
            out var dir,
            out var anim,
            out var snap,
            out var vertSnap);

        Assert.True(ok);
        Assert.Equal(216.0, x, precision: 3);
        Assert.Equal(200.0, y, precision: 3);
        Assert.Equal(1, dir);
        Assert.Equal("run", anim);

        // Frame 50 should yield the exact same clamped position as Frame 25
        buffer.SampleHermite(
            renderFrame: 50.0,
            out var x50,
            out var y50,
            out _,
            out _,
            out _,
            out _);
        Assert.Equal(216.0, x50, precision: 3);
        Assert.Equal(200.0, y50, precision: 3);
    }

    [Theory]
    [InlineData(0.0, 1.0, 1.0)]       // Distance 0 -> full volume
    [InlineData(13.0, 1.0, 0.25)]     // Distance 13 (half of 26) -> 0.25 volume
    [InlineData(20.0, 1.0, 0.053)]    // Distance 20 -> (1 - 20/26)^2 ~= 0.053
    [InlineData(23.0, 1.0, null)]     // Distance 23 -> (1 - 23/26)^2 = 0.0133 < 0.02 cutoff -> null
    [InlineData(26.0, 1.0, null)]     // Distance 26 (at max) -> null
    [InlineData(35.0, 1.0, null)]     // Distance > max -> null
    [InlineData(5.0, 0.0, null)]      // Zero initial volume -> null
    [InlineData(5.0, -1.0, null)]     // Negative initial volume -> null
    [InlineData(5.0, double.NaN, null)] // NaN initial volume -> null
    public void CalculateSpatializedVolume_AttenuationAndCutoffBoundaries(double distance, double initialVol, double? expectedVol)
    {
        var result = CombatSyncHelpers.CalculateSpatializedVolume(
            heroX: 0.0,
            heroY: 0.0,
            srcX: distance,
            srcY: 0.0,
            originalVolume: initialVol,
            maxDistanceCases: 26.0);

        if (expectedVol == null)
        {
            Assert.Null(result);
        }
        else
        {
            Assert.NotNull(result);
            Assert.Equal(expectedVol.Value, result.Value, precision: 2);
        }
    }

    [Fact]
    public void ResolveWeaponSlashFx_HandlesCasingAndEdgeCases()
    {
        // Case-insensitivity check for starter sword
        var upper = CombatSyncHelpers.ResolveWeaponSlashFx("FXATKBACKSTABBERA", "ATKBACKSTABBERA", 1234, 5678);
        Assert.Equal("fxSaberA", upper.resolvedFxId);
        Assert.Equal(16777215, upper.innerColor);
        Assert.Equal(16758355, upper.outerColor);

        var mixed = CombatSyncHelpers.ResolveWeaponSlashFx("fxAtkBackstabberB", "AtkBackStabberB", 1234, 5678);
        Assert.Equal("fxSaberB", mixed.resolvedFxId);
        Assert.Equal(16777215, mixed.innerColor);
        Assert.Equal(16758355, mixed.outerColor);

        // Whitespace and empty inputs are safely trimmed
        var empty = CombatSyncHelpers.ResolveWeaponSlashFx("   ", "   ");
        Assert.Equal("", empty.resolvedFxId);

        var nullAnim = CombatSyncHelpers.ResolveWeaponSlashFx(null, null);
        Assert.Null(nullAnim.resolvedFxId);
    }

    [Theory]
    [InlineData("jump", true)]
    [InlineData("fall", true)]
    [InlineData("pipe", true)]
    [InlineData("chute", true)]
    [InlineData("dive", true)]
    [InlineData("downAtk", true)]
    [InlineData("JUMP_LOOP", true)]
    [InlineData("hero_fall", true)]
    [InlineData("idle", false)]
    [InlineData("run", false)]
    [InlineData("walk", false)]
    [InlineData("crouch", false)]
    [InlineData("roll", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsAirborneAnim_CompleteClassificationMatrix(string? anim, bool expected)
    {
        Assert.Equal(expected, CombatSyncHelpers.IsAirborneAnim(anim));
    }

    [Theory]
    // Airborne to any ground state => Landing transition
    [InlineData("jump", "idle", true)]
    [InlineData("fall", "idle", true)]
    [InlineData("pipe", "run", true)]
    [InlineData("chute", "walk", true)]
    [InlineData("dive", "crouch", true)]
    [InlineData("downAtk", "crouch", true)]
    // Explicit "land" animation => Landing transition regardless of previous state
    [InlineData("idle", "land", true)]
    [InlineData("run", "hardLand", true)]
    [InlineData(null, "land", true)]
    // Ground-to-ground transitions => NOT landing transition
    [InlineData("idle", "run", false)]
    [InlineData("run", "idle", false)]
    [InlineData("walk", "idle", false)]
    [InlineData("idle", "crouch", false)] // Crouch while standing should not cause landing stomp
    [InlineData("run", "crouch", false)]  // Slide into crouch should not cause landing stomp
    [InlineData(null, null, false)]
    public void IsLandingTransition_CompleteMatrix(string? prevAnim, string? nextAnim, bool expected)
    {
        Assert.Equal(expected, CombatSyncHelpers.IsLandingTransition(prevAnim, nextAnim));
    }

    [Theory]
    [InlineData("idle", "jump", true)]
    [InlineData("run", "jump", true)]
    [InlineData("walk", "leap", true)]
    [InlineData("jump", "jump", false)]     // Air jump / double jump must not trigger ground jump VFX
    [InlineData("fall", "jump", false)]     // Coyote jump from air
    [InlineData(null, "jump", true)]
    [InlineData("idle", "idle", false)]
    [InlineData("idle", null, false)]
    public void IsJumpTransition_CompleteMatrix(string? prevAnim, string? nextAnim, bool expected)
    {
        Assert.Equal(expected, CombatSyncHelpers.IsJumpTransition(prevAnim, nextAnim));
    }

    [Theory]
    [InlineData("idle", "roll", true)]
    [InlineData("run", "dodge", true)]
    [InlineData("roll", "roll", false)]     // Continuing roll should not re-trigger dodge VFX
    [InlineData("dodge", "dodge", false)]
    [InlineData("idle", "run", false)]
    [InlineData(null, "roll", true)]
    public void IsRollTransition_CompleteMatrix(string? prevAnim, string? nextAnim, bool expected)
    {
        Assert.Equal(expected, CombatSyncHelpers.IsRollTransition(prevAnim, nextAnim));
    }

    [Fact]
    public void CalculateSpatializedVolume_VerticalDistance_AttenuatesFasterThanHorizontal()
    {
        // Bug 9: Multi-floor weapon sounds in Biome 2 bleed across map unless vertical distance is weighted.
        // At horizontal delta = 15, dy = 0:
        var horizontal = CombatSyncHelpers.CalculateSpatializedVolume(0, 0, 15, 0, 1.0, 26.0);
        // At vertical delta = 15, dx = 0: effective distance is 15 * 1.35 = 20.25:
        var vertical = CombatSyncHelpers.CalculateSpatializedVolume(0, 0, 0, 15, 1.0, 26.0);

        Assert.NotNull(horizontal);
        Assert.NotNull(vertical);
        // Vertical volume must be substantially lower than horizontal volume at the same coordinate distance
        Assert.True(vertical.Value < horizontal.Value);
        Assert.True(vertical.Value < 0.06); // ~0.049 vs ~0.179

        // At vertical delta = 18: effective distance is 18 * 1.35 = 24.3.
        // (1 - 24.3/26)^2 = 0.0042 < 0.035 cutoff threshold -> should be suppressed (null)
        var verticalSuppressed = CombatSyncHelpers.CalculateSpatializedVolume(0, 0, 0, 18, 1.0, 26.0);
        Assert.Null(verticalSuppressed);

        // While horizontal delta = 18 is still audible
        var horizontalAudible = CombatSyncHelpers.CalculateSpatializedVolume(0, 0, 18, 0, 1.0, 26.0);
        Assert.NotNull(horizontalAudible);
        Assert.True(horizontalAudible.Value > 0.035);
    }

    [Fact]
    public void CalculateSpatializedVolume_MinimumAudibleThreshold_CutsOffFaintReverbTriggers()
    {
        // When effective distance would produce volume below MinimumAudibleThreshold (0.035), return null
        // For maxDist = 26: (1 - d/26)^2 = 0.035 => 1 - d/26 = 0.187 => d = 26 * 0.813 = 21.14
        // At d = 22, volume is (1 - 22/26)^2 = (4/26)^2 = 0.0236 < 0.035 => suppressed to null
        var faintSound = CombatSyncHelpers.CalculateSpatializedVolume(0, 0, 22, 0, 1.0, 26.0);
        Assert.Null(faintSound);

        // At d = 20, volume is (1 - 20/26)^2 = (6/26)^2 = 0.0532 > 0.035 => preserved
        var audibleSound = CombatSyncHelpers.CalculateSpatializedVolume(0, 0, 20, 0, 1.0, 26.0);
        Assert.NotNull(audibleSound);
        Assert.True(audibleSound.Value >= 0.035);
    }

    [Fact]
    public void MultiplayerSettingsData_ProfileSlot_DefaultAndRangeValidation()
    {
        // Bug 8: Player 2 profile persistence
        var data = new MultiplayerSettingsData();
        Assert.Equal(-1, data.MultiplayerProfileSlot);

        data.MultiplayerProfileSlot = 2;
        Assert.Equal(2, data.MultiplayerProfileSlot);
    }

    [Fact]
    public void MUser_ResolveSlotFromSources_FollowsStrictPriorityHierarchy()
    {
        // Bug 8: Priority 1: Explicit slot
        Assert.Equal(2, DeadCellsMultiplayerMod.MUser.ResolveSlotFromSources(2, "3", 1, () => 0));

        // Priority 2: DCCM_SAVE_SLOT environment variable (per-process isolation for co-op)
        Assert.Equal(3, DeadCellsMultiplayerMod.MUser.ResolveSlotFromSources(null, "3", 1, () => 0));

        // Priority 3: MultiplayerSettingsStorage profile slot (persisted Player 2 settings)
        Assert.Equal(1, DeadCellsMultiplayerMod.MUser.ResolveSlotFromSources(null, null, 1, () => 0));

        // Priority 4: Game options fallback
        Assert.Equal(4, DeadCellsMultiplayerMod.MUser.ResolveSlotFromSources(null, null, -1, () => 4));

        // Priority 5: Default slot 0
        Assert.Equal(0, DeadCellsMultiplayerMod.MUser.ResolveSlotFromSources(null, null, -1, null));
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(-1.0, 0.0)]
    [InlineData(double.NaN, 0.0)]
    [InlineData(0.1, 0.1)]
    [InlineData(0.3, 0.3)]
    [InlineData(0.35, 0.35)]
    [InlineData(0.4, 0.35)]
    [InlineData(1.0, 0.35)]
    [InlineData(5.0, 0.35)]
    public void ClampLandingStunDuration_ClampsExcessiveStun(double duration, double expected)
    {
        var result = CombatSyncHelpers.ClampLandingStunDuration(duration, 0.35);
        Assert.Equal(expected, result, precision: 3);
    }

    [Theory]
    [InlineData("10:20", "10:20", 6, true)] // Identical door
    [InlineData("10:20", "12:22", 6, true)] // Within 6 tiles (dx=2, dy=2)
    [InlineData("10:20", "16:26", 6, true)] // Exactly 6 tiles (dx=6, dy=6)
    [InlineData("10:20", "17:20", 6, false)] // dx=7, exceeds 6
    [InlineData("10:20", "10:27", 6, false)] // dy=7, exceeds 6
    [InlineData("invalid", "10:20", 6, false)] // Invalid format
    [InlineData(null, "10:20", 6, false)] // Null key
    public void CombatSyncHelpers_IsDoorProximityMatch_ValidatesProximity(string? k1, string? k2, int threshold, bool expected)
    {
        var result = CombatSyncHelpers.IsDoorProximityMatch(k1!, k2!, threshold);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(-1, false)]
    [InlineData(-50, false)]
    [InlineData(int.MinValue, false)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    [InlineData(100, false)]
    [InlineData(int.MaxValue, false)]
    [InlineData(null, false)]
    public void MUser_IsValidSlot_ValidatesBoundaries(int? slot, bool expected)
    {
        var result = DeadCellsMultiplayerMod.MUser.IsValidSlot(slot);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("corrupt")]
    [InlineData("abc12")]
    [InlineData("12abc")]
    [InlineData("!@#$%")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\0")]
    [InlineData("99999999999999999999999999999999")]
    [InlineData("null")]
    public void MUser_ResolveSlotFromSources_HandlesCorruptStrings(string corruptEnvSlot)
    {
        // Corrupt string falls through to valid profileSlot
        var slotWithProfile = DeadCellsMultiplayerMod.MUser.ResolveSlotFromSources(null, corruptEnvSlot, 2, () => 0);
        Assert.Equal(2, slotWithProfile);

        // Corrupt string with no other sources falls through to default slot 0
        var slotDefault = DeadCellsMultiplayerMod.MUser.ResolveSlotFromSources(null, corruptEnvSlot, -1, null);
        Assert.Equal(0, slotDefault);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-5)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    public void MUser_ResolveSlotFromSources_HandlesNegativeNumbers(int negativeVal)
    {
        // Negative explicit slot falls through to env slot
        var slotFromEnv = DeadCellsMultiplayerMod.MUser.ResolveSlotFromSources(negativeVal, "2", 1, () => 0);
        Assert.Equal(2, slotFromEnv);

        // Negative env slot string falls through to profile slot
        var slotFromProfile = DeadCellsMultiplayerMod.MUser.ResolveSlotFromSources(null, negativeVal.ToString(), 3, () => 0);
        Assert.Equal(3, slotFromProfile);

        // All negative sources fall through to default slot 0
        var slotDefault = DeadCellsMultiplayerMod.MUser.ResolveSlotFromSources(negativeVal, negativeVal.ToString(), negativeVal, () => negativeVal);
        Assert.Equal(0, slotDefault);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public void MUser_ResolveSlotFromSources_HandlesOutOfRangeSlots(int outOfRangeVal)
    {
        // Out-of-range explicit slot falls through to env slot
        var slotFromEnv = DeadCellsMultiplayerMod.MUser.ResolveSlotFromSources(outOfRangeVal, "4", 1, () => 0);
        Assert.Equal(4, slotFromEnv);

        // Out-of-range env slot string falls through to profile slot
        var slotFromProfile = DeadCellsMultiplayerMod.MUser.ResolveSlotFromSources(null, outOfRangeVal.ToString(), 2, () => 0);
        Assert.Equal(2, slotFromProfile);

        // Out-of-range fallback delegate falls through to default slot 0
        var slotDefault = DeadCellsMultiplayerMod.MUser.ResolveSlotFromSources(outOfRangeVal, outOfRangeVal.ToString(), outOfRangeVal, () => outOfRangeVal);
        Assert.Equal(0, slotDefault);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void MUser_ResolveSlotFromSources_HandlesMissingEnvironmentVariables(string? missingEnv)
    {
        // Missing env falls through to profile slot
        var slotFromProfile = DeadCellsMultiplayerMod.MUser.ResolveSlotFromSources(null, missingEnv, 3, null);
        Assert.Equal(3, slotFromProfile);

        // Missing env with no other source falls through to default slot 0
        var slotDefault = DeadCellsMultiplayerMod.MUser.ResolveSlotFromSources(null, missingEnv, -1, null);
        Assert.Equal(0, slotDefault);
    }

    [Fact]
    public void MUser_ResolveSlotFromSources_HandlesThrowingGameOptionsFallback()
    {
        var slot = DeadCellsMultiplayerMod.MUser.ResolveSlotFromSources(
            null,
            null,
            -1,
            () => throw new InvalidOperationException("Simulated early game options uninitialized state"));

        Assert.Equal(0, slot);
    }

    [Theory]
    [InlineData("10:20", true, 10, 20)]
    [InlineData("0:0", true, 0, 0)]
    [InlineData("100:250", true, 100, 250)]
    [InlineData("-5:12", true, -5, 12)]
    [InlineData("invalid", false, 0, 0)]
    [InlineData("10:", false, 0, 0)]
    [InlineData(":20", false, 0, 0)]
    [InlineData("", false, 0, 0)]
    [InlineData(null, false, 0, 0)]
    public void CombatSyncHelpers_TryParseDoorKey_ValidatesCoordinates(string? key, bool expectedSuccess, int expectedCx, int expectedCy)
    {
        var success = CombatSyncHelpers.TryParseDoorKey(key!, out var cx, out var cy);
        Assert.Equal(expectedSuccess, success);
        if (expectedSuccess)
        {
            Assert.Equal(expectedCx, cx);
            Assert.Equal(expectedCy, cy);
        }
    }

    [Fact]
    public void BossCinePayload_BuildAndParse_RoundtripsAccurately()
    {
        var payload = CombatSyncHelpers.BuildBossCinePayload(
            "BossLevel",
            "roomDeath",
            100.5,
            200.5,
            1,
            150.0,
            200.5,
            -1);

        var parsed = CombatSyncHelpers.TryParseBossCinePayload(
            payload,
            out var levelId,
            out var eventId,
            out var snapX,
            out var snapY,
            out var snapDir,
            out var finalX,
            out var finalY,
            out var finalDir);

        Assert.True(parsed);
        Assert.Equal("BossLevel", levelId);
        Assert.Equal("roomDeath", eventId);
        Assert.True(snapX.HasValue && Math.Abs(snapX.Value - 100.5) < 0.001);
        Assert.True(snapY.HasValue && Math.Abs(snapY.Value - 200.5) < 0.001);
        Assert.Equal(1, snapDir);
        Assert.True(finalX.HasValue && Math.Abs(finalX.Value - 150.0) < 0.001);
        Assert.True(finalY.HasValue && Math.Abs(finalY.Value - 200.5) < 0.001);
        Assert.Equal(-1, finalDir);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("BossLevel", true)]
    [InlineData("BossLevel|roomEvent", true)]
    [InlineData("BossLevel|roomEvent|100.0|200.0", true)]
    public void BossCinePayload_TryParse_HandlesVariousFormats(string? raw, bool expectedSuccess)
    {
        var success = CombatSyncHelpers.TryParseBossCinePayload(
            raw,
            out var levelId,
            out var _,
            out var _,
            out var _,
            out var _,
            out var _,
            out var _,
            out var _);

        Assert.Equal(expectedSuccess, success);
        if (expectedSuccess)
            Assert.Equal("BossLevel", levelId);
    }

    [Theory]
    [InlineData(-10.0, 0.0)]
    [InlineData(double.NegativeInfinity, 0.0)]
    [InlineData(double.PositiveInfinity, 0.35)]
    public void CombatSyncHelpers_ClampLandingStunDuration_HandlesExtremeValues(double duration, double expected)
    {
        var result = CombatSyncHelpers.ClampLandingStunDuration(duration, 0.35);
        Assert.Equal(expected, result, precision: 3);
    }

    [Theory]
    [InlineData("10:20", "10:20", -5, true)]
    [InlineData("10:20", "11:20", -5, false)]
    [InlineData("10:20", "10:21", -1, false)]
    [InlineData("10:20", "10:20", -1, true)]
    public void CombatSyncHelpers_IsDoorProximityMatch_ClampsNegativeThreshold(string k1, string k2, int threshold, bool expected)
    {
        var result = CombatSyncHelpers.IsDoorProximityMatch(k1, k2, threshold);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void BossCinePayload_TryParse_RejectsNonFiniteCoordinates()
    {
        var raw = "BossLevel|roomEvent|NaN|200.0|1|150.0|Infinity|-1";
        var parsed = CombatSyncHelpers.TryParseBossCinePayload(
            raw,
            out var levelId,
            out var eventId,
            out var snapX,
            out var snapY,
            out var snapDir,
            out var finalX,
            out var finalY,
            out var finalDir);

        Assert.True(parsed);
        Assert.Equal("BossLevel", levelId);
        Assert.Equal("roomEvent", eventId);
        Assert.Null(snapX); // NaN rejected
        Assert.Equal(200.0, snapY);
        Assert.Equal(1, snapDir);
        Assert.Equal(150.0, finalX);
        Assert.Null(finalY); // Infinity rejected
        Assert.Equal(-1, finalDir);
    }

    [Fact]
    public void MobWireBinary_TryParseMobStatesBinary_RejectsNonFiniteFloats()
    {
        var states = new List<NetNode.MobStateSnapshot>
        {
            new NetNode.MobStateSnapshot(1, 100.0, 200.0, 1, 50, 50, "idle", "", "", 0, 1.0, 0.0, 0.0)
        };

        var ok = MobWireBinary.TryBuildMobStatesBinary(states, out var bytes);
        Assert.True(ok);
        Assert.NotNull(bytes);

        // Valid roundtrip
        var dest = new List<NetNode.MobStateSnapshot>();
        Assert.True(MobWireBinary.TryParseMobStatesBinary(bytes, dest));
        Assert.Single(dest);
        Assert.Equal(100.0, dest[0].X);

        // Corrupt x with double.NaN
        var corrupted = (byte[])bytes.Clone();
        var nanBytes = BitConverter.GetBytes(double.NaN);
        // ver(1) + count(2) + index(4) + gen(4) = 11 offset for x
        Array.Copy(nanBytes, 0, corrupted, 11, 8);

        var corruptDest = new List<NetNode.MobStateSnapshot>();
        var corruptOk = MobWireBinary.TryParseMobStatesBinary(corrupted, corruptDest);
        Assert.False(corruptOk);
        Assert.Empty(corruptDest);
    }

    [Theory]
    [InlineData("0x1")]
    [InlineData("1e0")]
    [InlineData("2.0")]
    [InlineData("3,0")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void MUser_ResolveSlotFromSources_RejectsNonStandardNumericFormats(string envSlot)
    {
        // Falls through to profile slot 4
        var result = DeadCellsMultiplayerMod.MUser.ResolveSlotFromSources(null, envSlot, 4, null);
        Assert.Equal(4, result);
    }

    [Theory]
    [InlineData("  0  ", 0)]
    [InlineData("  3 \t\r\n", 3)]
    [InlineData("+4", 4)]
    [InlineData("5", 5)]
    [InlineData("-0", 0)]
    public void MUser_ResolveSlotFromSources_AcceptsFormattedValidIntegerStrings(string envSlot, int expected)
    {
        var result = DeadCellsMultiplayerMod.MUser.ResolveSlotFromSources(null, envSlot, -1, null);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void RemoteSnapshot_SanitizesNonFiniteCoordinates()
    {
        var snapshot = new NetNode.RemoteSnapshot(
            id: 2,
            x: double.NaN,
            y: double.PositiveInfinity,
            dir: 1,
            levelId: "PrisonersQuarters",
            roomLevelId: null,
            roomId: null,
            hasRoom: false,
            anim: null,
            animQueue: null,
            animG: null,
            hasAnim: false,
            username: "Player1",
            headAnim: null,
            hasHeadAnim: false);

        Assert.Equal(0.0, snapshot.X);
        Assert.Equal(0.0, snapshot.Y);
        Assert.False(double.IsNaN(snapshot.X));
        Assert.False(double.IsInfinity(snapshot.Y));
    }

    [Fact]
    public void MobStateSnapshot_SanitizesNonFiniteAndNegativeValues()
    {
        var mob = new NetNode.MobStateSnapshot(
            index: -5,
            x: double.NegativeInfinity,
            y: double.NaN,
            dir: 1,
            life: -10,
            maxLife: -50,
            animPayload: null!,
            type: null!,
            statePayload: null!,
            generation: -2,
            time: double.NaN,
            dx: double.PositiveInfinity,
            dy: double.NegativeInfinity);

        Assert.Equal(0, mob.Index);
        Assert.Equal(0.0, mob.X);
        Assert.Equal(0.0, mob.Y);
        Assert.Equal(0, mob.Life);
        Assert.Equal(0, mob.MaxLife);
        Assert.Equal(string.Empty, mob.AnimPayload);
        Assert.Equal(string.Empty, mob.Type);
        Assert.Equal(string.Empty, mob.StatePayload);
        Assert.Equal(0.0, mob.Time);
        Assert.Equal(0.0, mob.Dx);
        Assert.Equal(0.0, mob.Dy);
    }

    [Fact]
    public void MobMoveSnapshot_SanitizesNonFiniteCoordinatesAndVelocities()
    {
        var move = new NetNode.MobMoveSnapshot(
            index: -1,
            x: double.NaN,
            y: double.PositiveInfinity,
            dir: 1,
            animPayload: null!,
            generation: -3,
            time: double.NaN,
            dx: double.NegativeInfinity,
            dy: double.NaN);

        Assert.Equal(0, move.Index);
        Assert.Equal(0.0, move.X);
        Assert.Equal(0.0, move.Y);
        Assert.Equal(0.0, move.Time);
        Assert.Equal(0.0, move.Dx);
        Assert.Equal(0.0, move.Dy);
        Assert.Equal(-3, move.Generation);
        Assert.Equal(string.Empty, move.AnimPayload);
    }

    [Fact]
    public void MobHitAndDie_SanitizesCoordinatesAndCounts()
    {
        var hit = new NetNode.MobHit(
            userId: -1,
            mobIndex: -2,
            hp: -100,
            x: double.NaN,
            y: double.NegativeInfinity,
            type: null!,
            generation: -1,
            damageHint: double.NaN);

        Assert.Equal(-1, hit.UserId);
        Assert.Equal(0, hit.MobIndex);
        Assert.Equal(0, hit.Hp);
        Assert.Equal(0.0, hit.X);
        Assert.Equal(0.0, hit.Y);
        Assert.Equal(-1, hit.Generation);
        Assert.Equal(string.Empty, hit.Type);
        Assert.Equal(0.0, hit.DamageHint);

        var die = new NetNode.MobDie(
            userId: -5,
            mobIndex: -10,
            x: double.PositiveInfinity,
            y: double.NaN,
            generation: -2,
            type: null!);

        Assert.Equal(-5, die.UserId);
        Assert.Equal(0, die.MobIndex);
        Assert.Equal(0.0, die.X);
        Assert.Equal(0.0, die.Y);
        Assert.Equal(-2, die.Generation);
        Assert.Equal(string.Empty, die.Type);
    }

    [Fact]
    public void MobAttack_SanitizesNonFiniteCoordinates()
    {
        var attack = new NetNode.MobAttack(
            index: -1,
            skillId: null!,
            requiresTargetInArea: true,
            data: null,
            x: double.NaN,
            y: double.PositiveInfinity,
            targetUserId: -1,
            dir: 1,
            blockSeconds: double.NaN,
            forcedDirSeconds: double.NegativeInfinity,
            type: null!,
            generation: -4);

        Assert.Equal(0, attack.Index);
        Assert.Equal(string.Empty, attack.SkillId);
        Assert.Equal(0.0, attack.X);
        Assert.Equal(0.0, attack.Y);
        Assert.Equal(-1, attack.TargetUserId);
        Assert.Equal(0.0, attack.BlockSeconds);
        Assert.Equal(0.0, attack.ForcedDirSeconds);
        Assert.Equal(string.Empty, attack.Type);
        Assert.Equal(-4, attack.Generation);
    }

    [Fact]
    public void PlayerDownState_SanitizesHeadCoordinates()
    {
        var down = new NetNode.PlayerDownState(
            userId: -1,
            isDowned: true,
            x: double.NaN,
            y: double.NegativeInfinity,
            levelId: null!,
            hasHeadPosition: true,
            headX: double.PositiveInfinity,
            headY: double.NaN,
            hasHeadAnim: true,
            headAnim: null!);

        Assert.Equal(-1, down.UserId);
        Assert.Equal(0.0, down.X);
        Assert.Equal(0.0, down.Y);
        Assert.Equal(string.Empty, down.LevelId);
        Assert.Equal(0.0, down.HeadX);
        Assert.Equal(0.0, down.HeadY);
        Assert.Equal(string.Empty, down.HeadAnim);
    }

    [Fact]
    public void RemoteAffectEvent_ClampsDurationAndSanitizesNonFinite()
    {
        var affect = new NetNode.RemoteAffectEvent(
            userId: -2,
            affectId: -5,
            sec: double.NaN,
            isRemove: false);

        Assert.Equal(-2, affect.UserId);
        Assert.Equal(-5, affect.AffectId);
        Assert.Equal(0.0, affect.Sec);

        var affectNeg = new NetNode.RemoteAffectEvent(1, 2, -10.5, false);
        Assert.Equal(0.0, affectNeg.Sec);

        var affectInf = new NetNode.RemoteAffectEvent(1, 2, double.PositiveInfinity, false);
        Assert.Equal(0.0, affectInf.Sec);
    }

    [Fact]
    public void InterSyncTypes_SanitizesCoordinatesAndNullStrings()
    {
        var door = new InterDoorEvent(-1, double.NaN, double.PositiveInfinity, null!, true, null!);
        Assert.Equal(0, door.UserId);
        Assert.Equal(0.0, door.X);
        Assert.Equal(0.0, door.Y);
        Assert.Equal(string.Empty, door.Action);
        Assert.Equal(string.Empty, door.LevelId);

        var elev = new InterElevatorEvent(-1, double.NegativeInfinity, double.NaN, -5, null!);
        Assert.Equal(0, elev.UserId);
        Assert.Equal(0.0, elev.X);
        Assert.Equal(0.0, elev.Y);
        Assert.Equal(0L, elev.Sequence);
        Assert.Equal(string.Empty, elev.LevelId);

        var elevState = new InterElevatorStateEvent(
            userId: -1,
            anchorX: double.NaN,
            anchorY: double.PositiveInfinity,
            sequence: -10,
            platformX: double.NegativeInfinity,
            platformY: double.NaN,
            moving: true,
            levelId: null!);
        Assert.Equal(0, elevState.UserId);
        Assert.Equal(0.0, elevState.AnchorX);
        Assert.Equal(0.0, elevState.AnchorY);
        Assert.Equal(0L, elevState.Sequence);
        Assert.Equal(0.0, elevState.PlatformX);
        Assert.Equal(0.0, elevState.PlatformY);
        Assert.Equal(string.Empty, elevState.LevelId);

        var chest = new InterTreasureChestEvent(double.NaN, double.NegativeInfinity, null!);
        Assert.Equal(0.0, chest.X);
        Assert.Equal(0.0, chest.Y);
        Assert.Equal(string.Empty, chest.LevelId);

        var teleport = new InterTeleportEvent(double.PositiveInfinity, double.NaN, null!);
        Assert.Equal(0.0, teleport.X);
        Assert.Equal(0.0, teleport.Y);
        Assert.Equal(string.Empty, teleport.LevelId);

        var bossHeroTele = new BossHeroTeleportEvent(-1, double.NaN, double.PositiveInfinity, 1);
        Assert.Equal(0, bossHeroTele.UserId);
        Assert.Equal(0.0, bossHeroTele.X);
        Assert.Equal(0.0, bossHeroTele.Y);

        var bossRuneCells = new InterBossRuneUpdateCellsEvent(double.NaN, double.NegativeInfinity, true, null!);
        Assert.Equal(0.0, bossRuneCells.X);
        Assert.Equal(0.0, bossRuneCells.Y);
        Assert.Equal(string.Empty, bossRuneCells.LevelId);
    }

    [Fact]
    public void MobWireBinary_TryBuildAndParse_HandlesCorruptPayloadsSafely()
    {
        // Null or empty payload
        var dest = new List<NetNode.MobStateSnapshot>();
        Assert.False(MobWireBinary.TryParseMobStatesBinary(null!, dest));
        Assert.False(MobWireBinary.TryParseMobStatesBinary(Array.Empty<byte>(), dest));
        Assert.False(MobWireBinary.TryParseMobStatesBase64(null!, dest));
        Assert.False(MobWireBinary.TryParseMobStatesBase64(string.Empty, dest));
        Assert.False(MobWireBinary.TryParseMobStatesBase64("   ", dest));
        Assert.False(MobWireBinary.TryParseMobStatesBase64("NotValidBase64!!!", dest));

        // Truncated header (< 3 bytes)
        Assert.False(MobWireBinary.TryParseMobStatesBinary(new byte[] { 1 }, dest));
        Assert.False(MobWireBinary.TryParseMobStatesBinary(new byte[] { 1, 0 }, dest));

        // Wrong version
        Assert.False(MobWireBinary.TryParseMobStatesBinary(new byte[] { 99, 1, 0 }, dest));

        // Oversized state count > 8192
        var oversizedCountHeader = new byte[] { 1, 0xFF, 0x7F }; // count = 32767 > 8192
        Assert.False(MobWireBinary.TryParseMobStatesBinary(oversizedCountHeader, dest));

        // Negative state count (bit 15 set in short)
        var negativeCountHeader = new byte[] { 1, 0x00, 0x80 }; // count = -32768
        Assert.False(MobWireBinary.TryParseMobStatesBinary(negativeCountHeader, dest));
    }

    [Fact]
    public void MobWireBinary_TryBuildAndParse_RoundtripsAndSanitizesNonFiniteCoordinates()
    {
        var inputStates = new List<NetNode.MobStateSnapshot>
        {
            new NetNode.MobStateSnapshot(
                index: 10,
                x: double.NaN, // Non-finite coordinate
                y: 250.5,
                dir: -1,
                life: 100,
                maxLife: 200,
                animPayload: "walk",
                type: "Zombie",
                statePayload: "PrisonersQuarters",
                generation: 1,
                time: 0.5,
                dx: double.PositiveInfinity,
                dy: -1.5)
        };

        Assert.True(MobWireBinary.TryBuildMobStatesBinary(inputStates, out var bytes));
        Assert.NotNull(bytes);

        var outputStates = new List<NetNode.MobStateSnapshot>();
        Assert.True(MobWireBinary.TryParseMobStatesBinary(bytes, outputStates));
        Assert.Single(outputStates);

        var parsed = outputStates[0];
        Assert.Equal(10, parsed.Index);
        Assert.Equal(0.0, parsed.X); // NaN sanitized to 0.0
        Assert.Equal(250.5, parsed.Y);
        Assert.Equal(-1, parsed.Dir);
        Assert.Equal(100, parsed.Life);
        Assert.Equal(200, parsed.MaxLife);
        Assert.Equal("walk", parsed.AnimPayload);
        Assert.Equal("Zombie", parsed.Type);
        Assert.Equal("PrisonersQuarters", parsed.StatePayload);
        Assert.Equal(1, parsed.Generation);
        Assert.Equal(0.0, parsed.Dx); // PositiveInfinity sanitized to 0.0
        Assert.Equal(-1.5, parsed.Dy);
    }

    [Fact]
    public void MobWireBinary_TryParseMobStatesBinary_DetectsTruncatedRecordData()
    {
        var inputStates = new List<NetNode.MobStateSnapshot>
        {
            new NetNode.MobStateSnapshot(1, 10.0, 20.0, 1, 50, 50, "idle", "Zombie", "Level1", 1, 0.0, 0.0, 0.0)
        };

        Assert.True(MobWireBinary.TryBuildMobStatesBinary(inputStates, out var bytes));
        Assert.NotNull(bytes);

        // Truncate half of the byte buffer
        var truncated = new byte[bytes.Length / 2];
        Array.Copy(bytes, truncated, truncated.Length);

        var dest = new List<NetNode.MobStateSnapshot>();
        Assert.False(MobWireBinary.TryParseMobStatesBinary(truncated, dest));
        Assert.Empty(dest);
    }

    [Fact]
    public void MobWireCodec_BuildMobEventsLine_SanitizesNonFiniteDoubles()
    {
        var updates = new List<NetNode.MobEventUpdate>
        {
            new NetNode.MobEventUpdate(
                index: 1,
                x: double.NaN,
                y: double.PositiveInfinity,
                dir: 1,
                events: new List<string> { "hit|10|25" },
                type: "Zombie",
                generation: 2)
        };

        var line = MobWireCodec.BuildMobEventsLine(updates);
        Assert.StartsWith("MOBEVENT|", line);
        Assert.DoesNotContain("NaN", line);
        Assert.DoesNotContain("Infinity", line);
    }

    [Theory]
    // Host alone -> 1
    [InlineData(true, 1, false, new int[] { 1 }, new int[] { }, new int[] { 1 }, 1)]
    // Host + 1 client (alive, fresh) -> 2
    [InlineData(true, 1, false, new int[] { 1, 2 }, new int[] { }, new int[] { 1, 2 }, 2)]
    // Host + 1 client (downed) -> 1
    [InlineData(true, 1, false, new int[] { 1, 2 }, new int[] { 2 }, new int[] { 1, 2 }, 1)]
    // Host + 1 client (stale/disconnected) -> 1
    [InlineData(true, 1, false, new int[] { 1, 2 }, new int[] { }, new int[] { 1 }, 1)]
    // Host downed + 1 client (alive, fresh) -> 1
    [InlineData(true, 1, true, new int[] { 1, 2 }, new int[] { }, new int[] { 1, 2 }, 1)]
    // Host downed + 1 client (downed) -> 1 (min 1)
    [InlineData(true, 1, true, new int[] { 1, 2 }, new int[] { 2 }, new int[] { 1, 2 }, 1)]
    // Host + 2 clients (1 alive, 1 stale) -> 2
    [InlineData(true, 1, false, new int[] { 1, 2, 3 }, new int[] { }, new int[] { 1, 2 }, 2)]
    // Host + 2 clients (1 alive, 1 downed) -> 2
    [InlineData(true, 1, false, new int[] { 1, 2, 3 }, new int[] { 3 }, new int[] { 1, 2, 3 }, 2)]
    // Host + 2 clients (both stale) -> 1
    [InlineData(true, 1, false, new int[] { 1, 2, 3 }, new int[] { }, new int[] { 1 }, 1)]
    // Client alone (host disconnected) -> 1
    [InlineData(false, 2, false, new int[] { 2 }, new int[] { }, new int[] { 2 }, 1)]
    // Client + Host alive -> 2
    [InlineData(false, 2, false, new int[] { 1, 2 }, new int[] { }, new int[] { 1, 2 }, 2)]
    // Client + Host downed -> 1
    [InlineData(false, 2, false, new int[] { 1, 2 }, new int[] { 1 }, new int[] { 1, 2 }, 1)]
    // Client downed + Host alive -> 1
    [InlineData(false, 2, true, new int[] { 1, 2 }, new int[] { }, new int[] { 1, 2 }, 1)]
    public void CombatSyncHelpers_ComputeExpectedExitPlayerCount_HandlesDownedAndStalePeers(
        bool isHost,
        int localId,
        bool isLocalDowned,
        int[] activePeerIds,
        int[] downedPeerIds,
        int[] freshPeerIds,
        int expected)
    {
        var downedSet = new HashSet<int>(downedPeerIds);
        var freshSet = new HashSet<int>(freshPeerIds);

        var result = CombatSyncHelpers.ComputeExpectedExitPlayerCount(
            isHost,
            localId,
            isLocalDowned,
            activePeerIds,
            peerId => downedSet.Contains(peerId),
            peerId => freshSet.Contains(peerId));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ProtocolTypes_SanitizesNegativeIdsAndNonFiniteFloats()
    {
        var draw = new NetNode.MobDraw(-5, -10, true, true, -1);
        Assert.Equal(0, draw.UserId);
        Assert.Equal(0, draw.MobIndex);

        var ready = new NetNode.ExitReadyState(-2, -5, -8, true, true, false, true, null);
        Assert.Equal(0, ready.UserId);
        Assert.Equal(0, ready.DoorCx);
        Assert.Equal(0, ready.DoorCy);
        Assert.Equal(string.Empty, ready.LevelId);

        var down = new NetNode.PlayerDownState(
            userId: -1,
            isDowned: true,
            x: double.NaN,
            y: double.PositiveInfinity,
            levelId: null!,
            hasHeadPosition: true,
            headX: double.NegativeInfinity,
            headY: double.NaN,
            hasHeadAnim: true,
            headAnim: null);
        Assert.Equal(-1, down.UserId);
        Assert.Equal(0.0, down.X);
        Assert.Equal(0.0, down.Y);
        Assert.Equal(0.0, down.HeadX);
        Assert.Equal(0.0, down.HeadY);
        Assert.Equal(string.Empty, down.LevelId);
        Assert.Equal(string.Empty, down.HeadAnim);

        var anchor = new NetNode.HostSpawnAnchor(-10, -20, null);
        Assert.Equal(0, anchor.Cx);
        Assert.Equal(0, anchor.Cy);
        Assert.Equal(string.Empty, anchor.LevelId);

        var commit = new NetNode.ExitTransitionCommit(-100, -1, -2, null, null);
        Assert.Equal(0L, commit.Sequence);
        Assert.Equal(0, commit.DoorCx);
        Assert.Equal(0, commit.DoorCy);
        Assert.Equal(string.Empty, commit.FromLevelId);
        Assert.Equal(string.Empty, commit.DestinationLevelId);

        var revive = new NetNode.PlayerReviveRequest(-1, -2);
        Assert.Equal(0, revive.ReviverId);
        Assert.Equal(0, revive.TargetId);

        var affect = new NetNode.RemoteAffectEvent(-5, -10, double.NaN, false);
        Assert.Equal(-5, affect.UserId);
        Assert.Equal(-10, affect.AffectId);
        Assert.Equal(0.0, affect.Sec);
    }

    [Fact]
    public void CombatSyncHelpers_IsSolidTile_BoundsAndNullSafety()
    {
        // Zero pointer returns false
        Assert.False(CombatSyncHelpers.IsSolidTile(IntPtr.Zero, 10, 10, 5, 5));
        Assert.False(CombatSyncHelpers.IsSolidTile(IntPtr.Zero, 0, 0, 0, 0));

        // Out of bounds coordinates count as solid walls
        Assert.True(CombatSyncHelpers.IsSolidTile(new IntPtr(12345), 10, 10, -1, 5));
        Assert.True(CombatSyncHelpers.IsSolidTile(new IntPtr(12345), 10, 10, 10, 5));
        Assert.True(CombatSyncHelpers.IsSolidTile(new IntPtr(12345), 10, 10, 5, -1));
        Assert.True(CombatSyncHelpers.IsSolidTile(new IntPtr(12345), 10, 10, 5, 10));
    }

    [Fact]
    public void CombatSyncHelpers_IsWoodTile_BoundsAndNullSafety()
    {
        // Zero pointer returns false
        Assert.False(CombatSyncHelpers.IsWoodTile(IntPtr.Zero, 10, 10, 5, 5));
        Assert.False(CombatSyncHelpers.IsWoodTile(IntPtr.Zero, 0, 0, 0, 0));

        // Out of bounds coordinates return false
        Assert.False(CombatSyncHelpers.IsWoodTile(new IntPtr(12345), 10, 10, -1, 5));
        Assert.False(CombatSyncHelpers.IsWoodTile(new IntPtr(12345), 10, 10, 10, 5));
        Assert.False(CombatSyncHelpers.IsWoodTile(new IntPtr(12345), 10, 10, 5, -1));
        Assert.False(CombatSyncHelpers.IsWoodTile(new IntPtr(12345), 10, 10, 5, 10));
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1, 1, true)]
    [InlineData(5, 5, true)]
    [InlineData(0, 5, true)]
    [InlineData(5, 0, true)]
    [InlineData(-1, 0, false)]
    [InlineData(0, -1, false)]
    [InlineData(6, 0, false)]
    [InlineData(0, 6, false)]
    [InlineData(10, 10, false)]
    public void BossStemCell_RunLaunchDescriptor_BoundsCheck(int difficulty, int bossCells, bool expectedValid)
    {
        var descriptor = new DeadCellsMultiplayerMod.PortableCore.RunLaunchDescriptor(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            RunId: 10,
            Sequence: 1,
            ProtocolVersion: 17,
            RunSeed: 99999,
            LaunchKind: "normal",
            InitialLevelId: "PrisonStart",
            InitialLevelSeed: 100.0,
            Difficulty: difficulty,
            BossCells: bossCells,
            BossRush: false,
            DlcFlags: 0,
            SaveSlot: 1,
            BossRushSeed: 0,
            LevelGenSeed: 0,
            BossRushTier: 0,
            Route: string.Empty,
            BossSequence: string.Empty,
            Modifiers: 0,
            TargetArena: "PrisonStart");

        var isValid = descriptor.TryValidate(out var error);
        Assert.Equal(expectedValid, isValid);
        if (!expectedValid)
        {
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
    }

    [Theory]
    [InlineData("0", 0, 5)]
    [InlineData("1", 1, 5)]
    [InlineData("3", 3, 5)]
    [InlineData("5", 5, 5)]
    [InlineData("-1", 0, 5)]
    [InlineData("-99", 0, 5)]
    [InlineData("6", 5, 5)]
    [InlineData("99", 5, 5)]
    [InlineData("3|4", 3, 4)]
    [InlineData("2|2", 2, 2)]
    [InlineData("5|2", 2, 2)] // Clamped to host's unlocked level 2
    [InlineData("4|0", 0, 0)] // Clamped to host's unlocked level 0
    [InlineData("-2|3", 0, 3)]
    [InlineData("10|10", 5, 5)] // Clamped to absolute max 5
    public void BossStemCell_TryParseBossRunePayload_BoundsAndHostLimitValidation(string payload, int expectedBC, int expectedMax)
    {
        var parsed = BossStemCellSync.TryParseBossRunePayload(payload, out var bc, out var maxAllowed);
        Assert.True(parsed);
        Assert.Equal(expectedBC, bc);
        Assert.Equal(expectedMax, maxAllowed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid_payload")]
    [InlineData("not|numbers")]
    public void BossStemCell_TryParseBossRunePayload_HandlesMalformedSafely(string? payload)
    {
        var parsed = BossStemCellSync.TryParseBossRunePayload(payload, out var bc, out var maxAllowed);
        Assert.False(parsed);
        Assert.Equal(0, bc);
        Assert.Equal(BossStemCellSync.MaxBossCells, maxAllowed);
    }

    [Theory]
    [InlineData("PrisonStart", true)]
    [InlineData("prisonstart", true)]
    [InlineData("PRISONSTART", true)]
    [InlineData("Prison_Start", false)]
    [InlineData("Ossuary", false)]
    [InlineData("Promenade", false)]
    [InlineData("ClockTower", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void BossStemCell_IsStartingHubLevel_Validation(string? levelId, bool expectedHub)
    {
        var isHub = BossStemCellSync.IsStartingHubLevel(levelId);
        Assert.Equal(expectedHub, isHub);
    }

    [Fact]
    public void BossStemCell_InterBossRuneUpdateCellsEvent_Validation()
    {
        var ev = new InterBossRuneUpdateCellsEvent(double.NaN, double.NegativeInfinity, true, null!);
        Assert.Equal(0, ev.UserId);
        Assert.Equal(0.0, ev.X);
        Assert.Equal(0.0, ev.Y);
        Assert.True(ev.Add);
        Assert.Equal(string.Empty, ev.LevelId);

        var evWithUser = new InterBossRuneUpdateCellsEvent(-5, 128.5, 256.0, false, "PrisonStart");
        Assert.Equal(0, evWithUser.UserId);
        Assert.Equal(128.5, evWithUser.X);
        Assert.Equal(256.0, evWithUser.Y);
        Assert.False(evWithUser.Add);
        Assert.Equal("PrisonStart", evWithUser.LevelId);
    }

    [Fact]
    public void BossStemCell_ClampBossCells_BoundsAndAuthority()
    {
        // Out of bounds values clamped to [0..5]
        Assert.Equal(0, BossStemCellSync.ClampBossCells(-10, 5));
        Assert.Equal(5, BossStemCellSync.ClampBossCells(10, 5));
        Assert.Equal(0, BossStemCellSync.ClampBossCells(0, 5));
        Assert.Equal(5, BossStemCellSync.ClampBossCells(5, 5));

        // Clamped against host's unlocked ceiling
        Assert.Equal(2, BossStemCellSync.ClampBossCells(5, 2));
        Assert.Equal(0, BossStemCellSync.ClampBossCells(3, 0));
        Assert.Equal(1, BossStemCellSync.ClampBossCells(1, 2));

        // Clamping handles out-of-range maxAllowed safely
        Assert.Equal(0, BossStemCellSync.ClampBossCells(5, -1));
        Assert.Equal(5, BossStemCellSync.ClampBossCells(10, 10));
    }

    [Theory]
    [InlineData("PrisonStart/", true)]
    [InlineData("PrisonStart\\", true)]
    [InlineData("PrisonStart\0", true)]
    [InlineData("  PrisonStart  ", true)]
    [InlineData("PrisonStart/\0", true)]
    [InlineData("Promenade/", false)]
    [InlineData("Ossuary\\", false)]
    public void BossStemCell_IsStartingHubLevel_SanitizesProtocolArtifacts(string? levelId, bool expectedHub)
    {
        var isHub = BossStemCellSync.IsStartingHubLevel(levelId);
        Assert.Equal(expectedHub, isHub);
    }

    [Fact]
    public void BossStemCell_TryParseInterBossRuneUpdateCellsPayload_BothFormats()
    {
        // 4-part legacy format
        var parsed4 = NetNode.TryParseInterBossRuneUpdateCellsPayload("120.0|240.5|1|PrisonStart", out var ev4);
        Assert.True(parsed4);
        Assert.Equal(0, ev4.UserId);
        Assert.Equal(120.0, ev4.X);
        Assert.Equal(240.5, ev4.Y);
        Assert.True(ev4.Add);
        Assert.Equal("PrisonStart", ev4.LevelId);

        // 5-part format with explicit userId
        var parsed5 = NetNode.TryParseInterBossRuneUpdateCellsPayload("3|120.0|240.5|0|PrisonStart", out var ev5);
        Assert.True(parsed5);
        Assert.Equal(3, ev5.UserId);
        Assert.Equal(120.0, ev5.X);
        Assert.Equal(240.5, ev5.Y);
        Assert.False(ev5.Add);
        Assert.Equal("PrisonStart", ev5.LevelId);

        // Malformed coordinate payloads rejected safely
        Assert.False(NetNode.TryParseInterBossRuneUpdateCellsPayload("NaN|240.5|1|PrisonStart", out _));
        Assert.False(NetNode.TryParseInterBossRuneUpdateCellsPayload("120.0|Infinity|1|PrisonStart", out _));
        Assert.False(NetNode.TryParseInterBossRuneUpdateCellsPayload("not|a|number", out _));
        Assert.False(NetNode.TryParseInterBossRuneUpdateCellsPayload(string.Empty, out _));
    }

    [Fact]
    public void BossStemCell_NetNode_HostAuthorityRejection()
    {
        var hostNode = new NetNode(1, NetRole.Host);

        // Host rejects client-originated BOSSRUNE packet
        var handledBossRune = hostNode.HandleLine("BOSSRUNE|5", senderId: 2, out var forwardLine1);
        Assert.True(handledBossRune);
        Assert.Null(forwardLine1);

        // Host rejects client-originated BOSSRUNE_UPDATE_CELLS packet
        var handledUpdate = hostNode.HandleLine("BOSSRUNE_UPDATE_CELLS|100|200|1|PrisonStart", senderId: 2, out var forwardLine2);
        Assert.True(handledUpdate);
        Assert.Null(forwardLine2);
    }

    [Fact]
    public void BossStemCell_NetNode_ClientRejectsDirectSendBossRune()
    {
        var clientNode = new NetNode(2, NetRole.Client);

        // Non-host calling SendBossRune or SendInterBossRuneUpdateCells does not throw and is dropped
        clientNode.SendBossRune(5, 5);
        clientNode.SendInterBossRuneUpdateCells(100.0, 200.0, true, "PrisonStart");
    }

    [Fact]
    public void BossStemCell_GameDataSync_HostMaxUnlockedTrackingAndClamping()
    {
        // Clean initial state
        GameDataSync.ResetBossRuneStateForTesting();
        Assert.False(GameDataSync.HasRemoteBossRune());

        // Receive dual payload with host max unlocked 3
        GameDataSync.ReceiveBossRune("5|3");
        Assert.True(GameDataSync.HasRemoteBossRune());
        Assert.True(GameDataSync.TryGetRemoteBossRune(out var bc));
        Assert.Equal(3, bc); // Clamped to host unlocked 3

        Assert.True(GameDataSync.TryGetHostMaxUnlockedBossRune(out var hostMax));
        Assert.Equal(3, hostMax);

        // Legacy single-integer payload with BC 5 should be clamped against host's unlocked 3
        GameDataSync.ReceiveBossRune("5");
        Assert.True(GameDataSync.TryGetRemoteBossRune(out bc));
        Assert.Equal(3, bc);

        // Valid lower BC within range updates correctly
        GameDataSync.ReceiveBossRune("1");
        Assert.True(GameDataSync.TryGetRemoteBossRune(out bc));
        Assert.Equal(1, bc);

        // Clean-up on session restore
        GameDataSync.ResetBossRuneStateForTesting();
        Assert.False(GameDataSync.HasRemoteBossRune());
        Assert.False(GameDataSync.TryGetRemoteBossRune(out _));
    }

    [Theory]
    [InlineData(0, 5, true)]
    [InlineData(3, 5, true)]
    [InlineData(5, 5, true)]
    [InlineData(2, 2, true)]
    [InlineData(3, 2, false)]
    [InlineData(-1, 5, false)]
    [InlineData(6, 5, false)]
    [InlineData(0, -1, false)]
    [InlineData(0, 6, true)] // maxAllowed clamped to 5, bc 0 is valid
    public void BossStemCell_IsValidBossCells_Validation(int bc, int maxAllowed, bool expectedValid)
    {
        var isValid = BossStemCellSync.IsValidBossCells(bc, maxAllowed);
        Assert.Equal(expectedValid, isValid);
    }

    [Theory]
    [InlineData(0, 5, "0|5")]
    [InlineData(3, 3, "3|3")]
    [InlineData(5, 5, "5|5")]
    [InlineData(-1, 5, "0|5")]
    [InlineData(10, 5, "5|5")]
    [InlineData(5, 2, "2|2")]
    [InlineData(3, -1, "0|0")]
    public void BossStemCell_FormatBossRunePayload_FormattingAndClamping(int bc, int maxAllowed, string expectedPayload)
    {
        var payload = BossStemCellSync.FormatBossRunePayload(bc, maxAllowed);
        Assert.Equal(expectedPayload, payload);
    }

    [Theory]
    [InlineData(true, "PrisonStart", true)]
    [InlineData(true, "prisonstart", true)]
    [InlineData(true, "PrisonStart/", true)]
    [InlineData(true, "Promenade", false)]
    [InlineData(true, "Ossuary", false)]
    [InlineData(true, "", false)]
    [InlineData(true, null, false)]
    [InlineData(false, "PrisonStart", false)]
    [InlineData(false, "Promenade", false)]
    public void BossStemCell_CanModifyBossCells_Validation(bool isHost, string? levelId, bool expectedCanModify)
    {
        var canModify = BossStemCellSync.CanModifyBossCells(isHost, levelId);
        Assert.Equal(expectedCanModify, canModify);
    }

    [Fact]
    public void BossStemCell_InterBossRuneUpdateCellsEvent_ExtendedFieldsAndClamping()
    {
        var ev = new InterBossRuneUpdateCellsEvent(1, 100.0, 200.0, true, "PrisonStart", 4, 3);
        Assert.Equal(1, ev.UserId);
        Assert.Equal(100.0, ev.X);
        Assert.Equal(200.0, ev.Y);
        Assert.True(ev.Add);
        Assert.Equal("PrisonStart", ev.LevelId);
        Assert.Equal(3, ev.TargetBossCells); // Clamped to maxAllowed 3
        Assert.Equal(3, ev.MaxAllowedBossCells);

        var evNegative = new InterBossRuneUpdateCellsEvent(0, 50.0, 60.0, false, "PrisonStart", -2, -1);
        Assert.Equal(0, evNegative.TargetBossCells);
        Assert.Equal(0, evNegative.MaxAllowedBossCells);

        var evOverflow = new InterBossRuneUpdateCellsEvent(0, 50.0, 60.0, false, "PrisonStart", 10, 10);
        Assert.Equal(5, evOverflow.TargetBossCells);
        Assert.Equal(5, evOverflow.MaxAllowedBossCells);
    }

    [Fact]
    public void BossStemCell_TryParseInterBossRuneUpdateCellsPayload_ExtendedFormats()
    {
        // 5-part without uid: x | y | add | levelId | targetBc
        var parsed5 = NetNode.TryParseInterBossRuneUpdateCellsPayload("120.0|240.5|1|PrisonStart|2", out var ev5);
        Assert.True(parsed5);
        Assert.Equal(0, ev5.UserId);
        Assert.Equal(120.0, ev5.X);
        Assert.Equal(240.5, ev5.Y);
        Assert.True(ev5.Add);
        Assert.Equal("PrisonStart", ev5.LevelId);
        Assert.Equal(2, ev5.TargetBossCells);
        Assert.Null(ev5.MaxAllowedBossCells);

        // 6-part without uid: x | y | add | levelId | targetBc | maxAllowed
        var parsed6 = NetNode.TryParseInterBossRuneUpdateCellsPayload("120.0|240.5|1|PrisonStart|4|3", out var ev6);
        Assert.True(parsed6);
        Assert.Equal(0, ev6.UserId);
        Assert.Equal(3, ev6.TargetBossCells); // Clamped to maxAllowed 3
        Assert.Equal(3, ev6.MaxAllowedBossCells);

        // 7-part with uid: uid | x | y | add | levelId | targetBc | maxAllowed
        var parsed7 = NetNode.TryParseInterBossRuneUpdateCellsPayload("2|120.0|240.5|0|PrisonStart|5|4", out var ev7);
        Assert.True(parsed7);
        Assert.Equal(2, ev7.UserId);
        Assert.False(ev7.Add);
        Assert.Equal(4, ev7.TargetBossCells); // Clamped to maxAllowed 4
        Assert.Equal(4, ev7.MaxAllowedBossCells);
    }

    [Fact]
    public void BossStemCell_GameDataSync_ExplicitMaxAllowsIncrease()
    {
        GameDataSync.ResetBossRuneStateForTesting();

        // Initial sync: host max is 2, BC is 2
        GameDataSync.ReceiveBossRune("2|2");
        Assert.True(GameDataSync.TryGetRemoteBossRune(out var bc));
        Assert.Equal(2, bc);
        Assert.True(GameDataSync.TryGetHostMaxUnlockedBossRune(out var max));
        Assert.Equal(2, max);

        // Host unlocks cell 3 and syncs 3|3
        GameDataSync.ReceiveBossRune("3|3");
        Assert.True(GameDataSync.TryGetRemoteBossRune(out bc));
        Assert.Equal(3, bc);
        Assert.True(GameDataSync.TryGetHostMaxUnlockedBossRune(out max));
        Assert.Equal(3, max);

        // Legacy packet without explicit max should preserve the learned host max 3
        GameDataSync.ReceiveBossRune("5");
        Assert.True(GameDataSync.TryGetRemoteBossRune(out bc));
        Assert.Equal(3, bc);

        GameDataSync.ResetBossRuneStateForTesting();
    }

    [Fact]
    public void BossStemCell_TryParseInterBossRuneUpdateCellsPayload_Legacy3Part()
    {
        var parsed3 = NetNode.TryParseInterBossRuneUpdateCellsPayload("120.0|240.5|1", out var ev3);
        Assert.True(parsed3);
        Assert.Equal(0, ev3.UserId);
        Assert.Equal(120.0, ev3.X);
        Assert.Equal(240.5, ev3.Y);
        Assert.True(ev3.Add);
        Assert.Equal(string.Empty, ev3.LevelId);
        Assert.Null(ev3.TargetBossCells);
        Assert.Null(ev3.MaxAllowedBossCells);
    }

    [Fact]
    public void BossHpScaling_ResetAll_ExecutesSafelyWithoutException()
    {
        DeadCellsMultiplayerMod.Mobs.Bosses.BossHpScaling.ResetAll();
    }

    [Fact]
    public void BossStemCell_RunLaunchCommit_ReconcilesStaleRemoteBossRune()
    {
        RunLaunchCoordinator.ResetForTesting(NetRole.Client);
        GameDataSync.ResetBossRuneStateForTesting();

        try
        {
            // Simulate client having stale BC 1
            GameDataSync.ReceiveBossRune("1|1");
            Assert.True(GameDataSync.TryGetRemoteBossRune(out var bc));
            Assert.Equal(1, bc);

            // Host commits run launch with BC 3
            var descriptor = new DeadCellsMultiplayerMod.PortableCore.RunLaunchDescriptor(
                Guid.NewGuid(),
                RunId: 100,
                Sequence: 2,
                ProtocolVersion: DeadCellsMultiplayerMod.BuildInfo.NetworkProtocolVersion,
                RunSeed: 54321,
                LaunchKind: "normal",
                InitialLevelId: "PrisonStart",
                InitialLevelSeed: 123.0,
                Difficulty: 3,
                BossCells: 3,
                BossRush: false,
                DlcFlags: 0,
                SaveSlot: 0,
                BossRushSeed: 0,
                LevelGenSeed: 0,
                BossRushTier: 0,
                Route: string.Empty,
                BossSequence: string.Empty,
                Modifiers: 0,
                TargetArena: string.Empty);

            var commitWire = DeadCellsMultiplayerMod.RunLaunchWireCodec.BuildCommitLine(descriptor)["RUNCOMMIT|".Length..];
            LobbySession.ReceiveRunLaunchCommitPayload(commitWire);

            // Reconciled to descriptor's 3 BC
            Assert.True(GameDataSync.TryGetRemoteBossRune(out bc));
            Assert.Equal(3, bc);
        }
        finally
        {
            GameDataSync.ResetBossRuneStateForTesting();
            RunLaunchCoordinator.ResetForTesting(NetRole.None);
        }
    }

    [Theory]
    [InlineData(false, 0.0, 2.5, true)]   // Reviver is alive: always true
    [InlineData(false, 10.0, 2.5, true)]  // Reviver is alive: always true
    [InlineData(true, 0.0, 2.5, true)]    // Reviver just downed at same tick (0s): honor revive
    [InlineData(true, 0.5, 2.5, true)]    // Reviver downed 0.5s ago: honor revive
    [InlineData(true, 1.5, 2.5, true)]    // Reviver downed 1.5s ago: honor revive
    [InlineData(true, 2.5, 2.5, true)]    // Reviver downed exactly at boundary (2.5s): honor revive
    [InlineData(true, 2.6, 2.5, false)]   // Reviver downed 2.6s ago (> grace): reject stale revive
    [InlineData(true, 10.0, 2.5, false)]  // Reviver downed 10s ago: reject stale revive
    [InlineData(true, -0.5, 2.5, false)]  // Negative duration: invalid/reject
    [InlineData(true, 0.1, 0.0, false)]   // Zero grace configured: reject
    [InlineData(true, 0.1, -1.0, false)]  // Negative grace configured: reject
    public void CombatSyncHelpers_CanHonorSimultaneousRevive_HandlesRaceConditions(
        bool reviverIsDowned,
        double reviverDownedDurationSec,
        double simultaneousGraceSec,
        bool expectedResult)
    {
        var result = CombatSyncHelpers.CanHonorSimultaneousRevive(reviverIsDowned, reviverDownedDurationSec, simultaneousGraceSec);
        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public void CollectorForgeLedger_ResetLocks_IdempotentlyWipesAllLocksAndSequences()
    {
        var ledger = new CollectorForgeLedger();

        // Acquire Collector lock for user 1
        Assert.True(ledger.TryAcquireLock(1, "Collector", 1000, out var seq1));
        Assert.True(ledger.IsLockedByOther(2, "Collector", 1000, out var occupyingCollector));
        Assert.Equal(1, occupyingCollector);

        // Acquire Forge lock for user 2
        Assert.True(ledger.TryAcquireLock(2, "Forge", 1000, out var seq2));
        Assert.True(ledger.IsLockedByOther(1, "Forge", 1000, out var occupyingForge));
        Assert.Equal(2, occupyingForge);

        // First reset
        ledger.ResetLocks();

        Assert.False(ledger.IsLockedByOther(1, "Collector", 1000, out _));
        Assert.False(ledger.IsLockedByOther(2, "Collector", 1000, out _));
        Assert.False(ledger.IsLockedByOther(1, "Forge", 1000, out _));
        Assert.False(ledger.IsLockedByOther(2, "Forge", 1000, out _));

        // Re-acquisition after reset must succeed
        Assert.True(ledger.TryAcquireLock(3, "Collector", 1000, out var seq3));
        Assert.True(ledger.TryAcquireLock(3, "Forge", 1000, out var seq4));

        // Multiple idempotent resets in a row must succeed cleanly
        ledger.ResetLocks();
        ledger.ResetLocks();
        ledger.ResetLocks();

        Assert.False(ledger.IsLockedByOther(3, "Collector", 1000, out _));
        Assert.False(ledger.IsLockedByOther(3, "Forge", 1000, out _));
    }

    [Fact]
    public void ShopReservationManager_ClearAll_IdempotentlyWipesClaims()
    {
        var reservations = new ShopReservationManager();

        // Reserve an item
        Assert.True(reservations.TryClaim("PrisonStart", 100.0, 200.0, 1, 1000));
        Assert.True(reservations.IsClaimed("PrisonStart", 100.0, 200.0));

        // Clear all
        reservations.ClearAll();

        Assert.False(reservations.IsClaimed("PrisonStart", 100.0, 200.0));

        // Idempotent clears
        reservations.ClearAll();
        reservations.ClearAll();
        Assert.False(reservations.IsClaimed("PrisonStart", 100.0, 200.0));
    }

    [Fact]
    public void NetNode_ClearPendingStateForRunRestart_ClearsAllPendingQueuesIdempotently()
    {
        var node = new NetNode(1, NetRole.Host);

        // Populate various pending queues via HandleLine
        node.HandleLine("PDOWN|2|1|100|200|PrisonStart", senderId: 2, out _);
        node.HandleLine("PREVIVE|2|1", senderId: 2, out _);
        node.HandleLine("INTER_COLLECTOR_LOCK|2|1|Collector|1", senderId: 2, out _);
        node.HandleLine("INTER_COLLECTOR_DEPOSIT|2|1|Sword|5", senderId: 2, out _);
        node.HandleLine("INTER_BLUEPRINT_UNLOCK|2|1|Bow", senderId: 2, out _);
        node.HandleLine("INTER_FORGE_UPGRADE|2|1|1|10", senderId: 2, out _);

        // Client node for exit commit and boss rune cell updates
        var clientNode = new NetNode(2, NetRole.Client);
        clientNode.HandleLine("EXITCOMMIT|1|PrisonStart|Promenade|100|200", senderId: 1, out _);
        clientNode.HandleLine("BOSSRUNE_UPDATE_CELLS|100|200|1|PrisonStart", senderId: 1, out _);

        // Clear for run restart
        node.ClearPendingStateForRunRestart();
        clientNode.ClearPendingStateForRunRestart();

        // Assert host queues are completely cleared
        Assert.False(node.TryConsumePlayerDownStates(out _));
        Assert.False(node.TryConsumePlayerReviveRequests(out _));
        Assert.False(node.TryConsumeInterCollectorLockEvents(out _));
        Assert.False(node.TryConsumeInterCollectorDepositEvents(out _));
        Assert.False(node.TryConsumeInterBlueprintUnlockEvents(out _));
        Assert.False(node.TryConsumeInterForgeUpgradeEvents(out _));

        // Assert client queues are completely cleared
        Assert.False(clientNode.TryConsumeExitTransitionCommits(out _));
        Assert.False(clientNode.TryConsumeBossRuneUpdateCells(out _));

        // Idempotent calls must execute cleanly without exceptions
        node.ClearPendingStateForRunRestart();
        node.ClearPendingStateForRunRestart();
        clientNode.ClearPendingStateForRunRestart();

        Assert.False(node.TryConsumePlayerDownStates(out _));
        Assert.False(clientNode.TryConsumeExitTransitionCommits(out _));
    }
}



