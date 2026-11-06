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
}
