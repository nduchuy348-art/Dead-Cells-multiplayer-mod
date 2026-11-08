using dc;
using dc.en;
using dc.en.inter;
using dc.hl.types;
using dc.pr;
using dc.tool.atk;
using DeadCellsMultiplayerMod.Interface.ModuleInitializing;
using HaxeProxy.Runtime;
using ModCore.Events;
using ModCore.Events.Interfaces.Game.Hero;
using Serilog;
using System.Reflection;

namespace DeadCellsMultiplayerMod.Interaction;

public partial class InteractionSync
{
    private static bool IsLocalHostAuthority()
    {
        var net = LobbySession.NetRef;
        if (net != null && net.IsAlive)
            return net.IsHost;

        if (RunLaunchCoordinator.CurrentRole == NetRole.Client || LobbySession.CurrentRole == NetRole.Client)
            return false;

        return true;
    }

    private bool Hook_SwitchBossRune_canBeActivated(Hook_SwitchBossRune.orig_canBeActivated orig, SwitchBossRune self, Hero by)
    {
        var currentLevel = GetCurrentInteractionLevelId();
        if (!BossStemCellSync.CanModifyBossCells(IsLocalHostAuthority(), currentLevel))
            return false;

        return orig(self, by);
    }

    private void Hook_SwitchBossRune_onActivate(Hook_SwitchBossRune.orig_onActivate orig, SwitchBossRune self, Hero by, bool lp)
    {
        var currentLevel = GetCurrentInteractionLevelId();
        if (!BossStemCellSync.CanModifyBossCells(IsLocalHostAuthority(), currentLevel))
        {
            _log.Information("[InteractionSync] Suppressed SwitchBossRune.onActivate: not host authority or not in starting hub ({LevelId})", currentLevel);
            return;
        }

        orig(self, by, lp);
    }

    private void Hook_SwitchBossRune_close(Hook_SwitchBossRune.orig_close orig, SwitchBossRune self)
    {
        var isHost = IsLocalHostAuthority();
        if (!isHost && !_applyingRemoteBossRuneEvents)
        {
            orig(self);
            return;
        }

        var currentLevel = GetCurrentInteractionLevelId();
        if (!BossStemCellSync.IsStartingHubLevel(currentLevel))
        {
            _log.Warning("[InteractionSync] Suppressed SwitchBossRune.close sync outside starting hub ({LevelId})", currentLevel);
            orig(self);
            return;
        }

        orig(self);

        var net = LobbySession.NetRef;
        if (!IsNetReadyForSend(net) || !net!.IsHost)
            return;

        try
        {
            // Authoritative re-scale when host finishes tuning boss cells at altar close
            DeadCellsMultiplayerMod.Mobs.MobsSynchronization.MobsSynchronization.ReinitializeMonsterScaling("switch_boss_rune_close");

            // updateCells already sends the visual +/- edge. close publishes only the final
            // authoritative value so the peer cannot process duplicate boss-cell changes or
            // schedule overlapping reloads.
            var user = self?._level?.game?.user ?? dc.Main.Class.ME?.user;
            if (user != null)
                GameDataSync.SendBossRune(user, net);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[InteractionSync] Failed to send boss rune after SwitchBossRune.close");
        }
    }

    private void Hook_SwitchBossRune_updateCells(Hook_SwitchBossRune.orig_updateCells orig, SwitchBossRune self, bool add)
    {
        if (self == null)
            return;

        var isHost = IsLocalHostAuthority();

        // In multiplayer, non-host peers must never initiate boss rune changes locally.
        // They may only execute updateCells when replaying authoritative events from the host.
        if (!isHost && !_applyingRemoteBossRuneEvents)
        {
            _log.Warning("[InteractionSync] Suppressed local SwitchBossRune.updateCells on non-host peer.");
            return;
        }

        var currentLevel = GetCurrentInteractionLevelId();
        if (!BossStemCellSync.IsStartingHubLevel(currentLevel))
        {
            _log.Warning("[InteractionSync] Suppressed SwitchBossRune.updateCells outside starting hub ({LevelId})", currentLevel);
            return;
        }

        var net = LobbySession.NetRef;

        // updateCells performs a native main-level rebuild. Dispose the old remote render shells
        // before that rebuild starts; otherwise Boot.tryRender can visit a GhostKing whose sprite
        // group has already been destroyed and crash Game.loadMainLevel with Null access .groupName.
        if (_applyingRemoteBossRuneEvents || (IsNetReadyForSend(net) && net!.IsHost))
        {
            ModEntry.PrepareAndDisposeRemoteKingsForBossCellReload(
                add ? "boss-rune-update:add" : "boss-rune-update:remove");
        }

        // Reset stale mob scaling baseline before native main-level rebuild
        Mobs.Bosses.BossHpScaling.ResetAll();

        orig(self, add);

        if (!IsNetReadyForSend(net) || !net!.IsHost)
            return;

        try
        {
            // Clamp and sanitize host state right after updateCells to prevent desync
            var user = self?._level?.game?.user ?? dc.Main.Class.ME?.user;
            var hostMax = GameDataSync.GetHostMaxUnlockedBossRune(user);
            var effectiveBc = user != null ? GameDataSync.GetBossRuneInt(user) : 0;
            var clampedBc = BossStemCellSync.ClampBossCells(effectiveBc, hostMax);

            if (user != null && effectiveBc != clampedBc)
            {
                try { user.bossRuneActivated = clampedBc; } catch { }
                try { user.br_setActivated(clampedBc); } catch { }
                try
                {
                    var gd = user.game?.data;
                    if (gd?.cgData != null)
                        gd.cgData.numBossCells = clampedBc;
                }
                catch { }
            }

            // Re-initialize mob scaling immediately when host updates cells at the altar
            DeadCellsMultiplayerMod.Mobs.MobsSynchronization.MobsSynchronization.ReinitializeMonsterScaling("switch_boss_rune_update");

            var (x, y) = GetEntityPixelPos(self!);
            net.SendInterBossRuneUpdateCells(x, y, add, currentLevel, clampedBc, hostMax);
            if (user != null)
                GameDataSync.SendBossRune(user, net);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[InteractionSync] Failed to send boss rune updateCells");
        }
    }

    private void Hook_TreasureChest_open(Hook_TreasureChest.orig_open orig, TreasureChest self, Hero by)
    {
        orig(self, by);
        if (!_applyingRemoteChestEvents)
            TrySendTreasureChestEvent(self);
    }

    private void TrySendTreasureChestEvent(TreasureChest self)
    {
        TrySendInteractEvent(self, (x, y) => LobbySession.NetRef!.SendInterTreasureChest(x, y, GetCurrentInteractionLevelId()), "TreasureChest");
    }

    private void Hook_Hero_curse(Hook_Hero.orig_curse orig, Hero self, int count, dc.String reason, Ref<bool> hidePopup, Ref<bool> useAltSound)
    {
        if (_applyingRemoteChestEvents)
            return;

        orig(self, count, reason, hidePopup, useAltSound);
    }

    private void Hook_VineLadder_activate(Hook_VineLadder.orig_activate orig, VineLadder self)
    {
        orig(self);
        // Latched: remember it so the host keeps re-asserting it for peers that missed the event.
        RememberHostLatchedVineLadder(self);
        TrySendVineLadderEvent(self);
    }

    private void TrySendVineLadderEvent(VineLadder self)
    {
        if (_applyingRemoteVineLadderEvents)
            return;
        TrySendInteractEvent(self, (x, y) => LobbySession.NetRef!.SendInterVineLadder(x, y, GetCurrentInteractionLevelId()), "VineLadder");
    }

    private void Hook_Teleport_open(Hook_Teleport.orig_open orig, Teleport self)
    {
        orig(self);
        RememberHostLatchedTeleport(self);
        TrySendTeleportEvent(self);
    }

    private void Hook_Hero_breakBreakableGround(Hook_Hero.orig_breakBreakableGround orig, Hero self, int x, int y)
    {
        orig(self, x, y);
        if (_applyingRemoteBreakableGroundEvents)
            return;
        var net = LobbySession.NetRef;
        if (!IsNetReadyForSend(net) || ModEntry.me == null || !ReferenceEquals(self, ModEntry.me))
            return;
        try
        {
            net!.SendInterBreakableGround(x, y, GetCurrentInteractionLevelId());
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[InteractionSync] BreakableGround send failed");
        }
    }

    private void TrySendTeleportEvent(Teleport self)
    {
        if (_applyingRemoteTeleportEvents)
            return;
        TrySendInteractEvent(self, (x, y) => LobbySession.NetRef!.SendInterTeleport(x, y, GetCurrentInteractionLevelId()), "Teleport");
    }

    private void Hook_Portal_show(Hook_Portal.orig_show orig, Portal self)
    {
        orig(self);
        if (_applyingRemotePortalEvents)
            return;
        var net = LobbySession.NetRef;
        if (net != null && !net.IsHost)
            return;
        TrySendPortalEvent(self, "show");
    }

    private void Hook_Portal_close(Hook_Portal.orig_close orig, Portal self)
    {
        orig(self);
        if (_applyingRemotePortalEvents)
            return;
        var net = LobbySession.NetRef;
        if (net != null && !net.IsHost)
            return;
        TrySendPortalEvent(self, "close");
    }

    private void TrySendPortalEvent(Portal self, string action)
    {
        if (_applyingRemotePortalEvents)
            return;
        var net = LobbySession.NetRef;
        if (net != null && !net.IsHost)
            return;
        if (!IsNetReadyForSend(net))
            return;
        try
        {
            var (x, y) = GetEntityPixelPos(self!);
            net!.SendInterPortal(x, y, action, GetCurrentInteractionLevelId());
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[InteractionSync] Portal send failed action={Action}", action);
        }
    }

    private void ApplyRemoteBossRuneUpdateCells(List<InterBossRuneUpdateCellsEvent> events)
    {
        var level = ModEntry.me?._level;
        if (level?.entities == null || events == null || events.Count == 0)
            return;

        _applyingRemoteBossRuneEvents = true;
        try
        {
            foreach (var ev in events)
            {
                if (!IsInteractionEventForCurrentLevel(ev.LevelId))
                    continue;

                var effectiveLevelId = string.IsNullOrWhiteSpace(ev.LevelId) ? GetCurrentInteractionLevelId() : ev.LevelId;
                if (!BossStemCellSync.IsStartingHubLevel(effectiveLevelId))
                {
                    _log.Warning("[InteractionSync] Suppressed remote boss rune updateCells for non-hub level {LevelId}", effectiveLevelId);
                    continue;
                }

                var maxAllowed = ev.MaxAllowedBossCells ??
                    (GameDataSync.TryGetHostMaxUnlockedBossRune(out var hm) ? hm : BossStemCellSync.MaxBossCells);

                var altar = FindSwitchBossRuneByPos(level, ev.X, ev.Y);
                if (altar == null)
                {
                    _log.Warning("[InteractionSync] No SwitchBossRune found at x={X} y={Y}; applying authoritative target BC directly.", ev.X, ev.Y);
                    if (ev.TargetBossCells.HasValue)
                    {
                        GameDataSync.ReceiveBossRune($"{ev.TargetBossCells.Value}|{maxAllowed}");
                    }
                    Mobs.Bosses.BossHpScaling.ResetAll();
                    continue;
                }
                try
                {
                    altar.updateCells(ev.Add);

                    var user = altar._level?.game?.user ?? dc.Main.Class.ME?.user;

                    if (user != null)
                    {
                        var currentBc = GameDataSync.GetBossRuneInt(user);
                        var clampedBc = BossStemCellSync.ClampBossCells(currentBc, maxAllowed);
                        if (currentBc != clampedBc)
                        {
                            try { user.bossRuneActivated = clampedBc; } catch { }
                            try { user.br_setActivated(clampedBc); } catch { }
                            try
                            {
                                var gd = user.game?.data;
                                if (gd?.cgData != null)
                                    gd.cgData.numBossCells = clampedBc;
                            }
                            catch { }
                        }
                    }

                    if (ev.TargetBossCells.HasValue)
                    {
                        GameDataSync.ReceiveBossRune($"{ev.TargetBossCells.Value}|{maxAllowed}");
                    }
                    else
                    {
                        GameDataSync.RequestBossRuneHudRefreshFromRemoteState();
                        GameDataSync.PumpBossRuneHudRefresh();
                    }

                    Mobs.Bosses.BossHpScaling.ResetAll();
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "[InteractionSync] updateCells(add={Add}) failed", ev.Add);
                }
            }
        }
        finally
        {
            _applyingRemoteBossRuneEvents = false;
        }
    }

    private static SwitchBossRune? FindSwitchBossRuneByPos(Level level, double x, double y)
    {
        var found = FindInteractByPos<SwitchBossRune>(level, x, y, SwitchBossRunePosTolerance);
        if (found != null)
            return found;

        RebuildInteractionCache(level);
        found = FindInteractByPos<SwitchBossRune>(level, x, y, SwitchBossRunePosTolerance * 2.0);
        if (found != null)
            return found;

        return FindNearestByPos<SwitchBossRune>(level, x, y, 96.0 * 96.0);
    }

    private void ApplyRemoteVineLadderEvents(List<InterVineLadderEvent> events)
    {
        var level = ModEntry.me?._level;
        if (level?.entities == null || events == null || events.Count == 0)
            return;

        _applyingRemoteVineLadderEvents = true;
        try
        {
            foreach (var ev in events)
            {
                if (!IsInteractionEventForCurrentLevel(ev.LevelId))
                    continue;

                var vineLadder = FindVineLadderByPos(level, ev.X, ev.Y);
                if (vineLadder == null)
                    continue;

                // Vine ladders stay grown permanently in the current level.
                // Do not re-trigger activation or sprout VFX on repeated heartbeat packets.
                if (_appliedVineLadders.Contains(vineLadder))
                    continue;

                try
                {
                    if (vineLadder.used)
                    {
                        _appliedVineLadders.Add(vineLadder);
                        continue;
                    }
                }
                catch { }

                // The host re-asserts latched vine ladders on a heartbeat; activate() is not
                // idempotent natively, so apply each one exactly once per level.
                if (!TryClaimPersistentInteractionApply(vineLadder))
                    continue;

                _appliedVineLadders.Add(vineLadder);

                try
                {
                    vineLadder.activate();
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "[InteractionSync] Apply vine ladder event failed x={X} y={Y}", ev.X, ev.Y);
                }
            }
        }
        finally
        {
            _applyingRemoteVineLadderEvents = false;
        }
    }

    private void ApplyRemotePortalEvents(List<InterPortalEvent> events)
    {
        var level = ModEntry.me?._level;
        if (level == null || events == null || events.Count == 0)
            return;

        _applyingRemotePortalEvents = true;
        try
        {
            foreach (var ev in events)
            {
                if (!IsInteractionEventForCurrentLevel(ev.LevelId))
                    continue;

                if (ev.RequiresMissingLocalDlc(out var missingDlc))
                {
                    _log.Warning("[InteractionSync] Dropping remote portal event: level '{Level}' requires missing DLC '{Dlc}'", ev.LevelId, missingDlc);
                    continue;
                }

                var portal = FindPortalByPos(level, ev.X, ev.Y);
                if (portal == null)
                    continue;

                // Protect against native crashes if portal leads to an unowned DLC biome
                var portalDest = ResolvePortalDestinationLevelId(portal);
                if (!string.IsNullOrEmpty(portalDest) && !DlcEntitlementManager.IsLevelAccessibleLocally(portalDest, out var destReqDlc, out var destDispName))
                {
                    _log.Warning(
                        "[InteractionSync] Dropping remote portal event at x={X} y={Y}: destination '{Dest}' requires missing DLC '{Dlc}' ({DispName})",
                        ev.X,
                        ev.Y,
                        portalDest,
                        destReqDlc,
                        destDispName);
                    continue;
                }

                try
                {
                    if (ev.Action == "show")
                        portal.show();
                    else if (ev.Action == "close")
                        portal.close();
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "[InteractionSync] Apply portal event failed x={X} y={Y} action={Action}", ev.X, ev.Y, ev.Action);
                }
            }
        }
        finally
        {
            _applyingRemotePortalEvents = false;
        }
    }

    private void ApplyRemoteTeleportEvents(List<InterTeleportEvent> events)
    {
        var level = ModEntry.me?._level;
        if (level?.entities == null || events == null || events.Count == 0)
            return;

        _applyingRemoteTeleportEvents = true;
        try
        {
            foreach (var ev in events)
            {
                if (!IsInteractionEventForCurrentLevel(ev.LevelId))
                    continue;

                var teleport = FindTeleportByPos(level, ev.X, ev.Y);
                if (teleport == null)
                {
                    continue;
                }

                // See the vine ladder path: open() is re-asserted by the host heartbeat.
                if (!TryClaimPersistentInteractionApply(teleport))
                    continue;

                try
                {
                    teleport.open();
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "[InteractionSync] Apply teleport event failed x={X} y={Y}", ev.X, ev.Y);
                }
            }
        }
        finally
        {
            _applyingRemoteTeleportEvents = false;
        }
    }

    private void ApplyRemoteBreakableGroundEvents(List<InterBreakableGroundEvent> events)
    {
        var hero = ModEntry.me;
        if (hero == null || events == null || events.Count == 0)
            return;

        _applyingRemoteBreakableGroundEvents = true;
        try
        {
            _scratchAppliedBreakableGround.Clear();
            foreach (var ev in events)
            {
                if (!IsInteractionEventForCurrentLevel(ev.LevelId))
                    continue;

                var alreadyNearby = false;
                for (var i = 0; i < _scratchAppliedBreakableGround.Count; i++)
                {
                    var (ax, ay) = _scratchAppliedBreakableGround[i];
                    if (System.Math.Abs(ax - ev.X) <= BreakableGroundPosTolerance && System.Math.Abs(ay - ev.Y) <= BreakableGroundPosTolerance)
                    {
                        alreadyNearby = true;
                        break;
                    }
                }
                if (alreadyNearby)
                    continue;

                var cx = (int)System.Math.Round(ev.X);
                var cy = (int)System.Math.Round(ev.Y);
                _scratchAppliedBreakableGround.Add((ev.X, ev.Y));

                try
                {
                    hero.breakBreakableGround(cx, cy);
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "[InteractionSync] Apply breakable ground failed x={X} y={Y}", cx, cy);
                }
            }
        }
        finally
        {
            _applyingRemoteBreakableGroundEvents = false;
        }
    }

    private Teleport? FindTeleportByPos(Level level, double x, double y)
    {
        var byPos = FindInteractByPos<Teleport>(level, x, y, TeleportPosTolerance);
        if (byPos != null)
            return byPos;
        var nearest = FindNearestByPos<Teleport>(level, x, y, TeleportPosTolerance * TeleportPosTolerance * 4);
        if (nearest != null)
            return nearest;
        return FindTeleportInTriggers(level, x, y);
    }

    private static Portal? FindPortalByPos(Level level, double x, double y)
    {
        var byPos = FindInteractByPos<Portal>(level, x, y, PortalPosTolerance);
        if (byPos != null)
            return byPos;
        var nearest = FindNearestByPos<Portal>(level, x, y, PortalPosTolerance * PortalPosTolerance * 4);
        if (nearest != null)
            return nearest;
        return FindPortalInTriggers(level, x, y);
    }

    private static Portal? FindPortalInTriggers(Level level, double x, double y) =>
        FindNearestTriggerByPos<Portal>(level, x, y, PortalPosTolerance * PortalPosTolerance * 4);

    private static string ResolvePortalDestinationLevelId(Portal? portal)
    {
        if (portal == null)
            return string.Empty;
        try
        {
            var dest = portal.destMap?.id?.ToString() ?? portal.destMap?.infos?.id?.ToString();
            if (!string.IsNullOrWhiteSpace(dest))
                return dest;
            string? dlc = portal.destMap?.infos?.dlc?.ToString() ?? (portal as dynamic)?.dlc?.ToString();
            if (!string.IsNullOrWhiteSpace(dlc))
                return dlc;
            string? dynDest = (portal as dynamic)?.destLevel?.ToString() ?? (portal as dynamic)?.destName?.ToString() ?? (portal as dynamic)?.destination?.ToString();
            if (!string.IsNullOrWhiteSpace(dynDest))
                return dynDest;
        }
        catch { }
        return string.Empty;
    }

    private static Teleport? FindTeleportInTriggers(Level level, double x, double y) =>
        FindNearestTriggerByPos<Teleport>(level, x, y, TeleportPosTolerance * TeleportPosTolerance * 4);

    private static PressurePlate? FindPressurePlateByPos(Level level, double x, double y)
    {
        return FindInteractByPos<PressurePlate>(level, x, y, PlatePosTolerance);
    }

    /// <summary>
    /// Resolves a button/switch activator. Matches ATSwitch and BossRushTierButton too, since both
    /// derive from Button. Same tolerance as plates: both are grid-aligned level fixtures.
    /// </summary>
    private static dc.en.inter.button.Button? FindButtonByPos(Level level, double x, double y)
    {
        var entityButton = FindInteractByPos<dc.en.inter.button.Button>(level, x, y, PlatePosTolerance);
        if (entityButton != null)
            return entityButton;

        return FindNearestTriggerByPos<dc.en.inter.button.Button>(
            level,
            x,
            y,
            PlatePosTolerance * PlatePosTolerance * 4);
    }

    private void ApplyRemoteTreasureChestEvents(List<InterTreasureChestEvent> events)
    {
        var level = ModEntry.me?._level;
        if (level?.entities == null || events == null || events.Count == 0)
            return;

        var localHero = ModEntry.me;
        if (localHero == null)
            return;

        _applyingRemoteChestEvents = true;
        try
        {
            foreach (var ev in events)
            {
                if (!IsInteractionEventForCurrentLevel(ev.LevelId))
                    continue;

                var chest = FindTreasureChestByPos(level, ev.X, ev.Y);
                if (chest == null)
                    continue;

                try
                {
                    chest.open(localHero);
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "[InteractionSync] Apply treasure chest event failed x={X} y={Y}", ev.X, ev.Y);
                }
            }
        }
        finally
        {
            _applyingRemoteChestEvents = false;
        }
    }

    private static TreasureChest? FindTreasureChestByPos(Level level, double x, double y)
    {
        var byPos = FindInteractByPos<TreasureChest>(level, x, y, ChestPosTolerance);
        if (byPos != null)
            return byPos;
        return FindNearestTreasureChest(level, x, y);
    }

    private static TreasureChest? FindNearestTreasureChest(Level level, double x, double y) =>
        FindNearestByPos<TreasureChest>(level, x, y, ChestPosTolerance * ChestPosTolerance * 4);

    private static T? FindInteractByPos<T>(Level level, double x, double y, double tolerance = PosTolerance) where T : Entity
    {
        var candidates = GetInteractionCandidates<T>(level);
        if (candidates == null || candidates.Count == 0)
            return null;

        for (var i = 0; i < candidates.Count; i++)
        {
            var e = candidates[i];
            if (e == null)
                continue;
            try
            {
                if (e.spr != null &&
                    System.Math.Abs(e.spr.x - x) < tolerance &&
                    System.Math.Abs(e.spr.y - y) < tolerance)
                {
                    return e;
                }
            }
            catch
            {
                // ignore
            }
        }

        return null;
    }

    private bool _applyingRemoteEliteShrineEvents;

    private void Hook_EliteShrine_trigger(Hook_EliteShrine.orig_trigger orig, EliteShrine self, Entity by)
    {
        if (_applyingRemoteEliteShrineEvents)
        {
            orig(self, by);
            return;
        }

        var net = LobbySession.NetRef;
        if (net != null && !net.IsHost)
        {
            if (IsNetReadyForSend(net))
            {
                try
                {
                    var (x, y) = GetEntityPixelPos(self!);
                    if (x == 0 && y == 0 && self != null)
                    {
                        x = (self.cx + self.xr) * TileSizePx;
                        y = (self.cy + self.yr) * TileSizePx;
                    }
                    var levelId = GetCurrentInteractionLevelId();
                    net.SendInterEliteShrine(net.id, x, y, levelId);
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "[InteractionSync] EliteShrine send failed");
                }
            }
            return;
        }

        orig(self, by);

        if (!IsNetReadyForSend(net))
            return;

        try
        {
            var (x, y) = GetEntityPixelPos(self!);
            if (x == 0 && y == 0 && self != null)
            {
                x = (self.cx + self.xr) * TileSizePx;
                y = (self.cy + self.yr) * TileSizePx;
            }
            var levelId = GetCurrentInteractionLevelId();
            net!.SendInterEliteShrine(net.id, x, y, levelId);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[InteractionSync] EliteShrine send failed");
        }
    }

    private static EliteShrine? FindEliteShrine(Level level, double x, double y)
    {
        var shrine = FindInteractByPos<EliteShrine>(level, x, y, 96.0);
        if (shrine != null)
            return shrine;

        var candidates = GetInteractionCandidates<EliteShrine>(level);
        if (candidates == null || candidates.Count == 0)
            return null;

        if (candidates.Count == 1)
            return candidates[0];

        EliteShrine? closest = null;
        var minDistSq = double.MaxValue;
        for (var i = 0; i < candidates.Count; i++)
        {
            var s = candidates[i];
            if (s == null)
                continue;
            var sx = (s.cx + s.xr) * TileSizePx;
            var sy = (s.cy + s.yr) * TileSizePx;
            var dx = sx - x;
            var dy = sy - y;
            var distSq = dx * dx + dy * dy;
            if (distSq < minDistSq)
            {
                minDistSq = distSq;
                closest = s;
            }
        }

        return closest;
    }

    private void ApplyRemoteEliteShrineEvents(List<InterEliteShrineEvent> events)
    {
        var level = ModEntry.me?._level;
        if (level == null || events == null || events.Count == 0)
            return;

        var localId = LobbySession.NetRef?.id ?? 0;
        _applyingRemoteEliteShrineEvents = true;
        try
        {
            foreach (var ev in events)
            {
                if (ev.UserId > 0 && ev.UserId == localId)
                    continue;
                if (!IsInteractionEventForCurrentLevel(ev.LevelId))
                    continue;

                var shrine = FindEliteShrine(level, ev.X, ev.Y);
                if (shrine == null)
                {
                    _log.Warning("[InteractionSync] No EliteShrine found at x={X} y={Y}", ev.X, ev.Y);
                    continue;
                }

                try
                {
                    shrine.trigger(ModEntry.me);
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "[InteractionSync] Apply EliteShrine trigger failed x={X} y={Y}", ev.X, ev.Y);
                }
            }
        }
        finally
        {
            _applyingRemoteEliteShrineEvents = false;
        }
    }
}
