using System;
using DeadCellsMultiplayerMod.Ghost;
using DeadCellsMultiplayerMod.Mobs.MobsSynchronization;
using Xunit;

namespace DeadCellsMultiplayerMod.ProtocolReplay.Tests;

public sealed class SnapshotBufferAndCombatAssistedTests
{
    [Fact]
    public void HermiteInterpolation_EndpointsAndMidpoint()
    {
        double p0 = 100.0;
        double p1 = 200.0;
        double v0 = 0.0;
        double v1 = 0.0;

        // t = 0 must return p0
        Assert.Equal(p0, CombatSyncHelpers.HermiteInterpolate(p0, v0, p1, v1, 0.0, 1.0));
        Assert.Equal(p0, CombatSyncHelpers.HermiteInterpolate(p0, v0, p1, v1, -0.5, 1.0));

        // t = 1 must return p1
        Assert.Equal(p1, CombatSyncHelpers.HermiteInterpolate(p0, v0, p1, v1, 1.0, 1.0));
        Assert.Equal(p1, CombatSyncHelpers.HermiteInterpolate(p0, v0, p1, v1, 1.5, 1.0));

        // t = 0.5 with zero velocities must return midpoint
        double mid = CombatSyncHelpers.HermiteInterpolate(p0, v0, p1, v1, 0.5, 1.0);
        Assert.Equal(150.0, mid, 3);
    }

    [Fact]
    public void HermiteInterpolation_VelocityAffectsTrajectory()
    {
        double p0 = 100.0;
        double p1 = 200.0;

        // Positive initial velocity curves trajectory higher earlier
        double high = CombatSyncHelpers.HermiteInterpolate(p0, v0: 50.0, p1, v1: 0.0, t: 0.25, duration: 1.0);
        // Negative initial velocity curves trajectory lower earlier
        double low = CombatSyncHelpers.HermiteInterpolate(p0, v0: -50.0, p1, v1: 0.0, t: 0.25, duration: 1.0);

        Assert.True(high > low, "Positive initial tangent must produce higher position than negative tangent");
    }

    [Fact]
    public void MobSnapshotBuffer_Add_RingWrapAround()
    {
        var buffer = new MobSnapshotBuffer();
        Assert.Equal(0, buffer.Count);

        for (int i = 0; i < 15; i++)
        {
            buffer.Add(new MobSnapshot(
                receivedFrame: i * 2.0,
                x: 100.0 + i * 10.0,
                y: 50.0,
                dx: 1.0,
                dy: 0.0,
                dir: 1,
                life: 100,
                maxLife: 100,
                animPayload: "run",
                statePayload: ""));
        }

        // Count must not exceed Capacity (8)
        Assert.Equal(MobSnapshotBuffer.Capacity, buffer.Count);

        // Latest snapshot must be the 15th one (i = 14)
        Assert.True(buffer.TryGetLatest(out var latest));
        Assert.Equal(28.0, latest.ReceivedFrame);
        Assert.Equal(240.0, latest.X);

        // 1 older must be i = 13
        Assert.True(buffer.TryGetAt(1, out var prev));
        Assert.Equal(26.0, prev.ReceivedFrame);
        Assert.Equal(230.0, prev.X);
    }

    [Fact]
    public void MobSnapshotBuffer_SampleHermite_InterpolatesCorrectly()
    {
        var buffer = new MobSnapshotBuffer();
        buffer.Add(new MobSnapshot(10.0, x: 100.0, y: 50.0, dx: 0.0, dy: 0.0, dir: 1, life: 100, maxLife: 100, animPayload: "walk", statePayload: ""));
        buffer.Add(new MobSnapshot(20.0, x: 200.0, y: 50.0, dx: 0.0, dy: 0.0, dir: 1, life: 100, maxLife: 100, animPayload: "walk", statePayload: ""));

        bool sampled = buffer.SampleHermite(
            renderFrame: 15.0,
            out var interpX,
            out var interpY,
            out var dir,
            out var anim,
            out var snap,
            out var vertSnap);

        Assert.True(sampled);
        Assert.Equal(150.0, interpX, 1);
        Assert.Equal(50.0, interpY, 1);
        Assert.Equal(1, dir);
        Assert.Equal("walk", anim);
        Assert.False(snap);
        Assert.False(vertSnap);
    }

    [Fact]
    public void MobSnapshotBuffer_SampleHermite_Stationary_ZeroJitter()
    {
        var buffer = new MobSnapshotBuffer();
        // Mobs standing still across 5 snapshots
        for (int i = 0; i < 5; i++)
        {
            buffer.Add(new MobSnapshot(
                receivedFrame: 10.0 + i * 3.0,
                x: 142.5,
                y: 88.0,
                dx: 0.0,
                dy: 0.0,
                dir: -1,
                life: 50,
                maxLife: 50,
                animPayload: "idle",
                statePayload: ""));
        }

        // Sampling at any intermediate frame must return exact coordinates without jitter
        for (double f = 10.0; f <= 22.0; f += 0.5)
        {
            Assert.True(buffer.SampleHermite(f, out var x, out var y, out var dir, out var anim, out var snap, out _));
            Assert.Equal(142.5, x, 4);
            Assert.Equal(88.0, y, 4);
            Assert.Equal(-1, dir);
            Assert.Equal("idle", anim);
            Assert.False(snap);
        }
    }

    [Fact]
    public void MobSnapshotBuffer_SampleHermite_ForceSnap_SnapsDirectly()
    {
        var buffer = new MobSnapshotBuffer();
        buffer.Add(new MobSnapshot(10.0, x: 100.0, y: 50.0, dx: 0.0, dy: 0.0, dir: 1, life: 100, maxLife: 100, animPayload: "idle", statePayload: ""));
        buffer.Add(new MobSnapshot(20.0, x: 500.0, y: 50.0, dx: 0.0, dy: 0.0, dir: 1, life: 100, maxLife: 100, animPayload: "teleport", statePayload: "", forceSnap: true));

        Assert.True(buffer.SampleHermite(15.0, out var x, out var y, out var dir, out var anim, out var snap, out _));
        Assert.Equal(500.0, x);
        Assert.Equal(50.0, y);
        Assert.True(snap);
    }

    [Fact]
    public void MobSnapshotBuffer_SampleHermite_Discontinuity_TriggersSnap()
    {
        var buffer = new MobSnapshotBuffer();
        // Discontinuity of 400px > 240px threshold without explicit flag
        buffer.Add(new MobSnapshot(10.0, x: 100.0, y: 50.0, dx: 0.0, dy: 0.0, dir: 1, life: 100, maxLife: 100, animPayload: "idle", statePayload: ""));
        buffer.Add(new MobSnapshot(20.0, x: 500.0, y: 50.0, dx: 0.0, dy: 0.0, dir: 1, life: 100, maxLife: 100, animPayload: "idle", statePayload: "", forceSnap: false));

        Assert.True(buffer.SampleHermite(15.0, out var x, out _, out _, out _, out var snap, out _));
        Assert.Equal(500.0, x);
        Assert.False(snap); // Was not explicitly flagged forceSnap, but snapped to destination
    }

    [Fact]
    public void MobSnapshotBuffer_SampleHermite_ExtrapolatesGentlyWhenAhead()
    {
        var buffer = new MobSnapshotBuffer();
        // dx = 1.0 case fraction = 24.0 pixels/frame
        buffer.Add(new MobSnapshot(10.0, x: 100.0, y: 50.0, dx: 1.0, dy: 0.0, dir: 1, life: 100, maxLife: 100, animPayload: "run", statePayload: ""));

        // Sample at 12.0 (2 frames ahead of latest snapshot 10.0)
        Assert.True(buffer.SampleHermite(12.0, out var x, out var y, out _, out _, out _, out _));
        Assert.Equal(100.0 + 24.0 * 2.0, x, 2);
        Assert.Equal(50.0, y);
    }

    [Fact]
    public void MobSnapshotBuffer_SampleHermite_ClampsToOldestWhenBehind()
    {
        var buffer = new MobSnapshotBuffer();
        buffer.Add(new MobSnapshot(10.0, x: 100.0, y: 50.0, dx: 0.0, dy: 0.0, dir: 1, life: 100, maxLife: 100, animPayload: "idle", statePayload: ""));
        buffer.Add(new MobSnapshot(20.0, x: 200.0, y: 50.0, dx: 0.0, dy: 0.0, dir: 1, life: 100, maxLife: 100, animPayload: "idle", statePayload: ""));

        // Render frame 5.0 is older than oldest snapshot (10.0)
        Assert.True(buffer.SampleHermite(5.0, out var x, out var y, out _, out _, out _, out _));
        Assert.Equal(100.0, x);
        Assert.Equal(50.0, y);
    }

    [Theory]
    [InlineData(0.0, 0.0, 50.0, 50.0, true)]       // Close (70px) -> valid
    [InlineData(0.0, 0.0, 400.0, 0.0, true)]      // 400px (16.6 tiles) -> valid (long range skill/bow)
    [InlineData(0.0, 0.0, 590.0, 0.0, true)]      // 590px (< 600px max) -> valid
    [InlineData(0.0, 0.0, 650.0, 0.0, false)]     // 650px (> 600px max) -> reach exceeded
    [InlineData(0.0, 0.0, 1000.0, 1000.0, false)] // Off screen / distant level -> reach exceeded
    public void SpatialSanityReach_ChecksAccurately(double ax, double ay, double mx, double my, bool expectedValid)
    {
        double dx = ax - mx;
        double dy = ay - my;
        const double MaxHitSanityDistanceSq = (24.0 * 25.0) * (24.0 * 25.0); // 600px squared = 360,000

        bool isValid = double.IsFinite(dx) && double.IsFinite(dy) && (dx * dx + dy * dy) <= MaxHitSanityDistanceSq;
        Assert.Equal(expectedValid, isValid);
    }
}
