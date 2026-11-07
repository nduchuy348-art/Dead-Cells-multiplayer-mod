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
}


