using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using DeadCellsMultiplayerMod.PortableCore;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace DeadCellsMultiplayerMod.ProtocolReplay.Tests;

public sealed class DlcEntitlementReconciliationTests : IDisposable
{
    public DlcEntitlementReconciliationTests()
    {
        DlcEntitlementManager.Reset();
    }

    public void Dispose()
    {
        DlcEntitlementManager.Reset();
        RunLaunchCoordinator.ResetForTesting(NetRole.None);
    }

    [Theory]
    [InlineData("PrisonStart", false, DlcEntitlementManager.DlcMask.None)]
    [InlineData("Promenade", false, DlcEntitlementManager.DlcMask.None)]
    [InlineData("Ossuary", false, DlcEntitlementManager.DlcMask.None)]
    [InlineData("Ramparts", false, DlcEntitlementManager.DlcMask.None)]
    [InlineData("BlackBridge", false, DlcEntitlementManager.DlcMask.None)]
    [InlineData("StiltVillage", false, DlcEntitlementManager.DlcMask.None)]
    [InlineData("Graveyard", false, DlcEntitlementManager.DlcMask.None)]
    [InlineData("ClockTower", false, DlcEntitlementManager.DlcMask.None)]
    [InlineData("ClockRoom", false, DlcEntitlementManager.DlcMask.None)]
    [InlineData("Castle", false, DlcEntitlementManager.DlcMask.None)]
    [InlineData("ThroneRoom", false, DlcEntitlementManager.DlcMask.None)]
    [InlineData("Bank", false, DlcEntitlementManager.DlcMask.None)]
    [InlineData("", false, DlcEntitlementManager.DlcMask.None)]
    [InlineData("   ", false, DlcEntitlementManager.DlcMask.None)]
    [InlineData(null, false, DlcEntitlementManager.DlcMask.None)]
    public void RequiresDlc_BaseGameLevels_DoNotRequireDlc(string? levelId, bool expectedRequiresDlc, DlcEntitlementManager.DlcMask expectedMask)
    {
        var requires = DlcEntitlementManager.RequiresDlc(levelId, out var mask, out var internalId, out var dispName);
        Assert.Equal(expectedRequiresDlc, requires);
        Assert.Equal(expectedMask, mask);
        Assert.Null(internalId);
        Assert.Null(dispName);
    }

    [Theory]
    // Rise of the Giant
    [InlineData("Cavern", DlcEntitlementManager.DlcMask.RiseOfTheGiant, "RiseOfTheGiant", "Rise of the Giant")]
    [InlineData("Giant", DlcEntitlementManager.DlcMask.RiseOfTheGiant, "RiseOfTheGiant", "Rise of the Giant")]
    [InlineData("Astrolab", DlcEntitlementManager.DlcMask.RiseOfTheGiant, "RiseOfTheGiant", "Rise of the Giant")]
    [InlineData("Observatory", DlcEntitlementManager.DlcMask.RiseOfTheGiant, "RiseOfTheGiant", "Rise of the Giant")]
    [InlineData("T_Cavern", DlcEntitlementManager.DlcMask.RiseOfTheGiant, "RiseOfTheGiant", "Rise of the Giant")]
    // The Bad Seed
    [InlineData("Greenhouse", DlcEntitlementManager.DlcMask.TheBadSeed, "TheBadSeed", "The Bad Seed")]
    [InlineData("Swamp", DlcEntitlementManager.DlcMask.TheBadSeed, "TheBadSeed", "The Bad Seed")]
    [InlineData("SwampHeart", DlcEntitlementManager.DlcMask.TheBadSeed, "TheBadSeed", "The Bad Seed")]
    [InlineData("T_Swamp", DlcEntitlementManager.DlcMask.TheBadSeed, "TheBadSeed", "The Bad Seed")]
    // Fatal Falls
    [InlineData("Tumulus", DlcEntitlementManager.DlcMask.FatalFalls, "FatalFalls", "Fatal Falls")]
    [InlineData("Cliff", DlcEntitlementManager.DlcMask.FatalFalls, "FatalFalls", "Fatal Falls")]
    [InlineData("GardenerStage", DlcEntitlementManager.DlcMask.FatalFalls, "FatalFalls", "Fatal Falls")]
    [InlineData("T_Tumulus", DlcEntitlementManager.DlcMask.FatalFalls, "FatalFalls", "Fatal Falls")]
    // The Queen and the Sea
    [InlineData("Shipwreck", DlcEntitlementManager.DlcMask.TheQueenAndTheSea, "TheQueenAndTheSea", "The Queen and the Sea")]
    [InlineData("Lighthouse", DlcEntitlementManager.DlcMask.TheQueenAndTheSea, "TheQueenAndTheSea", "The Queen and the Sea")]
    [InlineData("QueenArena", DlcEntitlementManager.DlcMask.TheQueenAndTheSea, "TheQueenAndTheSea", "The Queen and the Sea")]
    [InlineData("T_Queen", DlcEntitlementManager.DlcMask.TheQueenAndTheSea, "TheQueenAndTheSea", "The Queen and the Sea")]
    // Return to Castlevania ("Purple")
    [InlineData("PurpleGarden", DlcEntitlementManager.DlcMask.ReturnToCastlevania, "Purple", "Return to Castlevania")]
    [InlineData("DookuCastle", DlcEntitlementManager.DlcMask.ReturnToCastlevania, "Purple", "Return to Castlevania")]
    [InlineData("DeathArena", DlcEntitlementManager.DlcMask.ReturnToCastlevania, "Purple", "Return to Castlevania")]
    [InlineData("DookuArena", DlcEntitlementManager.DlcMask.ReturnToCastlevania, "Purple", "Return to Castlevania")]
    [InlineData("RichterCastle", DlcEntitlementManager.DlcMask.ReturnToCastlevania, "Purple", "Return to Castlevania")]
    public void RequiresDlc_OfficialDlcLevels_ResolvesCorrectDlcPack(
        string levelId,
        DlcEntitlementManager.DlcMask expectedMask,
        string expectedInternalId,
        string expectedDisplayName)
    {
        var requires = DlcEntitlementManager.RequiresDlc(levelId, out var mask, out var internalId, out var dispName);
        Assert.True(requires);
        Assert.Equal(expectedMask, mask);
        Assert.Equal(expectedInternalId, internalId);
        Assert.Equal(expectedDisplayName, dispName);
    }

    [Theory]
    [InlineData("RiseOfTheGiant", DlcEntitlementManager.DlcMask.RiseOfTheGiant)]
    [InlineData("TheBadSeed", DlcEntitlementManager.DlcMask.TheBadSeed)]
    [InlineData("FatalFalls", DlcEntitlementManager.DlcMask.FatalFalls)]
    [InlineData("TheQueenAndTheSea", DlcEntitlementManager.DlcMask.TheQueenAndTheSea)]
    [InlineData("Purple", DlcEntitlementManager.DlcMask.ReturnToCastlevania)]
    [InlineData("Castlevania", DlcEntitlementManager.DlcMask.ReturnToCastlevania)]
    [InlineData("Giant", DlcEntitlementManager.DlcMask.RiseOfTheGiant)]
    [InlineData("Seed", DlcEntitlementManager.DlcMask.TheBadSeed)]
    [InlineData("Fatal", DlcEntitlementManager.DlcMask.FatalFalls)]
    [InlineData("Queen", DlcEntitlementManager.DlcMask.TheQueenAndTheSea)]
    [InlineData("DLC0", DlcEntitlementManager.DlcMask.RiseOfTheGiant)]
    [InlineData("DLC_0", DlcEntitlementManager.DlcMask.RiseOfTheGiant)]
    [InlineData("DLC1", DlcEntitlementManager.DlcMask.TheBadSeed)]
    [InlineData("DLC_1", DlcEntitlementManager.DlcMask.TheBadSeed)]
    [InlineData("DLC2", DlcEntitlementManager.DlcMask.FatalFalls)]
    [InlineData("DLC_2", DlcEntitlementManager.DlcMask.FatalFalls)]
    [InlineData("DLC3", DlcEntitlementManager.DlcMask.TheQueenAndTheSea)]
    [InlineData("DLC_3", DlcEntitlementManager.DlcMask.TheQueenAndTheSea)]
    [InlineData("DLC4", DlcEntitlementManager.DlcMask.ReturnToCastlevania)]
    [InlineData("DLC_4", DlcEntitlementManager.DlcMask.ReturnToCastlevania)]
    [InlineData("DLCP", DlcEntitlementManager.DlcMask.ReturnToCastlevania)]
    [InlineData("DLC_P", DlcEntitlementManager.DlcMask.ReturnToCastlevania)]
    [InlineData("UnknownNonexistentDLC", DlcEntitlementManager.DlcMask.None)]
    [InlineData("", DlcEntitlementManager.DlcMask.None)]
    [InlineData(null, DlcEntitlementManager.DlcMask.None)]
    public void ParseDlcMask_ParsesIdentifersCorrectly(string? internalId, DlcEntitlementManager.DlcMask expectedMask)
    {
        var mask = DlcEntitlementManager.ParseDlcMask(internalId);
        Assert.Equal(expectedMask, mask);
    }

    [Fact]
    public void IsLevelAccessibleLocally_WithOverride_EnforcesOwnership()
    {
        // Player possesses 0 DLCs
        DlcEntitlementManager.SetLocalDlcMaskOverride(0);

        // Base game levels always accessible
        Assert.True(DlcEntitlementManager.IsLevelAccessibleLocally("PrisonStart", out _, out _));
        Assert.True(DlcEntitlementManager.IsLevelAccessibleLocally("Promenade", out _, out _));

        // DLC biomes inaccessible
        Assert.False(DlcEntitlementManager.IsLevelAccessibleLocally("Greenhouse", out var req1, out var disp1));
        Assert.Equal("TheBadSeed", req1);
        Assert.Equal("The Bad Seed", disp1);

        Assert.False(DlcEntitlementManager.IsLevelAccessibleLocally("Tumulus", out var req2, out var disp2));
        Assert.Equal("FatalFalls", req2);

        Assert.False(DlcEntitlementManager.IsLevelAccessibleLocally("Lighthouse", out var req3, out var disp3));
        Assert.Equal("TheQueenAndTheSea", req3);

        Assert.False(DlcEntitlementManager.IsLevelAccessibleLocally("DookuCastle", out var req4, out var disp4));
        Assert.Equal("Purple", req4);

        // Now grant player FatalFalls + ReturnToCastlevania
        var granted = (int)(DlcEntitlementManager.DlcMask.FatalFalls | DlcEntitlementManager.DlcMask.ReturnToCastlevania);
        DlcEntitlementManager.SetLocalDlcMaskOverride(granted);

        // Fatal Falls & Castlevania levels accessible
        Assert.True(DlcEntitlementManager.IsLevelAccessibleLocally("Tumulus", out _, out _));
        Assert.True(DlcEntitlementManager.IsLevelAccessibleLocally("Cliff", out _, out _));
        Assert.True(DlcEntitlementManager.IsLevelAccessibleLocally("PurpleGarden", out _, out _));
        Assert.True(DlcEntitlementManager.IsLevelAccessibleLocally("DookuCastle", out _, out _));

        // TheBadSeed and Queen and the Sea still inaccessible
        Assert.False(DlcEntitlementManager.IsLevelAccessibleLocally("Greenhouse", out _, out _));
        Assert.False(DlcEntitlementManager.IsLevelAccessibleLocally("Lighthouse", out _, out _));
        Assert.False(DlcEntitlementManager.IsLevelAccessibleLocally("Shipwreck", out _, out _));

        // Grant All
        DlcEntitlementManager.SetLocalDlcMaskOverride((int)DlcEntitlementManager.DlcMask.All);
        Assert.True(DlcEntitlementManager.IsLevelAccessibleLocally("Greenhouse", out _, out _));
        Assert.True(DlcEntitlementManager.IsLevelAccessibleLocally("Lighthouse", out _, out _));
        Assert.True(DlcEntitlementManager.IsLevelAccessibleLocally("DookuCastle", out _, out _));
        Assert.True(DlcEntitlementManager.IsLevelAccessibleLocally("Cavern", out _, out _));
    }

    [Fact]
    public void PeerDlcReconciliation_TracksAndReconcilesPeerOwnership()
    {
        // Local player has All DLCs
        DlcEntitlementManager.SetLocalDlcMaskOverride((int)DlcEntitlementManager.DlcMask.All);

        // Peer 2 has Fatal Falls only
        DlcEntitlementManager.SetPeerDlcMask(2, (int)DlcEntitlementManager.DlcMask.FatalFalls);

        // Peer 3 has Queen and the Sea + Castlevania
        DlcEntitlementManager.SetPeerDlcMask(3, (int)(DlcEntitlementManager.DlcMask.TheQueenAndTheSea | DlcEntitlementManager.DlcMask.ReturnToCastlevania));

        // Base game level accessible for all peers
        Assert.True(DlcEntitlementManager.IsLevelAccessibleForPeer(2, "PrisonStart", out _, out _));
        Assert.True(DlcEntitlementManager.IsLevelAccessibleForPeer(3, "PrisonStart", out _, out _));

        // Peer 2 access
        Assert.True(DlcEntitlementManager.IsLevelAccessibleForPeer(2, "Tumulus", out _, out _));
        Assert.False(DlcEntitlementManager.IsLevelAccessibleForPeer(2, "Lighthouse", out _, out _));
        Assert.False(DlcEntitlementManager.IsLevelAccessibleForPeer(2, "PurpleGarden", out _, out _));

        // Peer 3 access
        Assert.False(DlcEntitlementManager.IsLevelAccessibleForPeer(3, "Tumulus", out _, out _));
        Assert.True(DlcEntitlementManager.IsLevelAccessibleForPeer(3, "Lighthouse", out _, out _));
        Assert.True(DlcEntitlementManager.IsLevelAccessibleForPeer(3, "PurpleGarden", out _, out _));

        // Active peer group reconciliation:
        var party = new[] { 1, 2, 3 }; // 1 is local host

        // Base game accessible for entire party
        Assert.True(DlcEntitlementManager.IsLevelAccessibleForActivePeers(party, 1, "PrisonStart", out var missing1));
        Assert.Equal(0, missing1);

        // Fatal Falls level: Peer 3 lacks FatalFalls
        Assert.False(DlcEntitlementManager.IsLevelAccessibleForActivePeers(party, 1, "Tumulus", out var missing2));
        Assert.Equal(3, missing2);

        // Queen level: Peer 2 lacks TheQueenAndTheSea
        Assert.False(DlcEntitlementManager.IsLevelAccessibleForActivePeers(party, 1, "Shipwreck", out var missing3));
        Assert.Equal(2, missing3);

        // Peer disconnect
        DlcEntitlementManager.RemovePeerDlcMask(2);
        Assert.False(DlcEntitlementManager.TryGetPeerDlcMask(2, out _));

        // Unknown peer is treated safely as missing DLC
        Assert.False(DlcEntitlementManager.IsLevelAccessibleForPeer(2, "Tumulus", out _, out _));
    }

    [Fact]
    public void RunLaunchCoordinator_DLCValidation_RejectsInaccessibleBiomes()
    {
        RunLaunchCoordinator.ResetForTesting(NetRole.Client);

        // Client has 0 DLCs installed
        DlcEntitlementManager.SetLocalDlcMaskOverride(0);

        // Host launches into base game level (PrisonStart) -> Allowed!
        var baseGameDescriptor = new RunLaunchDescriptor(
            Guid.NewGuid(),
            RunId: 100,
            Sequence: 1,
            ProtocolVersion: BuildInfo.NetworkProtocolVersion,
            RunSeed: 12345,
            LaunchKind: "normal",
            InitialLevelId: "PrisonStart",
            InitialLevelSeed: 1.0,
            Difficulty: 0,
            BossCells: 0,
            BossRush: false,
            DlcFlags: (ulong)DlcEntitlementManager.DlcMask.All,
            SaveSlot: 0,
            BossRushSeed: 0,
            LevelGenSeed: 0,
            BossRushTier: 0,
            Route: string.Empty,
            BossSequence: string.Empty,
            Modifiers: 0,
            TargetArena: "PrisonStart");

        var acceptedBase = RunLaunchCoordinator.TryAcceptRemoteDescriptor(baseGameDescriptor, out var ackBase, out _);
        Assert.True(acceptedBase);
        Assert.True(ackBase.Accepted);
        Assert.Empty(ackBase.Error);

        // Host launches into Castlevania level (PurpleGarden) -> Client lacks DLC, must cleanly reject!
        RunLaunchCoordinator.ResetForTesting(NetRole.Client);
        var castlevaniaDescriptor = new RunLaunchDescriptor(
            Guid.NewGuid(),
            RunId: 101,
            Sequence: 2,
            ProtocolVersion: BuildInfo.NetworkProtocolVersion,
            RunSeed: 54321,
            LaunchKind: "normal",
            InitialLevelId: "PurpleGarden",
            InitialLevelSeed: 1.0,
            Difficulty: 0,
            BossCells: 0,
            BossRush: false,
            DlcFlags: (ulong)DlcEntitlementManager.DlcMask.ReturnToCastlevania,
            SaveSlot: 0,
            BossRushSeed: 0,
            LevelGenSeed: 0,
            BossRushTier: 0,
            Route: string.Empty,
            BossSequence: string.Empty,
            Modifiers: 0,
            TargetArena: "PurpleGarden");

        var acceptedCastlevania = RunLaunchCoordinator.TryAcceptRemoteDescriptor(castlevaniaDescriptor, out var ackCastlevania, out _);
        Assert.False(acceptedCastlevania);
        Assert.False(ackCastlevania.Accepted);
        Assert.Contains("Return to Castlevania", ackCastlevania.Error, StringComparison.OrdinalIgnoreCase);

        // Grant Castlevania to client -> Client now accepts!
        DlcEntitlementManager.SetLocalDlcMaskOverride((int)DlcEntitlementManager.DlcMask.ReturnToCastlevania);
        var acceptedWithDlc = RunLaunchCoordinator.TryAcceptRemoteDescriptor(castlevaniaDescriptor, out var ackWithDlc, out _);
        Assert.True(acceptedWithDlc);
        Assert.True(ackWithDlc.Accepted);
    }

    [Theory]
    [InlineData("TheBadSeed", DlcEntitlementManager.DlcMask.TheBadSeed)]
    [InlineData("The Bad Seed", DlcEntitlementManager.DlcMask.TheBadSeed)]
    [InlineData("Purple", DlcEntitlementManager.DlcMask.ReturnToCastlevania)]
    [InlineData("Return to Castlevania", DlcEntitlementManager.DlcMask.ReturnToCastlevania)]
    [InlineData("FatalFalls", DlcEntitlementManager.DlcMask.FatalFalls)]
    [InlineData("TheQueenAndTheSea", DlcEntitlementManager.DlcMask.TheQueenAndTheSea)]
    [InlineData("RiseOfTheGiant", DlcEntitlementManager.DlcMask.RiseOfTheGiant)]
    [InlineData("FisherBarque", DlcEntitlementManager.DlcMask.TheQueenAndTheSea)]
    [InlineData("LighthouseTop", DlcEntitlementManager.DlcMask.TheQueenAndTheSea)]
    [InlineData("LighthouseBottom", DlcEntitlementManager.DlcMask.TheQueenAndTheSea)]
    [InlineData("T_RichterCastle", DlcEntitlementManager.DlcMask.ReturnToCastlevania)]
    [InlineData("T_FisherBarque", DlcEntitlementManager.DlcMask.TheQueenAndTheSea)]
    [InlineData("T_AfterSwamp", DlcEntitlementManager.DlcMask.TheBadSeed)]
    [InlineData("T_AfterDeathArena", DlcEntitlementManager.DlcMask.ReturnToCastlevania)]
    [InlineData("DebugRTC", DlcEntitlementManager.DlcMask.ReturnToCastlevania)]
    [InlineData("DLC0", DlcEntitlementManager.DlcMask.RiseOfTheGiant)]
    [InlineData("DLC1", DlcEntitlementManager.DlcMask.TheBadSeed)]
    [InlineData("DLC2", DlcEntitlementManager.DlcMask.FatalFalls)]
    [InlineData("DLC3", DlcEntitlementManager.DlcMask.TheQueenAndTheSea)]
    [InlineData("DLC4", DlcEntitlementManager.DlcMask.ReturnToCastlevania)]
    [InlineData("DLCP", DlcEntitlementManager.DlcMask.ReturnToCastlevania)]
    public void RequiresDlc_InternalAndNewBiomes_ResolveCorrectly(string levelOrDlcId, DlcEntitlementManager.DlcMask expectedMask)
    {
        var requires = DlcEntitlementManager.RequiresDlc(levelOrDlcId, out var mask, out var internalId, out var dispName);
        Assert.True(requires);
        Assert.Equal(expectedMask, mask);
        Assert.NotNull(internalId);
        Assert.NotNull(dispName);
    }

    [Fact]
    public void IsLevelAccessibleForParty_ValidatesBothLocalAndRemotePeers()
    {
        var party = new[] { 1, 2 }; // 1 = Host, 2 = Client

        // Case 1: Local player (1) lacks DLC -> missingPeerId = 1
        DlcEntitlementManager.SetLocalDlcMaskOverride(0);
        DlcEntitlementManager.SetPeerDlcMask(2, (int)DlcEntitlementManager.DlcMask.All);
        Assert.False(DlcEntitlementManager.IsLevelAccessibleForParty(party, 1, "PurpleGarden", out var missing1));
        Assert.Equal(1, missing1);

        // Case 2: Local player (1) has DLC, but Client (2) lacks DLC -> missingPeerId = 2
        DlcEntitlementManager.SetLocalDlcMaskOverride((int)DlcEntitlementManager.DlcMask.All);
        DlcEntitlementManager.SetPeerDlcMask(2, 0);
        Assert.False(DlcEntitlementManager.IsLevelAccessibleForParty(party, 1, "PurpleGarden", out var missing2));
        Assert.Equal(2, missing2);

        // Case 3: Both possess DLC -> Returns true
        DlcEntitlementManager.SetPeerDlcMask(2, (int)DlcEntitlementManager.DlcMask.ReturnToCastlevania);
        Assert.True(DlcEntitlementManager.IsLevelAccessibleForParty(party, 1, "PurpleGarden", out var missing3));
        Assert.Equal(0, missing3);

        // Case 4: Base game level -> Returns true regardless of DLC
        DlcEntitlementManager.SetLocalDlcMaskOverride(0);
        DlcEntitlementManager.SetPeerDlcMask(2, 0);
        Assert.True(DlcEntitlementManager.IsLevelAccessibleForParty(party, 1, "PrisonStart", out var missing4));
        Assert.Equal(0, missing4);
    }

    [Fact]
    public void RunLaunchCoordinator_HostZeroDlc_RecordsPeerMaskZero()
    {
        RunLaunchCoordinator.ResetForTesting(NetRole.Client);

        // Host has 0 DLCs
        var zeroDlcDescriptor = new RunLaunchDescriptor(
            Guid.NewGuid(),
            RunId: 200,
            Sequence: 1,
            ProtocolVersion: BuildInfo.NetworkProtocolVersion,
            RunSeed: 12345,
            LaunchKind: "normal",
            InitialLevelId: "PrisonStart",
            InitialLevelSeed: null,
            Difficulty: 0,
            BossCells: 0,
            BossRush: false,
            DlcFlags: 0, // No DLC
            SaveSlot: 0,
            BossRushSeed: 0,
            LevelGenSeed: 0,
            BossRushTier: 0,
            Route: string.Empty,
            BossSequence: string.Empty,
            Modifiers: 0,
            TargetArena: "PrisonStart");

        var accepted = RunLaunchCoordinator.TryAcceptRemoteDescriptor(zeroDlcDescriptor, out var ack, out _);
        Assert.True(accepted);
        Assert.True(ack.Accepted);

        // Peer 1 mask should be registered as 0 (not missing)
        Assert.True(DlcEntitlementManager.TryGetPeerDlcMask(1, out var hostMask));
        Assert.Equal(0, hostMask);
    }

    [Fact]
    public void InterPortalEvent_RequiresMissingLocalDlc_ValidatesEntitlementCorrectly()
    {
        // Local has no DLC
        DlcEntitlementManager.SetLocalDlcMaskOverride(0);

        var portalDlc = new DeadCellsMultiplayerMod.Interaction.InterPortalEvent(100.0, 200.0, "show", "PurpleGarden");
        Assert.True(portalDlc.RequiresMissingLocalDlc(out var missingDlc));
        Assert.Equal("Return to Castlevania", missingDlc);

        var portalBase = new DeadCellsMultiplayerMod.Interaction.InterPortalEvent(100.0, 200.0, "show", "PrisonStart");
        Assert.False(portalBase.RequiresMissingLocalDlc(out _));

        // Grant Castlevania
        DlcEntitlementManager.SetLocalDlcMaskOverride((int)DlcEntitlementManager.DlcMask.ReturnToCastlevania);
        Assert.False(portalDlc.RequiresMissingLocalDlc(out _));
    }

    [Fact]
    public void CreateOrReuseHostDescriptor_ValidatesHostLocalEntitlement()
    {
        RunLaunchCoordinator.ResetForTesting(NetRole.Host);

        // Host lacks Castlevania
        DlcEntitlementManager.SetLocalDlcMaskOverride(0);

        // Starting in Castlevania level should throw
        var ex = Assert.Throws<InvalidOperationException>(() =>
            RunLaunchCoordinator.CreateOrReuseHostDescriptor(
                seed: 12345,
                sequence: 1,
                launchKind: "normal",
                initialLevelId: "PurpleGarden",
                initialLevelSeed: null,
                difficulty: 0,
                bossCells: 0,
                dlcFlags: 0,
                saveSlot: 0));

        Assert.Contains("Return to Castlevania", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Grant Castlevania -> Succeeds!
        DlcEntitlementManager.SetLocalDlcMaskOverride((int)DlcEntitlementManager.DlcMask.ReturnToCastlevania);
        var descriptor = RunLaunchCoordinator.CreateOrReuseHostDescriptor(
            seed: 12345,
            sequence: 1,
            launchKind: "normal",
            initialLevelId: "PurpleGarden",
            initialLevelSeed: null,
            difficulty: 0,
            bossCells: 0,
            dlcFlags: (ulong)DlcEntitlementManager.DlcMask.ReturnToCastlevania,
            saveSlot: 0);

        Assert.NotNull(descriptor);
        Assert.Equal("PurpleGarden", descriptor.InitialLevelId);
    }

    [Fact]
    public void Reset_ClearsAllPeerMasksAndOverrides()
    {
        DlcEntitlementManager.SetLocalDlcMaskOverride(15);
        DlcEntitlementManager.SetPeerDlcMask(2, 7);
        DlcEntitlementManager.SetPeerDlcMask(3, 31);

        Assert.Equal(15, DlcEntitlementManager.GetLocalDlcMask());
        Assert.True(DlcEntitlementManager.TryGetPeerDlcMask(2, out var mask2));
        Assert.Equal(7, mask2);

        DlcEntitlementManager.Reset();

        Assert.False(DlcEntitlementManager.TryGetPeerDlcMask(2, out _));
        Assert.False(DlcEntitlementManager.TryGetPeerDlcMask(3, out _));
        Assert.Empty(DlcEntitlementManager.GetAllPeerDlcMasks());
    }

    [Fact]
    public void InvalidPeerIds_IgnoredInSetAndRemove()
    {
        DlcEntitlementManager.SetPeerDlcMask(0, 15);
        DlcEntitlementManager.SetPeerDlcMask(-1, 15);
        Assert.Empty(DlcEntitlementManager.GetAllPeerDlcMasks());

        DlcEntitlementManager.RemovePeerDlcMask(0);
        DlcEntitlementManager.RemovePeerDlcMask(-5);
        Assert.Empty(DlcEntitlementManager.GetAllPeerDlcMasks());
    }

    [Fact]
    public void GetAllPeerDlcMasks_ReturnsRegisteredPeers()
    {
        DlcEntitlementManager.SetPeerDlcMask(2, (int)DlcEntitlementManager.DlcMask.TheBadSeed);
        DlcEntitlementManager.SetPeerDlcMask(4, (int)DlcEntitlementManager.DlcMask.ReturnToCastlevania);

        var all = new Dictionary<int, int>(DlcEntitlementManager.GetAllPeerDlcMasks());
        Assert.Equal(2, all.Count);
        Assert.Equal((int)DlcEntitlementManager.DlcMask.TheBadSeed, all[2]);
        Assert.Equal((int)DlcEntitlementManager.DlcMask.ReturnToCastlevania, all[4]);
    }
}
