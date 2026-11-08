using System;
using System.Collections.Generic;
using Math = System.Math;
using dc;
using dc.en;
using dc.en.inter.npc;
using dc.tool;
using dc.ui;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using Serilog;

namespace DeadCellsMultiplayerMod.Interaction;

public partial class InteractionSync
{
    /// <summary>
    /// Thread-safe authoritative ledger for Collector deposits, blueprint unlocks,
    /// Legendary Forge upgrades, and distributed mutual exclusion locks.
    /// </summary>
    public static readonly CollectorForgeLedger CollectorLedger = new();

    private bool _applyingRemoteCollectorEvents;
    private bool _applyingRemoteForgeEvents;

    internal void InitializeCollectorForgeHooks()
    {
        Hook_CollectorPanelNpc.onActivate += Hook_CollectorPanelNpc_onActivate;
        Hook_CollectorPanelNpc.openPanel += Hook_CollectorPanelNpc_openPanel;
        Hook_CollectorPanelNpc.onPanelClose += Hook_CollectorPanelNpc_onPanelClose;

        Hook_CollectorPanel.close += Hook_CollectorPanel_close;
        Hook_CollectorPanel.onDispose += Hook_CollectorPanel_onDispose;

        Hook_Blacksmith.onActivate += Hook_Blacksmith_onActivate;
        Hook_Blacksmith.openUI += Hook_Blacksmith_openUI;
        Hook_ForgeLegendary.initLegendaryForge += Hook_ForgeLegendary_initLegendaryForge;
        Hook_ForgeLegendary.onDispose += Hook_ForgeLegendary_onDispose;

        Hook_ItemMetaManager.investOnItemProgress += Hook_ItemMetaManager_investOnItemProgress;
        Hook_ItemMetaManager.unlockItem += Hook_ItemMetaManager_unlockItem;
        Hook_ItemMetaManager.f_investOn += Hook_ItemMetaManager_f_investOn;
    }

    // -------------------------------------------------------------------------
    // Collector Panel Hooks & Mutual Exclusion
    // -------------------------------------------------------------------------

    private void Hook_CollectorPanelNpc_onActivate(
        Hook_CollectorPanelNpc.orig_onActivate orig,
        CollectorPanelNpc self,
        Hero by,
        bool lp)
    {
        var net = LobbySession.NetRef;
        if (IsNetReadyForSend(net))
        {
            var nowTick = Environment.TickCount64;
            if (CollectorLedger.IsLockedByOther(net!.id, "Collector", nowTick, out var occupyingUser))
            {
                _log.Information(
                    "[InteractionSync] Collector currently occupied by peer {PeerId}; suppressing activation",
                    occupyingUser);
                return;
            }
        }

        orig(self, by, lp);
    }

    private void Hook_CollectorPanelNpc_openPanel(
        Hook_CollectorPanelNpc.orig_openPanel orig,
        CollectorPanelNpc self)
    {
        orig(self);

        var net = LobbySession.NetRef;
        if (!IsNetReadyForSend(net))
            return;

        var nowTick = Environment.TickCount64;
        if (CollectorLedger.TryAcquireLock(net!.id, "Collector", nowTick, out var seq))
        {
            try
            {
                net.SendInterCollectorLock(net.id, inUse: true, "Collector", seq, GetCurrentInteractionLevelId());
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "[InteractionSync] Failed to broadcast Collector lock acquisition");
            }
        }
    }

    private void Hook_CollectorPanelNpc_onPanelClose(
        Hook_CollectorPanelNpc.orig_onPanelClose orig,
        CollectorPanelNpc self)
    {
        ReleaseLocalCollectorLock();
        orig(self);
    }

    private void Hook_CollectorPanel_close(
        Hook_CollectorPanel.orig_close orig,
        CollectorPanel self)
    {
        ReleaseLocalCollectorLock();
        orig(self);
    }

    private void Hook_CollectorPanel_onDispose(
        Hook_CollectorPanel.orig_onDispose orig,
        CollectorPanel self)
    {
        ReleaseLocalCollectorLock();
        orig(self);
    }

    private void ReleaseLocalCollectorLock()
    {
        var net = LobbySession.NetRef;
        if (!IsNetReadyForSend(net))
            return;

        if (CollectorLedger.TryReleaseLock(net!.id, "Collector", out var seq))
        {
            try
            {
                net.SendInterCollectorLock(net.id, inUse: false, "Collector", seq, GetCurrentInteractionLevelId());
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "[InteractionSync] Failed to broadcast Collector lock release");
            }
        }
    }

    // -------------------------------------------------------------------------
    // Blacksmith & Legendary Forge Hooks & Mutual Exclusion
    // -------------------------------------------------------------------------

    private void Hook_Blacksmith_onActivate(
        Hook_Blacksmith.orig_onActivate orig,
        Blacksmith self,
        Hero by,
        bool lp)
    {
        var net = LobbySession.NetRef;
        if (IsNetReadyForSend(net))
        {
            var nowTick = Environment.TickCount64;
            if (CollectorLedger.IsLockedByOther(net!.id, "Forge", nowTick, out var occupyingUser))
            {
                _log.Information(
                    "[InteractionSync] Legendary Forge currently occupied by peer {PeerId}; suppressing activation",
                    occupyingUser);
                return;
            }
        }

        orig(self, by, lp);
    }

    private void Hook_Blacksmith_openUI(
        Hook_Blacksmith.orig_openUI orig,
        Blacksmith self)
    {
        orig(self);

        var net = LobbySession.NetRef;
        if (!IsNetReadyForSend(net))
            return;

        var nowTick = Environment.TickCount64;
        if (CollectorLedger.TryAcquireLock(net!.id, "Forge", nowTick, out var seq))
        {
            try
            {
                net.SendInterCollectorLock(net.id, inUse: true, "Forge", seq, GetCurrentInteractionLevelId());
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "[InteractionSync] Failed to broadcast Forge lock acquisition via Blacksmith.openUI");
            }
        }
    }

    private void Hook_ForgeLegendary_initLegendaryForge(
        Hook_ForgeLegendary.orig_initLegendaryForge orig,
        ForgeLegendary self)
    {
        var net = LobbySession.NetRef;
        if (IsNetReadyForSend(net))
        {
            var nowTick = Environment.TickCount64;
            if (CollectorLedger.IsLockedByOther(net!.id, "Forge", nowTick, out var occupyingUser))
            {
                _log.Information(
                    "[InteractionSync] Legendary Forge init suppressed; occupied by peer {PeerId}",
                    occupyingUser);
                return;
            }

            if (CollectorLedger.TryAcquireLock(net.id, "Forge", nowTick, out var seq))
            {
                try
                {
                    net.SendInterCollectorLock(net.id, inUse: true, "Forge", seq, GetCurrentInteractionLevelId());
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "[InteractionSync] Failed to broadcast Forge lock acquisition");
                }
            }
        }

        orig(self);
    }

    private void Hook_ForgeLegendary_onDispose(
        Hook_ForgeLegendary.orig_onDispose orig,
        ForgeLegendary self)
    {
        ReleaseLocalForgeLock();
        orig(self);
    }

    private void ReleaseLocalForgeLock()
    {
        var net = LobbySession.NetRef;
        if (!IsNetReadyForSend(net))
            return;

        if (CollectorLedger.TryReleaseLock(net!.id, "Forge", out var seq))
        {
            try
            {
                net.SendInterCollectorLock(net.id, inUse: false, "Forge", seq, GetCurrentInteractionLevelId());
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "[InteractionSync] Failed to broadcast Forge lock release");
            }
        }
    }

    // -------------------------------------------------------------------------
    // Local Deposit & Unlock Interception
    // -------------------------------------------------------------------------

    private bool Hook_ItemMetaManager_investOnItemProgress(
        Hook_ItemMetaManager.orig_investOnItemProgress orig,
        ItemMetaManager self,
        dc.String k)
    {
        if (_applyingRemoteCollectorEvents)
            return orig(self, k);

        var keyStr = k?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(keyStr))
            return false;

        // Disallow spending if Collector is occupied by another peer
        var net = LobbySession.NetRef;
        if (IsNetReadyForSend(net))
        {
            var nowTick = Environment.TickCount64;
            if (CollectorLedger.IsLockedByOther(net!.id, "Collector", nowTick, out _))
                return false;
        }

        // Disallow spending into already unlocked blueprints
        if (CollectorLedger.IsBlueprintUnlocked(keyStr) || self.hasUnlockedItem(k))
            return false;

        var hero = ModEntry.me;
        if (hero == null || hero.cells <= 0)
            return false;

        var progress = self.getItemProgress(k);
        var beforeInvested = progress?.investedCells ?? 0;

        var success = orig(self, k);
        if (!success)
            return false;

        var afterInvested = progress?.investedCells ?? beforeInvested;
        var delta = Math.Clamp(afterInvested - beforeInvested, 0, InterCollectorDepositEvent.MaxDepositCells);

        if (delta > 0 && IsNetReadyForSend(net))
        {
            CollectorLedger.RecordLocalCollectorDeposit(keyStr, afterInvested, out var seq);
            try
            {
                net!.SendInterCollectorDeposit(net.id, keyStr, delta, afterInvested, seq, GetCurrentInteractionLevelId());
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "[InteractionSync] Failed to send Collector deposit for item {Item}", keyStr);
            }
        }

        return true;
    }

    private bool Hook_ItemMetaManager_unlockItem(
        Hook_ItemMetaManager.orig_unlockItem orig,
        ItemMetaManager self,
        dc.String k)
    {
        if (_applyingRemoteCollectorEvents)
            return orig(self, k);

        var success = orig(self, k);
        if (!success)
            return false;

        var keyStr = k?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(keyStr))
            return true;

        CollectorLedger.RecordLocalBlueprintUnlock(keyStr, out var seq);

        var net = LobbySession.NetRef;
        if (IsNetReadyForSend(net))
        {
            try
            {
                net!.SendInterBlueprintUnlock(net.id, keyStr, seq, GetCurrentInteractionLevelId());
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "[InteractionSync] Failed to send Blueprint unlock for {Item}", keyStr);
            }
        }

        return true;
    }

    private bool Hook_ItemMetaManager_f_investOn(
        Hook_ItemMetaManager.orig_f_investOn orig,
        ItemMetaManager self,
        int upLevel)
    {
        if (_applyingRemoteForgeEvents)
            return orig(self, upLevel);

        upLevel = Math.Clamp(upLevel, 0, InterForgeUpgradeEvent.MaxForgeTier);

        // Disallow spending if Forge is occupied by another peer
        var net = LobbySession.NetRef;
        if (IsNetReadyForSend(net))
        {
            var nowTick = Environment.TickCount64;
            if (CollectorLedger.IsLockedByOther(net!.id, "Forge", nowTick, out _))
                return false;
        }

        var hero = ModEntry.me;
        if (hero == null || hero.cells <= 0)
            return false;

        if (self.f_isBarComplete(upLevel))
            return false;

        var beforeInvested = (int)self.f_getInvestedCells(upLevel);
        var success = orig(self, upLevel);
        if (!success)
            return false;

        var afterInvested = (int)self.f_getInvestedCells(upLevel);
        var delta = Math.Clamp(afterInvested - beforeInvested, 0, InterForgeUpgradeEvent.MaxDepositCells);

        if (delta > 0 && IsNetReadyForSend(net))
        {
            CollectorLedger.RecordLocalForgeUpgrade(upLevel, afterInvested, out var seq);
            try
            {
                net!.SendInterForgeUpgrade(net.id, upLevel, delta, afterInvested, seq, GetCurrentInteractionLevelId());
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "[InteractionSync] Failed to send Forge upgrade for tier {Tier}", upLevel);
            }
        }

        return true;
    }

    // -------------------------------------------------------------------------
    // Remote Event Processing
    // -------------------------------------------------------------------------

    private void ApplyRemoteCollectorDepositEvents(List<InterCollectorDepositEvent> events)
    {
        var localId = LobbySession.NetRef?.id ?? 0;
        var user = dc.Main.Class.ME?.user;
        var itemMeta = user?.itemMeta;

        _applyingRemoteCollectorEvents = true;
        try
        {
            foreach (var ev in events)
            {
                if (ev.UserId > 0 && ev.UserId == localId)
                    continue;
                if (!IsInteractionEventForCurrentLevel(ev.LevelId))
                    continue;

                if (!CollectorLedger.ValidateAndRecordRemoteCollectorDeposit(ev.UserId, ev.ItemId, ev.CellsDeposited, ev.TotalInvested, ev.Sequence))
                    continue;

                if (itemMeta == null || string.IsNullOrWhiteSpace(ev.ItemId))
                    continue;

                try
                {
                    var hlKey = ev.ItemId.AsHaxeString();
                    var progress = itemMeta.getItemProgress(hlKey);
                    if (progress != null && ev.TotalInvested > progress.investedCells)
                    {
                        progress.investedCells = ev.TotalInvested;
                    }
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "[InteractionSync] Failed to apply remote Collector deposit for {ItemId}", ev.ItemId);
                }
            }
        }
        finally
        {
            _applyingRemoteCollectorEvents = false;
        }
    }

    private void ApplyRemoteBlueprintUnlockEvents(List<InterBlueprintUnlockEvent> events)
    {
        var localId = LobbySession.NetRef?.id ?? 0;
        var user = dc.Main.Class.ME?.user;
        var itemMeta = user?.itemMeta;

        _applyingRemoteCollectorEvents = true;
        try
        {
            foreach (var ev in events)
            {
                if (ev.UserId > 0 && ev.UserId == localId)
                    continue;
                if (!IsInteractionEventForCurrentLevel(ev.LevelId))
                    continue;

                if (!CollectorLedger.ValidateAndRecordRemoteBlueprintUnlock(ev.UserId, ev.ItemId, ev.Sequence))
                    continue;

                if (itemMeta == null || string.IsNullOrWhiteSpace(ev.ItemId))
                    continue;

                try
                {
                    var hlKey = ev.ItemId.AsHaxeString();
                    if (!itemMeta.hasUnlockedItem(hlKey))
                    {
                        itemMeta.unlockItem(hlKey);
                        _log.Information("[InteractionSync] BluePrint {Item} unlocked by peer {PeerId}", ev.ItemId, ev.UserId);
                    }
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "[InteractionSync] Failed to apply remote blueprint unlock for {ItemId}", ev.ItemId);
                }
            }
        }
        finally
        {
            _applyingRemoteCollectorEvents = false;
        }
    }

    private void ApplyRemoteForgeUpgradeEvents(List<InterForgeUpgradeEvent> events)
    {
        var localId = LobbySession.NetRef?.id ?? 0;
        var user = dc.Main.Class.ME?.user;
        var itemMeta = user?.itemMeta;

        _applyingRemoteForgeEvents = true;
        try
        {
            foreach (var ev in events)
            {
                if (ev.UserId > 0 && ev.UserId == localId)
                    continue;
                if (!IsInteractionEventForCurrentLevel(ev.LevelId))
                    continue;

                if (!CollectorLedger.ValidateAndRecordRemoteForgeUpgrade(ev.UserId, ev.UpgradeLevel, ev.CellsDeposited, ev.TotalInvested, ev.Sequence))
                    continue;

                if (itemMeta == null)
                    continue;

                try
                {
                    var forgeMap = itemMeta.forgeInvestedCells;
                    if (forgeMap != null)
                    {
                        var current = (int)itemMeta.f_getInvestedCells(ev.UpgradeLevel);
                        if (ev.TotalInvested > current)
                        {
                            forgeMap.set(ev.UpgradeLevel, ev.TotalInvested);
                            _log.Information(
                                "[InteractionSync] Forge tier {Tier} invested cells updated to {Total} by peer {PeerId}",
                                ev.UpgradeLevel,
                                ev.TotalInvested,
                                ev.UserId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "[InteractionSync] Failed to apply remote Forge upgrade for tier {Tier}", ev.UpgradeLevel);
                }
            }
        }
        finally
        {
            _applyingRemoteForgeEvents = false;
        }
    }

    private void ApplyRemoteCollectorLockEvents(List<InterCollectorLockEvent> events)
    {
        var localId = LobbySession.NetRef?.id ?? 0;
        var nowTick = Environment.TickCount64;

        foreach (var ev in events)
        {
            if (ev.UserId > 0 && ev.UserId == localId)
                continue;
            if (!IsInteractionEventForCurrentLevel(ev.LevelId))
                continue;

            CollectorLedger.ApplyRemoteLock(ev.UserId, ev.InUse, ev.Kind, ev.Sequence, nowTick);
        }
    }

    /// <summary>
    /// Fortified check ensuring local profile save isolation:
    /// Validates that one player's spent cells or remote interaction state cannot overwrite or corrupt another player's save slot.
    /// </summary>
    internal static bool ValidateSaveSlotIsolation(User? user, int targetSlot)
    {
        if (user == null)
            return false;

        return MUser.ValidateProfileSaveIsolation(targetSlot);
    }
}
