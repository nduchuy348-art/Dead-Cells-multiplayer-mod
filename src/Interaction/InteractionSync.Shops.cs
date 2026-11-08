using System;
using System.Collections.Generic;
using System.Threading;
using dc;
using dc.en;
using dc.en.inter;
using HaxeProxy.Runtime;
using Math = System.Math;

namespace DeadCellsMultiplayerMod.Interaction;

public partial class InteractionSync
{
    /// <summary>
    /// Authoritative reservation manager ensuring dual-pickup and simultaneous interaction
    /// races are resolved first-arrival wins, with losers receiving clean rejection and 0 gold spent.
    /// </summary>
    public static readonly ShopReservationManager ShopReservations = new();

    private long _nextShopSequence;
    private bool _applyingRemoteShopEvents;
    private bool _applyingRemotePedestalEvents;
    private bool _applyingRemoteRerollEvents;

    private void InitializeShopHooks()
    {
        Hook_ShopBooth.canBeActivated += Hook_ShopBooth_canBeActivated;
        Hook_ShopBooth.onActivate += Hook_ShopBooth_onActivate;
        Hook_ShopBooth.buy += Hook_ShopBooth_buy;
        Hook_ItemAltar.canBeActivated += Hook_ItemAltar_canBeActivated;
        Hook_ItemAltar.onActivate += Hook_ItemAltar_onActivate;
        Hook_ShopReroll.canBeActivated += Hook_ShopReroll_canBeActivated;
        Hook_ShopReroll.onActivate += Hook_ShopReroll_onActivate;
        Hook_ItemDrop.canBeActivated += Hook_ItemDrop_canBeActivated;
        Hook_ItemDrop.onActivate += Hook_ItemDrop_onActivate;
        Hook_ItemDrop.recycle += Hook_ItemDrop_recycle;
        Hook_Hero.substractMoney += Hook_Hero_substractMoney;
        Hook_Hero.tryToSubstractMoney += Hook_Hero_tryToSubstractMoney;
        Hook_Hero.addMoney += Hook_Hero_addMoney;
        Hook_Hero.substractCells += Hook_Hero_substractCells;
        Hook_Hero.hudSetMoney += Hook_Hero_hudSetMoney;
    }

    private static void EnsureNonNegativeMoney(Hero? hero)
    {
        if (hero == null) return;
        try
        {
            var data = hero._level?.game?.data;
            if (data != null && data.money < 0)
            {
                data.money = 0;
                hero.hudSetMoney(0);
            }
        }
        catch { }
    }

    private static List<(double X, double Y)>? FindLinkedAltarCoordinates(ItemAltar altar)
    {
        if (altar == null || !altar.linked)
            return null;

        var level = altar._level;
        if (level == null)
            return null;

        var candidates = GetInteractionCandidates<ItemAltar>(level);
        if (candidates == null || candidates.Count <= 1)
            return null;

        var (ax, ay) = GetEntityPixelPos(altar);
        var result = new List<(double X, double Y)>();
        for (int i = 0; i < candidates.Count; i++)
        {
            var other = candidates[i];
            if (other == null || ReferenceEquals(other, altar) || !other.linked)
                continue;

            var (ox, oy) = GetEntityPixelPos(other);
            double dx = ox - ax;
            double dy = oy - ay;
            // Altars linked in the same choice room are within 240px
            if (dx * dx + dy * dy <= 240.0 * 240.0)
            {
                result.Add((ox, oy));
            }
        }
        return result.Count > 0 ? result : null;
    }

    // -------------------------------------------------------------------------
    // ShopBooth Hooks (Equipment, Food, Weapons, Mimics)
    // -------------------------------------------------------------------------

    private bool Hook_ShopBooth_canBeActivated(Hook_ShopBooth.orig_canBeActivated orig, ShopBooth self, Hero by)
    {
        if (self == null) return false;
        var (x, y) = GetEntityPixelPos(self);
        var levelId = GetCurrentInteractionLevelId();

        if (ShopReservations.IsClaimed(levelId, x, y))
            return false;

        return orig(self, by);
    }

    private void Hook_ShopBooth_onActivate(Hook_ShopBooth.orig_onActivate orig, ShopBooth self, Hero by, bool lp)
    {
        if (self == null) return;
        if (_applyingRemoteShopEvents)
        {
            orig(self, by, lp);
            return;
        }

        var (x, y) = GetEntityPixelPos(self);
        var levelId = GetCurrentInteractionLevelId();
        int price = self.price;
        bool isMimic = self.get_isMimic();
        var net = LobbySession.NetRef;

        // Offline / single-player fallback:
        if (!IsNetReadyForSend(net))
        {
            if (!ShopReservations.TryClaim(levelId, x, y, 0, 0))
                return; // Already activated

            if (!isMimic)
            {
                var gameData = self._level?.game?.data ?? ModEntry.me?._level?.game?.data;
                int currentGold = gameData?.money ?? 0;
                if (!ShopSyncHelpers.ValidateGoldTransaction(currentGold, price, out _, out _))
                {
                    ShopReservations.Release(levelId, x, y);
                    return;
                }
            }

            orig(self, by, lp);
            return;
        }

        // Multiplayer mode:
        if (net!.IsHost)
        {
            long seq = Interlocked.Increment(ref _nextShopSequence);
            if (!ShopReservations.TryClaim(levelId, x, y, net.id, seq))
            {
                // First arrival already claimed this item. Clean rejection, 0 gold spent!
                _log.Information("[ShopSync] Host rejected duplicate shop purchase at x={X} y={Y}", x, y);
                return;
            }

            if (!isMimic)
            {
                var gameData = self._level?.game?.data ?? ModEntry.me?._level?.game?.data;
                int currentGold = gameData?.money ?? 0;
                if (!ShopSyncHelpers.ValidateGoldTransaction(currentGold, price, out _, out var reason))
                {
                    _log.Information("[ShopSync] Host rejected purchase due to invalid gold: {Reason}", reason);
                    ShopReservations.Release(levelId, x, y);
                    return;
                }
            }

            orig(self, by, lp);
            net.SendInterShopPurchase(net.id, x, y, price, seq, isMimic, levelId);
        }
        else
        {
            // Client: Check local claim state and local funds before submitting purchase request to host
            if (ShopReservations.IsClaimed(levelId, x, y))
            {
                _log.Information("[ShopSync] Client item already claimed at x={X} y={Y}. Clean rejection.", x, y);
                return;
            }

            if (!isMimic)
            {
                var gameData = self._level?.game?.data ?? ModEntry.me?._level?.game?.data;
                int currentGold = gameData?.money ?? 0;
                if (!ShopSyncHelpers.ValidateGoldTransaction(currentGold, price, out _, out var reason))
                {
                    _log.Information("[ShopSync] Client aborted purchase due to invalid funds: {Reason}", reason);
                    return;
                }
            }

            long seq = Interlocked.Increment(ref _nextShopSequence);
            net.SendInterShopPurchase(net.id, x, y, price, seq, isMimic, levelId);
        }
    }

    private void Hook_ShopBooth_buy(Hook_ShopBooth.orig_buy orig, ShopBooth self, Hero h, Ref<bool> showFx)
    {
        orig(self, h, showFx);
        EnsureNonNegativeMoney(h);
    }

    // -------------------------------------------------------------------------
    // ItemAltar Hooks (Pedestals, Choice Altars)
    // -------------------------------------------------------------------------

    private bool Hook_ItemAltar_canBeActivated(Hook_ItemAltar.orig_canBeActivated orig, ItemAltar self, Hero by)
    {
        if (self == null) return false;
        var (x, y) = GetEntityPixelPos(self);
        var levelId = GetCurrentInteractionLevelId();

        if (ShopReservations.IsClaimed(levelId, x, y))
            return false;

        return orig(self, by);
    }

    private void Hook_ItemAltar_onActivate(Hook_ItemAltar.orig_onActivate orig, ItemAltar self, Hero by, bool lp)
    {
        if (self == null) return;
        if (_applyingRemotePedestalEvents)
        {
            orig(self, by, lp);
            return;
        }

        var (x, y) = GetEntityPixelPos(self);
        var levelId = GetCurrentInteractionLevelId();
        var net = LobbySession.NetRef;
        var linkedCoords = FindLinkedAltarCoordinates(self);
        long seq = Interlocked.Increment(ref _nextShopSequence);

        if (!IsNetReadyForSend(net))
        {
            if (!ShopReservations.TryClaimLinked(levelId, x, y, linkedCoords, 0, seq))
            {
                _log.Information("[ShopSync] Offline altar already claimed at x={X} y={Y}", x, y);
                return;
            }
            orig(self, by, lp);
            return;
        }

        if (net!.IsHost)
        {
            if (!ShopReservations.TryClaimLinked(levelId, x, y, linkedCoords, net.id, seq))
            {
                _log.Information("[ShopSync] Host pedestal/choice altar already claimed at x={X} y={Y}. Clean rejection.", x, y);
                return;
            }

            orig(self, by, lp);
            net.SendInterPedestalPickup(net.id, x, y, seq, levelId);
        }
        else
        {
            if (ShopReservations.IsClaimed(levelId, x, y))
            {
                _log.Information("[ShopSync] Client pedestal/choice altar already claimed at x={X} y={Y}. Aborting request.", x, y);
                return;
            }

            net.SendInterPedestalPickup(net.id, x, y, seq, levelId);
        }
    }

    // -------------------------------------------------------------------------
    // ShopReroll Hooks (Weapon Reroll Pedestals)
    // -------------------------------------------------------------------------

    private bool Hook_ShopReroll_canBeActivated(Hook_ShopReroll.orig_canBeActivated orig, ShopReroll self, Hero by)
    {
        if (self == null || self.remainUses <= 0)
            return false;

        return orig(self, by);
    }

    private void Hook_ShopReroll_onActivate(Hook_ShopReroll.orig_onActivate orig, ShopReroll self, Hero by, bool lp)
    {
        if (self == null) return;
        if (_applyingRemoteRerollEvents)
        {
            orig(self, by, lp);
            return;
        }

        if (!ShopSyncHelpers.ValidateRerollCounter(self.remainUses, out int newRemainUses, out var reason))
        {
            _log.Information("[ShopSync] Reroll rejected: {Reason}", reason);
            return;
        }

        var (x, y) = GetEntityPixelPos(self);
        var levelId = GetCurrentInteractionLevelId();
        var net = LobbySession.NetRef;
        long seq = Interlocked.Increment(ref _nextShopSequence);

        if (!IsNetReadyForSend(net))
        {
            if (!ShopReservations.TryClaimDebounced(levelId, x, y, 0, seq, debounceMs: 1200))
                return;

            orig(self, by, lp);
            self.remainUses = newRemainUses;
            return;
        }

        if (net!.IsHost)
        {
            if (!ShopReservations.TryClaimDebounced(levelId, x, y, net.id, seq, debounceMs: 1200))
            {
                _log.Information("[ShopSync] Reroll debounce active at x={X} y={Y}. Clean rejection.", x, y);
                return;
            }

            orig(self, by, lp);
            self.remainUses = newRemainUses;
            net.SendInterShopReroll(net.id, x, y, 0, newRemainUses, seq, levelId);
        }
        else
        {
            if (ShopReservations.IsClaimed(levelId, x, y))
                return;

            net.SendInterShopReroll(net.id, x, y, 0, newRemainUses, seq, levelId);
        }
    }

    // -------------------------------------------------------------------------
    // ItemDrop Hooks (Ground Pickup / Recycle Race Prevention)
    // -------------------------------------------------------------------------

    private bool Hook_ItemDrop_canBeActivated(Hook_ItemDrop.orig_canBeActivated orig, ItemDrop self, Hero by)
    {
        if (self == null) return false;
        var (x, y) = GetEntityPixelPos(self);
        var levelId = GetCurrentInteractionLevelId();

        if (ShopReservations.IsClaimed(levelId, x, y, tolerancePx: 12.0))
            return false;

        return orig(self, by);
    }

    private void Hook_ItemDrop_onActivate(Hook_ItemDrop.orig_onActivate orig, ItemDrop self, Hero by, bool lp)
    {
        if (self == null) return;

        var (x, y) = GetEntityPixelPos(self);
        var levelId = GetCurrentInteractionLevelId();
        var net = LobbySession.NetRef;
        long seq = Interlocked.Increment(ref _nextShopSequence);

        if (!ShopReservations.TryClaim(levelId, x, y, net?.id ?? 0, seq, tolerancePx: 12.0))
        {
            _log.Information("[ShopSync] ItemDrop already claimed for pickup/recycle at x={X} y={Y}.", x, y);
            return;
        }

        orig(self, by, lp);
    }

    private void Hook_ItemDrop_recycle(Hook_ItemDrop.orig_recycle orig, ItemDrop self, Hero by)
    {
        if (self == null) return;

        var (x, y) = GetEntityPixelPos(self);
        var levelId = GetCurrentInteractionLevelId();
        var net = LobbySession.NetRef;
        long seq = Interlocked.Increment(ref _nextShopSequence);

        if (!ShopReservations.TryClaim(levelId, x, y, net?.id ?? 0, seq, tolerancePx: 12.0))
        {
            _log.Information("[ShopSync] ItemDrop already claimed, refusing duplicate recycle at x={X} y={Y}.", x, y);
            return;
        }

        orig(self, by);
        EnsureNonNegativeMoney(by);
    }

    // -------------------------------------------------------------------------
    // Hero Gold & Cells Strict Validation Hooks
    // -------------------------------------------------------------------------

    private void Hook_Hero_substractMoney(Hook_Hero.orig_substractMoney orig, Hero self, int v, Ref<bool> noStats)
    {
        if (v <= 0) return; // Prevent negative subtraction exploits

        var data = self._level?.game?.data;
        if (data != null)
        {
            int current = System.Math.Max(0, data.money);
            int clampedSub = System.Math.Min(v, current);
            orig(self, clampedSub, noStats);
            EnsureNonNegativeMoney(self);
            return;
        }

        orig(self, v, noStats);
    }

    private int Hook_Hero_tryToSubstractMoney(Hook_Hero.orig_tryToSubstractMoney orig, Hero self, int v, Ref<bool> noStats)
    {
        if (v <= 0) return 0; // Prevent negative subtraction exploits

        var data = self._level?.game?.data;
        if (data != null)
        {
            int current = System.Math.Max(0, data.money);
            if (current < v) return 0; // Strictly all-or-nothing! Zero partial deductions on insufficient funds!

            orig(self, v, noStats);
            EnsureNonNegativeMoney(self);
            return v;
        }

        return orig(self, v, noStats);
    }

    private void Hook_Hero_addMoney(Hook_Hero.orig_addMoney orig, Hero self, int v, Ref<bool> noStats)
    {
        if (v <= 0) return;

        var data = self._level?.game?.data;
        if (data != null)
        {
            int current = System.Math.Max(0, data.money);
            long sum = (long)current + v;
            int clampedAdd = sum > int.MaxValue ? (int.MaxValue - current) : v;
            if (clampedAdd <= 0) return;

            orig(self, clampedAdd, noStats);
            EnsureNonNegativeMoney(self);
            return;
        }

        orig(self, v, noStats);
    }

    private void Hook_Hero_substractCells(Hook_Hero.orig_substractCells orig, Hero self, int v, Ref<bool> noStats)
    {
        if (v <= 0) return;

        var data = self._level?.game?.data;
        int current = System.Math.Max(0, data?.cells ?? self.cells);
        int clampedSub = System.Math.Min(v, current);

        orig(self, clampedSub, noStats);

        if (data != null && data.cells < 0) data.cells = 0;
        if (self.cells < 0) self.cells = 0;
    }

    private void Hook_Hero_hudSetMoney(Hook_Hero.orig_hudSetMoney orig, Hero self, int value)
    {
        orig(self, System.Math.Max(0, value));
    }

    // -------------------------------------------------------------------------
    // Applying Remote Shop Events
    // -------------------------------------------------------------------------

    private void ApplyRemoteShopPurchaseEvents(List<InterShopPurchaseEvent> events)
    {
        var net = LobbySession.NetRef;
        var level = ModEntry.me?._level;
        if (level == null || net == null) return;

        for (int i = 0; i < events.Count; i++)
        {
            var ev = events[i];
            if (!IsInteractionEventForCurrentLevel(ev.LevelId))
                continue;

            var booth = FindNearestByPos<ShopBooth>(level, ev.X, ev.Y, 32.0 * 32.0);
            if (booth == null)
                continue;

            if (net.IsHost && ev.UserId != net.id)
            {
                // Host arbitrates remote client purchase request!
                if (!ShopReservations.TryClaim(ev.LevelId, ev.X, ev.Y, ev.UserId, ev.Sequence))
                {
                    _log.Information("[ShopSync] Host rejected duplicate shop purchase from user={UserId} at x={X} y={Y}", ev.UserId, ev.X);
                    net.SendInterShopReject(ev.UserId, ev.X, ev.Y, ev.Sequence, ev.LevelId);
                    continue;
                }

                // Approved by host: broadcast approval to all peers
                net.SendInterShopPurchase(ev.UserId, ev.X, ev.Y, ev.Price, ev.Sequence, ev.IsMimic, ev.LevelId);

                try
                {
                    if (ev.IsMimic)
                    {
                        booth.checkForMimic();
                    }
                    else
                    {
                        booth.dispose();
                    }
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "[ShopSync] Failed to apply booth consumption on host at x={X} y={Y}", ev.X, ev.Y);
                }
            }
            else
            {
                // Peer receives authoritative approval from host
                ShopReservations.TryClaim(ev.LevelId, ev.X, ev.Y, ev.UserId, ev.Sequence);

                if (ev.UserId == net.id)
                {
                    // Local client was granted purchase approval by the host!
                    try
                    {
                        if (ev.IsMimic)
                        {
                            booth.checkForMimic();
                        }
                        else
                        {
                            _applyingRemoteShopEvents = true;
                            bool showFx = true;
                            booth.buy(ModEntry.me, Ref<bool>.From(ref showFx));
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.Warning(ex, "[ShopSync] Failed to apply local confirmed purchase for booth at x={X} y={Y}", ev.X, ev.Y);
                    }
                    finally
                    {
                        _applyingRemoteShopEvents = false;
                    }
                }
                else
                {
                    // Another player bought this item or triggered the mimic
                    try
                    {
                        if (ev.IsMimic)
                        {
                            booth.checkForMimic();
                        }
                        else
                        {
                            booth.dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.Warning(ex, "[ShopSync] Failed to apply remote purchase for booth at x={X} y={Y}", ev.X, ev.Y);
                    }
                }
            }
        }
    }

    private void ApplyRemoteShopRerollEvents(List<InterShopRerollEvent> events)
    {
        var net = LobbySession.NetRef;
        var level = ModEntry.me?._level;
        if (level == null || net == null) return;

        for (int i = 0; i < events.Count; i++)
        {
            var ev = events[i];
            if (!IsInteractionEventForCurrentLevel(ev.LevelId))
                continue;

            var reroll = FindNearestByPos<ShopReroll>(level, ev.X, ev.Y, 32.0 * 32.0);
            if (reroll == null)
                continue;

            if (net.IsHost && ev.UserId != net.id)
            {
                // Host arbitrates remote reroll request
                if (!ShopSyncHelpers.ValidateRerollCounter(reroll.remainUses, out int newRemain, out _))
                {
                    _log.Information("[ShopSync] Host rejected exhausted reroll from user={UserId}", ev.UserId);
                    continue;
                }

                if (!ShopReservations.TryClaimDebounced(ev.LevelId, ev.X, ev.Y, ev.UserId, ev.Sequence, debounceMs: 1200))
                {
                    _log.Information("[ShopSync] Host debounced duplicate reroll from user={UserId}", ev.UserId);
                    continue;
                }

                try
                {
                    _applyingRemoteRerollEvents = true;
                    reroll.onActivate(ModEntry.me, true);
                    reroll.remainUses = newRemain;
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "[ShopSync] Failed host execution of remote reroll");
                }
                finally
                {
                    _applyingRemoteRerollEvents = false;
                }

                net.SendInterShopReroll(ev.UserId, ev.X, ev.Y, ev.Cost, newRemain, ev.Sequence, ev.LevelId);
            }
            else
            {
                try
                {
                    reroll.remainUses = System.Math.Max(0, ev.RemainUses);
                    if (ev.UserId == net.id)
                    {
                        _applyingRemoteRerollEvents = true;
                        reroll.onActivate(ModEntry.me, true);
                    }
                    else if (ev.RemainUses <= 0)
                    {
                        reroll.destroyWithFx();
                    }
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "[ShopSync] Failed to apply remote reroll at x={X} y={Y}", ev.X, ev.Y);
                }
                finally
                {
                    _applyingRemoteRerollEvents = false;
                }
            }
        }
    }

    private void ApplyRemotePedestalPickupEvents(List<InterPedestalPickupEvent> events)
    {
        var net = LobbySession.NetRef;
        var level = ModEntry.me?._level;
        if (level == null || net == null) return;

        for (int i = 0; i < events.Count; i++)
        {
            var ev = events[i];
            if (!IsInteractionEventForCurrentLevel(ev.LevelId))
                continue;

            var altar = FindNearestByPos<ItemAltar>(level, ev.X, ev.Y, 32.0 * 32.0);
            if (altar == null)
                continue;

            var linkedCoords = FindLinkedAltarCoordinates(altar);

            if (net.IsHost && ev.UserId != net.id)
            {
                // Host arbitrates remote client pickup request!
                if (!ShopReservations.TryClaimLinked(ev.LevelId, ev.X, ev.Y, linkedCoords, ev.UserId, ev.Sequence))
                {
                    _log.Information("[ShopSync] Host rejected race for pedestal pickup from user={UserId} at x={X} y={Y}", ev.UserId, ev.X);
                    net.SendInterPedestalReject(ev.UserId, ev.X, ev.Y, ev.Sequence, ev.LevelId);
                    continue;
                }

                // Approved by host: broadcast approval to all peers
                net.SendInterPedestalPickup(ev.UserId, ev.X, ev.Y, ev.Sequence, ev.LevelId);

                // Disable the altar and linked altars on host
                try
                {
                    altar.disable(true);
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "[ShopSync] Failed to disable pedestal altar at x={X} y={Y}", ev.X, ev.Y);
                }
            }
            else
            {
                // Peer receives authoritative approval from host
                ShopReservations.TryClaimLinked(ev.LevelId, ev.X, ev.Y, linkedCoords, ev.UserId, ev.Sequence);

                if (ev.UserId == net.id)
                {
                    // Local client was granted pickup approval by the host!
                    try
                    {
                        _applyingRemotePedestalEvents = true;
                        altar.onActivate(ModEntry.me, true);
                    }
                    catch (Exception ex)
                    {
                        _log.Warning(ex, "[ShopSync] Failed to activate local approved pedestal at x={X} y={Y}", ev.X, ev.Y);
                    }
                    finally
                    {
                        _applyingRemotePedestalEvents = false;
                    }
                }
                else
                {
                    // Remote peer picked up this pedestal or linked choice
                    try
                    {
                        altar.disable(true);
                    }
                    catch (Exception ex)
                    {
                        _log.Warning(ex, "[ShopSync] Failed to apply remote pedestal disable at x={X} y={Y}", ev.X, ev.Y);
                    }
                }
            }
        }
    }

    private void ApplyRemoteShopRejectEvents(List<InterShopRejectEvent> events)
    {
        var net = LobbySession.NetRef;
        if (net == null) return;

        for (int i = 0; i < events.Count; i++)
        {
            var ev = events[i];
            if (ev.UserId == net.id)
            {
                _log.Information("[ShopSync] Local shop purchase request rejected by host (sold out or race lost). 0 gold spent.");
            }
        }
    }

    private void ApplyRemotePedestalRejectEvents(List<InterPedestalRejectEvent> events)
    {
        var net = LobbySession.NetRef;
        if (net == null) return;

        for (int i = 0; i < events.Count; i++)
        {
            var ev = events[i];
            if (ev.UserId == net.id)
            {
                _log.Information("[ShopSync] Local pedestal pickup request rejected by host (already taken or race lost).");
            }
        }
    }
}
