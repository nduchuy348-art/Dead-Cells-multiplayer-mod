using DeadCellsMultiplayerMod.PortableCore;
using DeadCellsMultiplayerMod.Network;
using DeadCellsMultiplayerMod.Tools;
using Xunit;

namespace DeadCellsMultiplayerMod.ProtocolReplay.Tests;

public sealed class LaunchProtocolReplayTests
{
    [Fact]
    public void CommitLine_RoundTripsDescriptor()
    {
        var descriptor = CreateDescriptor();

        var line = RunLaunchWireCodec.BuildCommitLine(descriptor);
        Assert.StartsWith("RUNCOMMIT|", line, StringComparison.Ordinal);
        Assert.True(
            RunLaunchWireCodec.TryDecodeCommit(line["RUNCOMMIT|".Length..], out var decoded, out var error),
            error);
        Assert.NotNull(decoded);
        Assert.Equal(descriptor, decoded);
    }

    [Fact]
    public void ReplayRejectsStaleAndIllegalTransitions()
    {
        var state = new CoopSessionStateMachine();

        Assert.True(state.TryTransition(CoopSessionPhase.Lobby, 1, "connected", out var error), error);
        Assert.True(state.TryTransition(CoopSessionPhase.LaunchCommitted, 2, "commit", out error), error);
        Assert.False(state.TryTransition(CoopSessionPhase.Playing, 2, "stale", out error));
        Assert.Contains("Stale transition", error, StringComparison.Ordinal);
        Assert.False(state.TryTransition(CoopSessionPhase.Disconnected, 3, "illegal", out error));
        Assert.Contains("Illegal co-op session transition", error, StringComparison.Ordinal);
        Assert.Equal(CoopSessionPhase.LaunchCommitted, state.Phase);
    }

    [Fact]
    public void MalformedAndOversizedCommitPayloadsAreRejected()
    {
        Assert.False(RunLaunchWireCodec.TryDecodeCommit("not-base64", out _, out var malformedError));
        Assert.False(string.IsNullOrWhiteSpace(malformedError));

        var oversized = new string('A', 64 * 1024 + 1);
        Assert.False(RunLaunchWireCodec.TryDecodeCommit(oversized, out _, out var oversizedError));
        Assert.Contains("exceeds", oversizedError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClientLaunchGateMapsToPortableSessionPhases()
    {
        Assert.Equal(
            CoopSessionPhase.LaunchCommitted,
            RunLaunchCoordinator.MapClientLaunchPhaseToSessionPhase(LobbySession.ClientLaunchPhase.Armed));
        Assert.Equal(
            CoopSessionPhase.LoadingLevel,
            RunLaunchCoordinator.MapClientLaunchPhaseToSessionPhase(LobbySession.ClientLaunchPhase.Starting));
        Assert.Equal(
            CoopSessionPhase.Playing,
            RunLaunchCoordinator.MapClientLaunchPhaseToSessionPhase(LobbySession.ClientLaunchPhase.InRun));
    }

    [Fact]
    public void RealtimeBudgetDropsOnlyWhenWindowIsFull()
    {
        var budget = new NetPacketBudget(maxBytes: 10, windowMilliseconds: 1000);

        Assert.True(budget.TryConsume(6));
        Assert.False(budget.TryConsume(5));
        Assert.Equal(1, budget.DroppedPackets);
        Assert.Equal(5, budget.DroppedBytes);
    }

    [Fact]
    public void LifecycleTrackerInvalidatesOldGenerationOnStop()
    {
        var tracker = new LifecycleTracker("replay");
        var generation = tracker.Start();

        Assert.True(tracker.IsCurrent(generation));
        Assert.True(tracker.TryBeginStop());
        Assert.False(tracker.IsCurrent(generation));

        tracker.MarkDisposed();
        Assert.Equal(LifecycleState.Disposed, tracker.Snapshot.State);
    }


    [Fact]
    public void MobLifecycleLedgerIsIdempotentPerGeneration()
    {
        var ledger = new MobLifecycleLedger();

        ledger.MarkActive(10, 7);
        Assert.False(ledger.IsDead(10, 7));

        ledger.MarkDead(10, 7);
        Assert.True(ledger.IsDead(10, 7));

        // A stale active observation must not resurrect a confirmed death.
        ledger.MarkActive(10, 7);
        Assert.True(ledger.IsDead(10, 7));

        // The same NetId is valid again only in a new level generation.
        Assert.False(ledger.IsDead(11, 7));
    }

    [Theory]
    [InlineData("100.124.96.16")]
    [InlineData("100.125.93.13")]
    [InlineData("192.168.1.2")]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.5")]
    public void RoomCode_RoundTrips_IPv4Addresses(string ip)
    {
        var code = DeadCellsMultiplayerMod.LobbySession.IpToRoomCode(ip);
        Assert.False(string.IsNullOrWhiteSpace(code));
        Assert.Matches("^[0-9A-Z]+$", code);

        var ok = DeadCellsMultiplayerMod.LobbySession.TryParseRoomCodeOrIp(code, out var resolvedIp, out var resolvedPort);
        Assert.True(ok);
        Assert.Equal(ip, resolvedIp);
        Assert.Equal(1234, resolvedPort);
    }

    [Fact]
    public void RoomCode_HandlesPrefixAndCaseAndWhitespace()
    {
        var code = DeadCellsMultiplayerMod.LobbySession.IpToRoomCode("100.124.96.16");
        Assert.True(DeadCellsMultiplayerMod.LobbySession.TryParseRoomCodeOrIp($"  DC-{code.ToLowerInvariant()}  ", out var ip1, out _));
        Assert.Equal("100.124.96.16", ip1);

        Assert.True(DeadCellsMultiplayerMod.LobbySession.TryParseRoomCodeOrIp($"dc-{code}", out var ip2, out _));
        Assert.Equal("100.124.96.16", ip2);
    }

    [Fact]
    public void RoomCode_DirectIpAndPortParsing()
    {
        Assert.True(DeadCellsMultiplayerMod.LobbySession.TryParseRoomCodeOrIp("100.124.96.16:4321", out var ip, out var port));
        Assert.Equal("100.124.96.16", ip);
        Assert.Equal(4321, port);

        Assert.True(DeadCellsMultiplayerMod.LobbySession.TryParseRoomCodeOrIp("  192.168.1.50  ", out var ip2, out var port2));
        Assert.Equal("192.168.1.50", ip2);
        Assert.Equal(1234, port2);

        Assert.False(DeadCellsMultiplayerMod.LobbySession.TryParseRoomCodeOrIp(null, out _, out _));
        Assert.False(DeadCellsMultiplayerMod.LobbySession.TryParseRoomCodeOrIp("   ", out _, out _));
        Assert.False(DeadCellsMultiplayerMod.LobbySession.TryParseRoomCodeOrIp("invalid?!code", out _, out _));
    }

    private static RunLaunchDescriptor CreateDescriptor()
    {
        return new RunLaunchDescriptor(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            RunId: 7,
            Sequence: 3,
            ProtocolVersion: 17,
            RunSeed: 12345,
            LaunchKind: "normal",
            InitialLevelId: "PrisonStart",
            InitialLevelSeed: 42.5,
            Difficulty: 2,
            BossCells: 2,
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
    }
}
