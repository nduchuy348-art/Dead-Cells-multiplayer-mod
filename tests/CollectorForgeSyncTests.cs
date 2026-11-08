using System;
using System.Collections.Generic;
using DeadCellsMultiplayerMod.Interaction;
using Xunit;

namespace DeadCellsMultiplayerMod.ProtocolReplay.Tests;

public sealed class CollectorForgeSyncTests
{
    [Fact]
    public void CollectorDepositEvent_SanitizesAndBoundsAllFields()
    {
        // Negative / malformed inputs
        var evNeg = new InterCollectorDepositEvent(
            userId: -5,
            itemId: "  melee/sword|exploit\r\n  ",
            cellsDeposited: -100,
            totalInvested: -50,
            sequence: -10L,
            levelId: "level|corrupted\r\n");

        Assert.Equal(0, evNeg.UserId);
        Assert.Equal("melee/sword/exploit", evNeg.ItemId);
        Assert.Equal(0, evNeg.CellsDeposited);
        Assert.Equal(0, evNeg.TotalInvested);
        Assert.Equal(0L, evNeg.Sequence);
        Assert.Equal("level/corrupted", evNeg.LevelId);

        // Clamping to upper limits
        var oversizedId = new string('x', 200);
        var evMax = new InterCollectorDepositEvent(
            userId: 2,
            itemId: oversizedId,
            cellsDeposited: 999_999,
            totalInvested: int.MaxValue,
            sequence: 500L,
            levelId: "PrisonersQuarters");

        Assert.Equal(2, evMax.UserId);
        Assert.Equal(InterCollectorDepositEvent.MaxItemIdLength, evMax.ItemId.Length);
        Assert.Equal(InterCollectorDepositEvent.MaxDepositCells, evMax.CellsDeposited);
        Assert.Equal(int.MaxValue, evMax.TotalInvested);
        Assert.Equal(500L, evMax.Sequence);
        Assert.Equal("PrisonersQuarters", evMax.LevelId);

        // Null strings safely handled
        var evNull = new InterCollectorDepositEvent(1, null!, 10, 10, 1L, null!);
        Assert.Equal(string.Empty, evNull.ItemId);
        Assert.Equal(string.Empty, evNull.LevelId);
    }

    [Fact]
    public void BlueprintUnlockEvent_SanitizesAndBoundsAllFields()
    {
        var ev = new InterBlueprintUnlockEvent(
            userId: -1,
            itemId: "  magic_missile|inject\n",
            sequence: -99L,
            levelId: "Passage|Level\r\n");

        Assert.Equal(0, ev.UserId);
        Assert.Equal("magic_missile/inject", ev.ItemId);
        Assert.Equal(0L, ev.Sequence);
        Assert.Equal("Passage/Level", ev.LevelId);

        var evNull = new InterBlueprintUnlockEvent(3, null!, 10L, null!);
        Assert.Equal(3, evNull.UserId);
        Assert.Equal(string.Empty, evNull.ItemId);
        Assert.Equal(string.Empty, evNull.LevelId);
    }

    [Fact]
    public void ForgeUpgradeEvent_BoundsTierAndCells()
    {
        // Negative tier and cells
        var evNeg = new InterForgeUpgradeEvent(
            userId: -10,
            upgradeLevel: -3,
            cellsDeposited: -50,
            totalInvested: -1,
            sequence: -5L,
            levelId: null!);

        Assert.Equal(0, evNeg.UserId);
        Assert.Equal(0, evNeg.UpgradeLevel);
        Assert.Equal(0, evNeg.CellsDeposited);
        Assert.Equal(0, evNeg.TotalInvested);
        Assert.Equal(0L, evNeg.Sequence);
        Assert.Equal(string.Empty, evNeg.LevelId);

        // Tier exceeding maximum (MaxForgeTier = 2 for +, ++, S)
        var evOver = new InterForgeUpgradeEvent(
            userId: 1,
            upgradeLevel: 99,
            cellsDeposited: 500_000,
            totalInvested: 1_000_000,
            sequence: 42L,
            levelId: "BlacksmithArea");

        Assert.Equal(1, evOver.UserId);
        Assert.Equal(InterForgeUpgradeEvent.MaxForgeTier, evOver.UpgradeLevel);
        Assert.Equal(InterForgeUpgradeEvent.MaxDepositCells, evOver.CellsDeposited);
        Assert.Equal(1_000_000, evOver.TotalInvested);
        Assert.Equal(42L, evOver.Sequence);
        Assert.Equal("BlacksmithArea", evOver.LevelId);
    }

    [Fact]
    public void CollectorLockEvent_NormalizesKindAndBoundsFields()
    {
        var evForge = new InterCollectorLockEvent(-1, true, "Forge", -2L, "Transit");
        Assert.Equal(0, evForge.UserId);
        Assert.True(evForge.InUse);
        Assert.Equal("Forge", evForge.Kind);
        Assert.Equal(0L, evForge.Sequence);
        Assert.Equal("Transit", evForge.LevelId);

        var evCollector = new InterCollectorLockEvent(2, false, "anything_else", 10L, null!);
        Assert.Equal(2, evCollector.UserId);
        Assert.False(evCollector.InUse);
        Assert.Equal("Collector", evCollector.Kind);
        Assert.Equal(10L, evCollector.Sequence);
        Assert.Equal(string.Empty, evCollector.LevelId);
    }

    [Fact]
    public void NetNode_InterCollectorDepositPayload_RoundTripAndValidation()
    {
        // Valid payload
        var validPayload = "2|ShieldBlueprint|15|75|4|Passage01";
        Assert.True(NetNode.TryParseInterCollectorDepositPayload(validPayload, senderId: 2, forceSenderId: true, out var ev));
        Assert.Equal(2, ev.UserId);
        Assert.Equal("ShieldBlueprint", ev.ItemId);
        Assert.Equal(15, ev.CellsDeposited);
        Assert.Equal(75, ev.TotalInvested);
        Assert.Equal(4L, ev.Sequence);
        Assert.Equal("Passage01", ev.LevelId);

        // Negative cells deposited must be rejected
        Assert.False(NetNode.TryParseInterCollectorDepositPayload("2|ShieldBlueprint|-5|75|4|Passage01", null, false, out _));

        // Negative total invested must be rejected
        Assert.False(NetNode.TryParseInterCollectorDepositPayload("2|ShieldBlueprint|5|-1|4|Passage01", null, false, out _));

        // Negative sequence must be rejected
        Assert.False(NetNode.TryParseInterCollectorDepositPayload("2|ShieldBlueprint|5|75|-4|Passage01", null, false, out _));

        // Empty itemId must be rejected
        Assert.False(NetNode.TryParseInterCollectorDepositPayload("2||5|75|4|Passage01", null, false, out _));
        Assert.False(NetNode.TryParseInterCollectorDepositPayload("2|   |5|75|4|Passage01", null, false, out _));

        // Incomplete / truncated payload must be rejected
        Assert.False(NetNode.TryParseInterCollectorDepositPayload("2|ShieldBlueprint|15", null, false, out _));
        Assert.False(NetNode.TryParseInterCollectorDepositPayload(string.Empty, null, false, out _));
        Assert.False(NetNode.TryParseInterCollectorDepositPayload(null!, null, false, out _));
    }

    [Fact]
    public void NetNode_InterBlueprintUnlockPayload_RoundTripAndValidation()
    {
        // Valid unlock line
        var valid = "3|Broadsword|12|Passage02";
        Assert.True(NetNode.TryParseInterBlueprintUnlockPayload(valid, senderId: null, forceSenderId: false, out var ev));
        Assert.Equal(3, ev.UserId);
        Assert.Equal("Broadsword", ev.ItemId);
        Assert.Equal(12L, ev.Sequence);
        Assert.Equal("Passage02", ev.LevelId);

        // Empty item rejected
        Assert.False(NetNode.TryParseInterBlueprintUnlockPayload("3||12|Passage02", null, false, out _));
        Assert.False(NetNode.TryParseInterBlueprintUnlockPayload("3|   |12|Passage02", null, false, out _));

        // Negative sequence rejected
        Assert.False(NetNode.TryParseInterBlueprintUnlockPayload("3|Broadsword|-1|Passage02", null, false, out _));

        // Missing fields rejected
        Assert.False(NetNode.TryParseInterBlueprintUnlockPayload("3|Broadsword", null, false, out _));
        Assert.False(NetNode.TryParseInterBlueprintUnlockPayload(string.Empty, null, false, out _));
    }

    [Fact]
    public void NetNode_InterForgeUpgradePayload_RoundTripAndValidation()
    {
        // Valid forge line (Tier 1 = ++)
        var valid = "1|1|25|250|7|BlacksmithRoom";
        Assert.True(NetNode.TryParseInterForgeUpgradePayload(valid, senderId: 1, forceSenderId: false, out var ev));
        Assert.Equal(1, ev.UserId);
        Assert.Equal(1, ev.UpgradeLevel);
        Assert.Equal(25, ev.CellsDeposited);
        Assert.Equal(250, ev.TotalInvested);
        Assert.Equal(7L, ev.Sequence);
        Assert.Equal("BlacksmithRoom", ev.LevelId);

        // Invalid tier (> MaxForgeTier = 2) must be rejected
        Assert.False(NetNode.TryParseInterForgeUpgradePayload("1|3|25|250|7|BlacksmithRoom", null, false, out _));
        Assert.False(NetNode.TryParseInterForgeUpgradePayload("1|99|25|250|7|BlacksmithRoom", null, false, out _));

        // Negative tier rejected
        Assert.False(NetNode.TryParseInterForgeUpgradePayload("1|-1|25|250|7|BlacksmithRoom", null, false, out _));

        // Negative cells rejected
        Assert.False(NetNode.TryParseInterForgeUpgradePayload("1|0|-5|250|7|BlacksmithRoom", null, false, out _));
        Assert.False(NetNode.TryParseInterForgeUpgradePayload("1|0|5|-250|7|BlacksmithRoom", null, false, out _));

        // Truncated payload rejected
        Assert.False(NetNode.TryParseInterForgeUpgradePayload("1|0|5", null, false, out _));
    }

    [Fact]
    public void NetNode_InterCollectorLockPayload_RoundTripAndValidation()
    {
        // Lock acquisition
        var acquire = "2|1|Collector|1|Passage";
        Assert.True(NetNode.TryParseInterCollectorLockPayload(acquire, senderId: 2, forceSenderId: true, out var evAcq));
        Assert.Equal(2, evAcq.UserId);
        Assert.True(evAcq.InUse);
        Assert.Equal("Collector", evAcq.Kind);
        Assert.Equal(1L, evAcq.Sequence);

        // Lock release
        var release = "2|0|Forge|2|Passage";
        Assert.True(NetNode.TryParseInterCollectorLockPayload(release, null, false, out var evRel));
        Assert.Equal(2, evRel.UserId);
        Assert.False(evRel.InUse);
        Assert.Equal("Forge", evRel.Kind);
        Assert.Equal(2L, evRel.Sequence);

        // Invalid format rejected
        Assert.False(NetNode.TryParseInterCollectorLockPayload("2|not_a_bool|Collector|1|Passage", null, false, out _));
        Assert.False(NetNode.TryParseInterCollectorLockPayload("2|1", null, false, out _));
    }

    [Fact]
    public void CollectorLedger_EnforcesMonotonicIncreaseAndPerItemSequenceIntegrity()
    {
        // Tests the real CollectorForgeLedger production class directly!
        var ledger = new CollectorForgeLedger();

        // Initial deposit into "SwordA": 10 cells, total 10, seq 1 -> OK
        Assert.True(ledger.ValidateAndRecordRemoteCollectorDeposit(userId: 1, itemId: "SwordA", cellsDeposited: 10, totalInvested: 10, sequence: 1));
        Assert.Equal(10, ledger.GetInvestedCells("SwordA"));

        // Subsequent deposit with strictly monotonic increase: 15 cells, total 25, seq 2 -> OK
        Assert.True(ledger.ValidateAndRecordRemoteCollectorDeposit(userId: 1, itemId: "SwordA", cellsDeposited: 15, totalInvested: 25, sequence: 2));
        Assert.Equal(25, ledger.GetInvestedCells("SwordA"));

        // Stale sequence number (seq 2 again or seq 1) for SwordA -> REJECTED (anti-duplicate)
        Assert.False(ledger.ValidateAndRecordRemoteCollectorDeposit(userId: 1, itemId: "SwordA", cellsDeposited: 15, totalInvested: 40, sequence: 2));
        Assert.False(ledger.ValidateAndRecordRemoteCollectorDeposit(userId: 1, itemId: "SwordA", cellsDeposited: 5, totalInvested: 30, sequence: 1));

        // Rollback attempt: totalInvested lower than current recorded (25 -> 20) -> REJECTED
        Assert.False(ledger.ValidateAndRecordRemoteCollectorDeposit(userId: 1, itemId: "SwordA", cellsDeposited: 5, totalInvested: 20, sequence: 3));

        // Critical Test for Out-of-Order Packets Across Different Items:
        // User 1 deposits into "BowB" with sequence 1. Even though User 1 already sent seq 2 on "SwordA",
        // "BowB" has its own sequence tracking and must NOT be rejected!
        Assert.True(ledger.ValidateAndRecordRemoteCollectorDeposit(userId: 1, itemId: "BowB", cellsDeposited: 20, totalInvested: 20, sequence: 1));
        Assert.Equal(20, ledger.GetInvestedCells("BowB"));

        // Once blueprint is unlocked, all further deposits into that item must be rejected (no wasted cells)
        Assert.True(ledger.ValidateAndRecordRemoteBlueprintUnlock(userId: 1, itemId: "SwordA", sequence: 4));
        Assert.True(ledger.IsBlueprintUnlocked("SwordA"));
        Assert.False(ledger.ValidateAndRecordRemoteCollectorDeposit(userId: 1, itemId: "SwordA", cellsDeposited: 5, totalInvested: 30, sequence: 5));
    }

    [Fact]
    public void ForgeLedger_EnforcesMonotonicIncreaseAndPerTierSequenceIntegrity()
    {
        var ledger = new CollectorForgeLedger();

        // Tier 0 (+): 50 cells, seq 1 -> OK
        Assert.True(ledger.ValidateAndRecordRemoteForgeUpgrade(userId: 2, tier: 0, cellsDeposited: 50, totalInvested: 50, sequence: 1));
        Assert.Equal(50, ledger.GetForgeInvestedCells(0));

        // Monotonic increment: 50 more cells, total 100, seq 2 -> OK
        Assert.True(ledger.ValidateAndRecordRemoteForgeUpgrade(userId: 2, tier: 0, cellsDeposited: 50, totalInvested: 100, sequence: 2));
        Assert.Equal(100, ledger.GetForgeInvestedCells(0));

        // Replay of seq 2 -> REJECTED
        Assert.False(ledger.ValidateAndRecordRemoteForgeUpgrade(userId: 2, tier: 0, cellsDeposited: 50, totalInvested: 150, sequence: 2));

        // Rollback of total invested (100 -> 80) -> REJECTED
        Assert.False(ledger.ValidateAndRecordRemoteForgeUpgrade(userId: 2, tier: 0, cellsDeposited: 10, totalInvested: 80, sequence: 3));

        // Tier 1 (++) update with seq 1 must NOT be rejected by Tier 0's seq 2
        Assert.True(ledger.ValidateAndRecordRemoteForgeUpgrade(userId: 2, tier: 1, cellsDeposited: 100, totalInvested: 100, sequence: 1));
        Assert.Equal(100, ledger.GetForgeInvestedCells(1));

        // Invalid tier (> 2) -> REJECTED
        Assert.False(ledger.ValidateAndRecordRemoteForgeUpgrade(userId: 2, tier: 3, cellsDeposited: 10, totalInvested: 10, sequence: 4));
        Assert.False(ledger.ValidateAndRecordRemoteForgeUpgrade(userId: 2, tier: -1, cellsDeposited: 10, totalInvested: 10, sequence: 4));
    }

    [Fact]
    public void CollectorForgeLedger_MutualExclusionAndWatchdogTimeout()
    {
        var ledger = new CollectorForgeLedger(lockTimeoutMs: 1000); // 1-second timeout for testing
        const int userA = 1;
        const int userB = 2;
        long tick = 10_000;

        // User A acquires Collector lock
        Assert.True(ledger.TryAcquireLock(userA, "Collector", tick, out var seq1));
        Assert.True(seq1 > 0);

        // User B attempts to acquire while User A holds it -> REJECTED
        Assert.False(ledger.TryAcquireLock(userB, "Collector", tick + 100, out _));
        Assert.True(ledger.IsLockedByOther(userB, "Collector", tick + 100, out var occupier));
        Assert.Equal(userA, occupier);

        // User A itself is NOT locked by other
        Assert.False(ledger.IsLockedByOther(userA, "Collector", tick + 100, out _));

        // Forge lock is separate: User B CAN acquire Forge while User A is at Collector
        Assert.True(ledger.TryAcquireLock(userB, "Forge", tick + 200, out var seqForge));
        Assert.True(seqForge > 0);
        Assert.True(ledger.IsLockedByOther(userA, "Forge", tick + 200, out var occupierForge));
        Assert.Equal(userB, occupierForge);

        // User A releases Collector lock cleanly
        Assert.True(ledger.TryReleaseLock(userA, "Collector", out var seqRel));
        Assert.True(seqRel > seq1);
        Assert.False(ledger.IsLockedByOther(userB, "Collector", tick + 300, out _));

        // Now User B can acquire Collector
        Assert.True(ledger.TryAcquireLock(userB, "Collector", tick + 400, out _));

        // Watchdog Timeout: advance tick past 1000ms timeout
        long expiredTick = tick + 400 + 1500;
        // User A can acquire now because User B's lock timed out!
        Assert.False(ledger.IsLockedByOther(userA, "Collector", expiredTick, out _));
        Assert.True(ledger.TryAcquireLock(userA, "Collector", expiredTick, out _));
    }

    [Fact]
    public void CollectorForgeLedger_PeerDisconnectReleasesLocksImmediately()
    {
        var ledger = new CollectorForgeLedger(lockTimeoutMs: 30_000);
        const int peerUser = 2;
        const int localUser = 1;
        long tick = 50_000;

        // Peer acquires both Collector and Forge
        Assert.True(ledger.TryAcquireLock(peerUser, "Collector", tick, out _));
        Assert.True(ledger.TryAcquireLock(peerUser, "Forge", tick, out _));
        Assert.True(ledger.IsLockedByOther(localUser, "Collector", tick, out _));
        Assert.True(ledger.IsLockedByOther(localUser, "Forge", tick, out _));

        // Peer abruptly disconnects -> release peer's locks
        ledger.ReleaseRemoteLocks(localUserId: localUser);

        // Local player can immediately use both without waiting 30 seconds
        Assert.False(ledger.IsLockedByOther(localUser, "Collector", tick + 1, out _));
        Assert.False(ledger.IsLockedByOther(localUser, "Forge", tick + 1, out _));
        Assert.True(ledger.TryAcquireLock(localUser, "Collector", tick + 1, out _));
        Assert.True(ledger.TryAcquireLock(localUser, "Forge", tick + 1, out _));
    }

    [Fact]
    public void ProfileSaveIsolation_EnsuresSaveSlotMatchesConfiguredSlot()
    {
        // Local player's configured save slot is 0 (or derived via ResolveSlotFromSources)
        const int localConfiguredSlot = 0;

        // Saving to the player's configured slot is valid
        Assert.True(MUser.ValidateProfileSaveIsolation(targetSlot: localConfiguredSlot, configuredSlot: localConfiguredSlot));

        // Attempting to save to a different slot (e.g. from peer userId) is rejected
        var peerCorruptedSlot = 1;
        Assert.False(MUser.ValidateProfileSaveIsolation(targetSlot: peerCorruptedSlot, configuredSlot: localConfiguredSlot));

        // Out-of-bounds slots (< 0 or > 5) are strictly rejected
        Assert.False(MUser.ValidateProfileSaveIsolation(targetSlot: -1, configuredSlot: localConfiguredSlot));
        Assert.False(MUser.ValidateProfileSaveIsolation(targetSlot: 6, configuredSlot: localConfiguredSlot));
        Assert.False(MUser.ValidateProfileSaveIsolation(targetSlot: 99, configuredSlot: localConfiguredSlot));
        Assert.False(MUser.ValidateProfileSaveIsolation(targetSlot: localConfiguredSlot, configuredSlot: -1));

        // Verification with priority hierarchy slot resolution
        var slotFromEnv = MUser.ResolveSlotFromSources(null, "2", 1, () => 0);
        Assert.Equal(2, slotFromEnv);
        Assert.True(MUser.ValidateProfileSaveIsolation(targetSlot: 2, configuredSlot: slotFromEnv));
        Assert.False(MUser.ValidateProfileSaveIsolation(targetSlot: 3, configuredSlot: slotFromEnv));
    }
}
