using System;
using DeadCellsMultiplayerMod;
using DeadCellsMultiplayerMod.Ghost;
using DeadCellsMultiplayerMod.PortableCore;
using Xunit;

namespace DeadCellsMultiplayerMod.ProtocolReplay.Tests;

public sealed class MidRunJoinAndReconnectSyncTests
{
    [Fact]
    public void HostSpawnAnchor_InitializesWithSafeValuesAndCombatState()
    {
        var anchorSafe = new NetNode.HostSpawnAnchor(10, 20, "PrisonStart", inCombat: false);
        Assert.Equal(10, anchorSafe.Cx);
        Assert.Equal(20, anchorSafe.Cy);
        Assert.Equal("PrisonStart", anchorSafe.LevelId);
        Assert.False(anchorSafe.InCombat);

        var anchorCombat = new NetNode.HostSpawnAnchor(-5, -10, null, inCombat: true);
        Assert.Equal(0, anchorCombat.Cx);
        Assert.Equal(0, anchorCombat.Cy);
        Assert.Equal(string.Empty, anchorCombat.LevelId);
        Assert.True(anchorCombat.InCombat);
    }

    [Theory]
    [InlineData("SPAWNANCHOR|15|25|PrisonStart|1", 15, 25, "PrisonStart", true)]
    [InlineData("SPAWNANCHOR|30|40|Ramparts|0", 30, 40, "Ramparts", false)]
    [InlineData("SPAWNANCHOR|100|200|Ossuary|true", 100, 200, "Ossuary", true)]
    [InlineData("SPAWNANCHOR|100|200|Ossuary|false", 100, 200, "Ossuary", false)]
    [InlineData("SPAWNANCHOR|5|10|PrisonStart", 5, 10, "PrisonStart", false)] // Legacy 3-field line
    public void SpawnAnchor_ParsesCorrectly(string line, int expectedCx, int expectedCy, string expectedLevel, bool expectedInCombat)
    {
        Assert.StartsWith("SPAWNANCHOR|", line, StringComparison.OrdinalIgnoreCase);
        var parts = line["SPAWNANCHOR|".Length..].Split('|');
        Assert.True(parts.Length >= 3);
        Assert.True(int.TryParse(parts[0], out var cx));
        Assert.True(int.TryParse(parts[1], out var cy));
        var level = parts[2];
        var inCombat = false;
        if (parts.Length >= 4)
        {
            var trimmed = parts[3].Trim();
            inCombat = trimmed == "1" || string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase);
        }

        var anchor = new NetNode.HostSpawnAnchor(cx, cy, level, inCombat);
        Assert.Equal(expectedCx, anchor.Cx);
        Assert.Equal(expectedCy, anchor.Cy);
        Assert.Equal(expectedLevel, anchor.LevelId);
        Assert.Equal(expectedInCombat, anchor.InCombat);
    }

    [Theory]
    [InlineData(0, false)] // Empty
    [InlineData(1, true)]  // Solid
    [InlineData(2, false)]
    [InlineData(8, true)]  // Wood platform / one-way
    [InlineData(9, true)]  // Solid + wood
    public void SolidAndWoodTiles_DistinguishCollisionsCorrectly(int bits, bool expectedSolid)
    {
        Assert.Equal(expectedSolid, CombatSyncHelpers.IsSolidTileValue(bits));
        Assert.Equal((bits & 8) != 0, CombatSyncHelpers.IsWoodTileValue(bits));
    }

    [Fact]
    public void RunLaunchCoordinator_OnRemoteConnected_ClearsAckAndQueuedWithoutBreakingHostDescriptor()
    {
        RunLaunchCoordinator.ResetForTesting(NetRole.Host);

        var descriptor = RunLaunchCoordinator.CreateOrReuseHostDescriptor(
            seed: 12345,
            sequence: 1,
            launchKind: "NewGame",
            initialLevelId: "PrisonStart",
            initialLevelSeed: null,
            difficulty: 0,
            bossCells: 0,
            dlcFlags: 0,
            saveSlot: 0);

        Assert.NotNull(descriptor);
        Assert.Equal(1, descriptor.Sequence);

        // Simulate client acknowledging and queuing launch
        RunLaunchCoordinator.ReceiveHostAck(new RunLaunchAck(descriptor.SessionId, descriptor.RunId, descriptor.Sequence, true, ""));
        RunLaunchCoordinator.ReceiveClientQueued(new RunLaunchQueued(descriptor.SessionId, descriptor.RunId, descriptor.Sequence, true, ""));

        Assert.True(RunLaunchCoordinator.HasClientQueued(1));
        Assert.True(RunLaunchCoordinator.IsRemoteLaunchConfirmed(1));

        // Now simulate client reconnection (same role Host)
        RunLaunchCoordinator.OnRemoteConnected(NetRole.Host);

        // Host descriptor and session phase should be retained
        var current = RunLaunchCoordinator.GetCurrentHostDescriptor();
        Assert.NotNull(current);
        Assert.Equal(1, current.Sequence);

        // But handshake confirmation should be reset for the new peer session so stale ACK/QUEUED is not assumed
        Assert.False(RunLaunchCoordinator.HasClientQueued(1));
        Assert.False(RunLaunchCoordinator.IsRemoteLaunchConfirmed(1));
    }

    [Fact]
    public void RunLaunchCoordinator_CommitLaunchState_HandlesMidRunJoinPhases()
    {
        RunLaunchCoordinator.ResetForTesting(NetRole.Client);

        var descriptor = new RunLaunchDescriptor(
            SessionId: Guid.NewGuid(),
            RunId: 1,
            Sequence: 1,
            ProtocolVersion: BuildInfo.NetworkProtocolVersion,
            RunSeed: 98765,
            LaunchKind: "NewGame",
            InitialLevelId: "PrisonStart",
            InitialLevelSeed: null,
            Difficulty: 1,
            BossCells: 1,
            BossRush: false,
            DlcFlags: 0,
            SaveSlot: 0,
            BossRushSeed: 0,
            LevelGenSeed: 0,
            BossRushTier: 0,
            Route: string.Empty,
            BossSequence: string.Empty,
            Modifiers: 0,
            TargetArena: "PrisonStart");

        Assert.True(RunLaunchCoordinator.TryAcceptRemoteDescriptor(descriptor, out var ack, out var isNewCommit));
        Assert.True(ack.Accepted);
        Assert.True(isNewCommit);
        Assert.Equal(CoopSessionPhase.LaunchCommitted, RunLaunchCoordinator.CurrentPhase);
    }

    [Fact]
    public void LobbySession_TryConsumeMidRunJoinSpawn_ConsumesOnce()
    {
        RunLaunchCoordinator.ResetForTesting(NetRole.Client);
        LobbySession._role = NetRole.Client;
        LobbySession._inActualRun = false;

        // Simulate RUNREADY arriving for client not in actual run
        var ready = new RunLevelReady(Guid.NewGuid(), 1, 1, "PrisonStart");
        RunLaunchFlow.ReceiveRunLevelReadyPayload(RunLaunchWireCodec.EncodeReadyPayload(ready));

        Assert.True(RunLaunchFlow.TryConsumeMidRunJoinSpawn());
        Assert.False(RunLaunchFlow.TryConsumeMidRunJoinSpawn());
    }

    [Fact]
    public void SolidAndWoodCollisionBuffer_AccuratelyDistinguishesGroundPlatformsAndAir()
    {
        // 3 wide by 3 high test grid
        // Row 0: air (0), air (0), air (0)
        // Row 1: air (0), wood platform (8), air (0)
        // Row 2: solid (1), solid (1), solid (1)
        int wid = 3;
        int hei = 3;
        var bytes = System.Runtime.InteropServices.Marshal.AllocHGlobal(wid * hei * 4);
        try
        {
            int[] grid = new int[]
            {
                0, 0, 0,
                0, 8, 0,
                1, 1, 1
            };
            System.Runtime.InteropServices.Marshal.Copy(grid, 0, bytes, grid.Length);

            // Row 0 is air
            Assert.False(CombatSyncHelpers.IsSolidTile(bytes, wid, hei, 1, 0));
            Assert.False(CombatSyncHelpers.IsWoodTile(bytes, wid, hei, 1, 0));

            // Row 1 middle is wood platform (bit 8)
            Assert.True(CombatSyncHelpers.IsSolidTile(bytes, wid, hei, 1, 1)); // bit 8 is recognized as solid barrier for standing
            Assert.True(CombatSyncHelpers.IsWoodTile(bytes, wid, hei, 1, 1));
            Assert.False(CombatSyncHelpers.IsSolidTile(bytes, wid, hei, 0, 1)); // row 1 left is air
            Assert.False(CombatSyncHelpers.IsWoodTile(bytes, wid, hei, 0, 1));

            // Row 2 is solid wall/floor (bit 1)
            Assert.True(CombatSyncHelpers.IsSolidTile(bytes, wid, hei, 1, 2));
            Assert.False(CombatSyncHelpers.IsWoodTile(bytes, wid, hei, 1, 2));

            // Out-of-bounds checks
            Assert.True(CombatSyncHelpers.IsSolidTile(bytes, wid, hei, -1, 0)); // negative X is treated as solid boundary
            Assert.True(CombatSyncHelpers.IsSolidTile(bytes, wid, hei, 5, 0));  // overflow X is treated as solid boundary
            Assert.False(CombatSyncHelpers.IsWoodTile(bytes, wid, hei, -1, 0));
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(bytes);
        }
    }

    [Fact]
    public void RunLaunchCoordinator_OnRemoteDisconnected_WhileInActualRun_PreservesSessionPhase()
    {
        RunLaunchCoordinator.ResetForTesting(NetRole.Host);
        LobbySession._inActualRun = true;

        var descriptor = RunLaunchCoordinator.CreateOrReuseHostDescriptor(
            seed: 55555,
            sequence: 1,
            launchKind: "NewGame",
            initialLevelId: "PrisonStart",
            initialLevelSeed: null,
            difficulty: 0,
            bossCells: 0,
            dlcFlags: 0,
            saveSlot: 0);

        Assert.NotNull(descriptor);
        RunLaunchCoordinator.MarkLocalLoading(1, "test_loading");
        Assert.Equal(CoopSessionPhase.LoadingLevel, RunLaunchCoordinator.CurrentPhase);

        // When in actual run, disconnecting a peer must NOT demote host session phase to Lobby
        RunLaunchCoordinator.OnRemoteDisconnected(NetRole.Host);
        Assert.Equal(CoopSessionPhase.LoadingLevel, RunLaunchCoordinator.CurrentPhase);

        // Clean up
        LobbySession._inActualRun = false;
    }

    [Fact]
    public void RunLaunchCoordinator_OnRemoteDisconnected_WhenNotInActualRun_ReturnsToLobby()
    {
        RunLaunchCoordinator.ResetForTesting(NetRole.Host);
        LobbySession._inActualRun = false;

        var descriptor = RunLaunchCoordinator.CreateOrReuseHostDescriptor(
            seed: 77777,
            sequence: 1,
            launchKind: "NewGame",
            initialLevelId: "PrisonStart",
            initialLevelSeed: null,
            difficulty: 0,
            bossCells: 0,
            dlcFlags: 0,
            saveSlot: 0);

        Assert.NotNull(descriptor);
        RunLaunchCoordinator.MarkLocalLoading(1, "test_loading");
        Assert.Equal(CoopSessionPhase.LoadingLevel, RunLaunchCoordinator.CurrentPhase);

        // When not in actual run, disconnecting during launch returns cleanly to Lobby
        RunLaunchCoordinator.OnRemoteDisconnected(NetRole.Host);
        Assert.Equal(CoopSessionPhase.Lobby, RunLaunchCoordinator.CurrentPhase);
    }

    [Fact]
    public void MobDie_CarriesAuthoritativeDeadMobInformationForCullResync()
    {
        var die = new NetNode.MobDie(1, 42, 125.5, 250.75, 99, "Zombie");
        Assert.Equal(1, die.UserId);
        Assert.Equal(42, die.MobIndex);
        Assert.Equal(125.5, die.X);
        Assert.Equal(250.75, die.Y);
        Assert.Equal(99, die.Generation);
        Assert.Equal("Zombie", die.Type);

        var nanDie = new NetNode.MobDie(2, -1, double.NaN, double.PositiveInfinity, 0, null!);
        Assert.Equal(2, nanDie.UserId);
        Assert.Equal(0, nanDie.MobIndex);
        Assert.Equal(0.0, nanDie.X);
        Assert.Equal(0.0, nanDie.Y);
        Assert.Equal(string.Empty, nanDie.Type);
    }
}
