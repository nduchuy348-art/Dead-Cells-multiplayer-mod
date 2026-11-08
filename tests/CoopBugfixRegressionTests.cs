using System;
using DeadCellsMultiplayerMod.Ghost;
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
}



