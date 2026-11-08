using System;
using DeadCellsMultiplayerMod.Network;
using DeadCellsMultiplayerMod.PortableCore;
using Xunit;

namespace DeadCellsMultiplayerMod.ProtocolReplay.Tests;

public sealed class BossRushTransitionGateTests : IDisposable
{
    public BossRushTransitionGateTests()
    {
        Cleanup();
    }

    public void Dispose()
    {
        Cleanup();
    }

    private static void Cleanup()
    {
        LobbySession.NetRef = null;
        LobbySession.ClearPrecommittedHostBossRushLaunchForTesting();
        RunLaunchCoordinator.ResetForTesting(NetRole.None);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(-1, false)]
    [InlineData(-5, false)]
    [InlineData(-100, false)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(999, false)]
    public void IsValidBossRushDoorIndex_ValidatesRange(int index, bool expected)
    {
        var result = LobbySession.IsValidBossRushDoorIndex(index);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("0", true, 0)]
    [InlineData("1", true, 1)]
    [InlineData("2", true, 2)]
    [InlineData("3", true, 3)]
    [InlineData("4", true, 4)]
    [InlineData("-1", false, -1)]
    [InlineData("-10", false, -10)]
    [InlineData("5", false, 5)]
    [InlineData("100", false, 100)]
    [InlineData("BossRushDoorSS", true, 0)]
    [InlineData("ShortSimple", true, 0)]
    [InlineData("BossRushDoorSH", true, 1)]
    [InlineData("ShortHard", true, 1)]
    [InlineData("BossRushDoorLS", true, 2)]
    [InlineData("LongSimple", true, 2)]
    [InlineData("BossRushDoorLH", true, 3)]
    [InlineData("LongHard", true, 3)]
    [InlineData("BossRushDoorCustom", true, 4)]
    [InlineData("Custom", true, 4)]
    [InlineData("Tier 0", true, 0)]
    [InlineData("Tier 1", true, 1)]
    [InlineData("Tier 2", true, 2)]
    [InlineData("Tier 3", true, 3)]
    [InlineData("Tier 4", true, 4)]
    [InlineData("Tier 5", false, 5)]
    [InlineData("Tier -1", false, -1)]
    [InlineData(null, false, -1)]
    [InlineData("", false, -1)]
    [InlineData("   ", false, -1)]
    [InlineData("InvalidVariant", false, -1)]
    public void TryParseBossRushDoorIndex_ParsesAndValidates(string? input, bool expectedSuccess, int expectedIndex)
    {
        var success = LobbySession.TryParseBossRushDoorIndex(input, out var index);
        Assert.Equal(expectedSuccess, success);
        Assert.Equal(expectedIndex, index);
    }

    [Theory]
    [InlineData("BossRushZone", true)]
    [InlineData("BossRushHUB", true)]
    [InlineData("BossRush", true)]
    [InlineData("bossrushzone", true)]
    [InlineData("BR_STAGE_1", true)]
    [InlineData("BR_Exit", true)]
    [InlineData("BossRushArena_02", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("PrisonStart", false)]
    [InlineData("Ossuary", false)]
    [InlineData("Bridge", false)]
    [InlineData("BossRushZone|Injected", false)]
    [InlineData("BossRushZone\nBad", false)]
    [InlineData("BossRushZone\0Null", false)]
    [InlineData("BossRushZone;drop", false)]
    [InlineData("BossRush/Exploit", false)]
    [InlineData("BR_\tStage", false)]
    public void IsValidBossRushLevelId_ValidatesLevelIdentifiers(string? levelId, bool expected)
    {
        var result = LobbySession.IsValidBossRushLevelId(levelId);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void PrecommitHostBossRushRunSeed_RejectsNegativeCoordinates()
    {
        var res1 = LobbySession.PrecommitHostBossRushRunSeed("BossRushDoorSS", -1, 10, out var seed1, out var seq1);
        Assert.False(res1);
        Assert.Equal(0, seed1);
        Assert.Equal(0, seq1);

        var res2 = LobbySession.PrecommitHostBossRushRunSeed("BossRushDoorSS", 10, -5, out var seed2, out var seq2);
        Assert.False(res2);
        Assert.Equal(0, seed2);
        Assert.Equal(0, seq2);
    }

    [Fact]
    public void PrecommitHostBossRushRunSeed_RejectsInvalidDoorIndex()
    {
        var res1 = LobbySession.PrecommitHostBossRushRunSeed("-1", 10, 10, out var seed1, out var seq1);
        Assert.False(res1);
        Assert.Equal(0, seed1);
        Assert.Equal(0, seq1);

        var res2 = LobbySession.PrecommitHostBossRushRunSeed("99", 10, 10, out var seed2, out var seq2);
        Assert.False(res2);
        Assert.Equal(0, seed2);
        Assert.Equal(0, seq2);

        var res3 = LobbySession.PrecommitHostBossRushRunSeed("InvalidDoorType", 10, 10, out var seed3, out var seq3);
        Assert.False(res3);
        Assert.Equal(0, seed3);
        Assert.Equal(0, seq3);
    }

    [Fact]
    public void TryBeginLocalBossRushLoad_RejectsInvalidLevelIdentifier()
    {
        RunLaunchCoordinator.ResetForTesting(NetRole.Client);
        LobbySession.NetRef = new NetNode(2, NetRole.Client);

        var descriptor = new RunLaunchDescriptor(
            Guid.NewGuid(),
            RunId: 10,
            Sequence: 1,
            ProtocolVersion: BuildInfo.NetworkProtocolVersion,
            RunSeed: 4444,
            LaunchKind: "dc.LaunchMode+BossRush",
            InitialLevelId: "PrisonStart", // Invalid for Boss Rush
            InitialLevelSeed: null,
            Difficulty: 0,
            BossCells: 0,
            BossRush: true,
            DlcFlags: 0,
            SaveSlot: 0,
            BossRushSeed: 4444,
            LevelGenSeed: 0,
            BossRushTier: 0,
            Route: "BossRushDoorSS",
            BossSequence: string.Empty,
            Modifiers: 0,
            TargetArena: "BossRushZone");

        // The coordinator must reject this remote descriptor due to invalid Boss Rush initial level ID
        var accepted = RunLaunchCoordinator.TryAcceptRemoteDescriptor(descriptor, out var ack, out _);
        Assert.False(accepted);
        Assert.False(ack.Accepted);
        Assert.Contains("invalid Boss Rush initial level identifier", ack.Error, StringComparison.OrdinalIgnoreCase);

        var valid = LobbySession.TryValidateBossRushDescriptor(descriptor, out var validateReason);
        Assert.False(valid);
        Assert.Contains("invalid Boss Rush initial level identifier", validateReason, StringComparison.OrdinalIgnoreCase);

        var canLoad = LobbySession.TryBeginLocalBossRushLoad(out var reason);
        Assert.False(canLoad);
        Assert.Contains("authoritative Boss Rush launch not received yet", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBeginLocalBossRushLoad_RejectsOutOfRangeDoorTier()
    {
        RunLaunchCoordinator.ResetForTesting(NetRole.Client);
        LobbySession.NetRef = new NetNode(2, NetRole.Client);

        var descriptor = new RunLaunchDescriptor(
            Guid.NewGuid(),
            RunId: 11,
            Sequence: 2,
            ProtocolVersion: BuildInfo.NetworkProtocolVersion,
            RunSeed: 5555,
            LaunchKind: "dc.LaunchMode+BossRush",
            InitialLevelId: "BossRushZone",
            InitialLevelSeed: null,
            Difficulty: 0,
            BossCells: 0,
            BossRush: true,
            DlcFlags: 0,
            SaveSlot: 0,
            BossRushSeed: 5555,
            LevelGenSeed: 0,
            BossRushTier: 5, // Out of range (> 4)
            Route: "BossRushDoorSS",
            BossSequence: string.Empty,
            Modifiers: 0,
            TargetArena: "BossRushZone");

        var accepted = RunLaunchCoordinator.TryAcceptRemoteDescriptor(descriptor, out var ack, out _);
        Assert.False(accepted);
        Assert.False(ack.Accepted);
        Assert.Contains("out-of-range Boss Rush door tier/index", ack.Error, StringComparison.OrdinalIgnoreCase);

        var valid = LobbySession.TryValidateBossRushDescriptor(descriptor, out var validateReason);
        Assert.False(valid);
        Assert.Contains("out-of-range Boss Rush door tier/index", validateReason, StringComparison.OrdinalIgnoreCase);

        var canLoad = LobbySession.TryBeginLocalBossRushLoad(out var reason);
        Assert.False(canLoad);
    }

    [Fact]
    public void TryBeginLocalBossRushLoad_RejectsNegativeRouteDoorIndex()
    {
        RunLaunchCoordinator.ResetForTesting(NetRole.Client);
        LobbySession.NetRef = new NetNode(2, NetRole.Client);

        var descriptor = new RunLaunchDescriptor(
            Guid.NewGuid(),
            RunId: 12,
            Sequence: 3,
            ProtocolVersion: BuildInfo.NetworkProtocolVersion,
            RunSeed: 6666,
            LaunchKind: "dc.LaunchMode+BossRush",
            InitialLevelId: "BossRushZone",
            InitialLevelSeed: null,
            Difficulty: 0,
            BossCells: 0,
            BossRush: true,
            DlcFlags: 0,
            SaveSlot: 0,
            BossRushSeed: 6666,
            LevelGenSeed: 0,
            BossRushTier: 0,
            Route: "-1", // Negative door index
            BossSequence: string.Empty,
            Modifiers: 0,
            TargetArena: "BossRushZone");

        var accepted = RunLaunchCoordinator.TryAcceptRemoteDescriptor(descriptor, out var ack, out _);
        Assert.False(accepted);
        Assert.False(ack.Accepted);
        Assert.Contains("invalid Boss Rush route door index", ack.Error, StringComparison.OrdinalIgnoreCase);

        var valid = LobbySession.TryValidateBossRushDescriptor(descriptor, out var validateReason);
        Assert.False(valid);
        Assert.Contains("invalid Boss Rush route door index", validateReason, StringComparison.OrdinalIgnoreCase);

        var canLoad = LobbySession.TryBeginLocalBossRushLoad(out var reason);
        Assert.False(canLoad);
    }

    [Fact]
    public void HostWatchdog_RemoteDisconnect_RecoversCommittedLaunchPhase()
    {
        RunLaunchCoordinator.ResetForTesting(NetRole.Host);

        // Commit host launch to put session into LaunchCommitted
        var desc = RunLaunchCoordinator.CreateOrReuseHostDescriptor(
            seed: 7777,
            sequence: 1,
            launchKind: "dc.LaunchMode+BossRush",
            initialLevelId: "BossRushZone",
            initialLevelSeed: null,
            difficulty: 0,
            bossCells: 0,
            dlcFlags: 0,
            saveSlot: 0,
            bossRushSeed: 7777,
            bossRushTier: 1,
            route: "BossRushDoorSH");

        Assert.Equal(CoopSessionPhase.LaunchCommitted, RunLaunchCoordinator.CurrentPhase);

        // Remote peer drops connection during launch loading
        RunLaunchCoordinator.OnRemoteDisconnected(NetRole.Host);

        // Host session phase must cleanly return to Lobby instead of staying frozen in LaunchCommitted
        Assert.Equal(CoopSessionPhase.Lobby, RunLaunchCoordinator.CurrentPhase);
    }

    [Fact]
    public void HostWatchdog_RemoteDisconnect_DuringLoadingLevel_RecoversSessionPhase()
    {
        RunLaunchCoordinator.ResetForTesting(NetRole.Host);

        // Commit host launch
        var desc = RunLaunchCoordinator.CreateOrReuseHostDescriptor(
            seed: 8888,
            sequence: 1,
            launchKind: "dc.LaunchMode+BossRush",
            initialLevelId: "BossRushZone",
            initialLevelSeed: null,
            difficulty: 0,
            bossCells: 0,
            dlcFlags: 0,
            saveSlot: 0,
            bossRushSeed: 8888,
            bossRushTier: 1,
            route: "BossRushDoorSH");

        // Advance to LoadingLevel phase
        RunLaunchCoordinator.MarkLocalLoading(1, "host_start_loading");
        Assert.Equal(CoopSessionPhase.LoadingLevel, RunLaunchCoordinator.CurrentPhase);

        // Remote peer drops connection during active loading
        RunLaunchCoordinator.OnRemoteDisconnected(NetRole.Host);

        // Host session phase must cleanly return to Lobby instead of staying frozen in LoadingLevel
        Assert.Equal(CoopSessionPhase.Lobby, RunLaunchCoordinator.CurrentPhase);
    }

    [Fact]
    public void HostWatchdog_ClientRejectionAck_AbortsCommittedLaunch()
    {
        RunLaunchCoordinator.ResetForTesting(NetRole.Host);

        var desc = RunLaunchCoordinator.CreateOrReuseHostDescriptor(
            seed: 9999,
            sequence: 1,
            launchKind: "dc.LaunchMode+BossRush",
            initialLevelId: "BossRushZone",
            initialLevelSeed: null,
            difficulty: 0,
            bossCells: 0,
            dlcFlags: 0,
            saveSlot: 0,
            bossRushSeed: 9999,
            bossRushTier: 1,
            route: "BossRushDoorSH");

        Assert.Equal(CoopSessionPhase.LaunchCommitted, RunLaunchCoordinator.CurrentPhase);

        // Client rejects launch with error
        var rejectionAck = new RunLaunchAck(
            desc.SessionId,
            desc.RunId,
            desc.Sequence,
            false,
            "missing required Boss Rush DLC");

        RunLaunchCoordinator.ReceiveHostAck(rejectionAck);

        // Host must abort committed launch and return to Lobby immediately
        Assert.Equal(CoopSessionPhase.Lobby, RunLaunchCoordinator.CurrentPhase);
    }
}
