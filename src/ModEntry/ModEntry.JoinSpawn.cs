using System;
using System.Diagnostics;
using dc;
using dc.en;
using dc.pr;

namespace DeadCellsMultiplayerMod
{
    /// <summary>
    /// Host-approved safe spawn for a player who joins a run that is already in progress.
    /// </summary>
    /// <remarks>
    /// A joining client rebuilds the host's level and then starts at that level's native entrance.
    /// That is correct for a coordinated transition (both players walk in together) but wrong for a
    /// mid-run join: the host may be far past one-way progression — an activated vine, a dropped
    /// ledge, a sealed door — so the joiner starts behind geometry it cannot pass and is softlocked.
    /// Ramparts is where this was reported, but nothing about it is Ramparts-specific.
    ///
    /// The fix places the joiner where the host is standing. That cell needs no collision
    /// heuristics to justify: a live hero occupies it, so by construction it is inside the level,
    /// inside a room, non-solid and reachable. The client still re-validates it against its OWN map
    /// before moving, which doubles as a world-agreement check — if the client has no room there,
    /// the two peers did not generate the same world, and teleporting would be strictly worse than
    /// staying at the entrance.
    ///
    /// Ordering matters and is enforced rather than timed: this only runs once the level exists,
    /// the hero is alive in it, and the authoritative level graph has already been applied (the
    /// graph wait happens earlier, inside LevelGen). Failure at any step leaves the native entrance
    /// untouched.
    /// </remarks>
    public partial class ModEntry
    {
        private bool _joinSpawnArmed;
        private long _joinSpawnArmedTicks;
        private string _joinSpawnLevelId = string.Empty;
        private long _nextJoinSpawnAttemptTicks;

        public bool IsJoinSpawnArmed => _joinSpawnArmed;

        internal void ResetHostSpawnAnchorTimer()
        {
            _nextHostSpawnAnchorTicks = 0;
        }

        /// <summary>Give the level a moment to finish building before reading its collision data.</summary>
        private const double JoinSpawnRetryIntervalSeconds = 0.25;

        /// <summary>Stop trying rather than teleporting a player who has already started moving.</summary>
        private const double JoinSpawnGiveUpSeconds = 20.0;

        private void ArmJoinSpawnForCurrentLevel(string? levelId)
        {
            if (!RunLaunchFlow.TryConsumeMidRunJoinSpawn())
                return;

            _joinSpawnArmed = true;
            _joinSpawnArmedTicks = Stopwatch.GetTimestamp();
            _joinSpawnLevelId = levelId ?? string.Empty;
            _nextJoinSpawnAttemptTicks = 0;
            Logger.Information(
                "[NetMod][Session] Armed host-approved join spawn for level={Level}",
                _joinSpawnLevelId);
        }

        private void TryApplyHostApprovedJoinSpawn()
        {
            if (!_joinSpawnArmed)
                return;

            var net = _net;
            if (net == null || !net.IsAlive || net.IsHost)
            {
                _joinSpawnArmed = false;
                return;
            }

            var now = Stopwatch.GetTimestamp();
            if (_nextJoinSpawnAttemptTicks != 0 && now < _nextJoinSpawnAttemptTicks)
                return;
            _nextJoinSpawnAttemptTicks = now + (long)(Stopwatch.Frequency * JoinSpawnRetryIntervalSeconds);

            if (Stopwatch.GetElapsedTime(_joinSpawnArmedTicks).TotalSeconds >= JoinSpawnGiveUpSeconds)
            {
                _joinSpawnArmed = false;
                Logger.Warning(
                    "[NetMod][Session] Gave up on host-approved join spawn for level={Level}: " +
                    "no usable host anchor arrived. Keeping the native entrance.",
                    _joinSpawnLevelId);
                return;
            }

            // Never relocate the hero out from under a scripted sequence or a downed body. Both
            // own the hero's position, and moving it there produces exactly the stuck states this
            // is supposed to prevent. Keep waiting instead; the give-up timer bounds it.
            if (IsLocalBossIntroCineActive() || IsLocalPlayerDowned())
                return;

            var hero = me;
            if (hero == null || hero.destroyed || hero.life <= 0)
                return;

            if (hero._level == null || hero._level.destroyed)
                return;

            var level = hero._level;
            if (level.map == null)
                return;

            string currentLevelId;
            try
            {
                currentLevelId = level.map.id?.ToString() ?? string.Empty;
            }
            catch
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(currentLevelId))
                return;

            // Viewport pointer guard: ensure the level viewport has fully initialized before moving hero
            var vp = level.viewport;
            if (vp == null)
                return;

            if (!net.TryGetHostSpawnAnchor(out var anchor))
                return;

            // The anchor must describe the level this client is actually standing in.
            if (anchor.LevelId.Length > 0 &&
                currentLevelId.Length > 0 &&
                !string.Equals(anchor.LevelId, currentLevelId, StringComparison.Ordinal))
            {
                return;
            }

            // Combat safety guard: if host is in combat (or fighting a boss), keep client safely at the native entrance.
            if (anchor.InCombat)
            {
                _joinSpawnArmed = false;
                Logger.Information(
                    "[NetMod][Session] Host is actively in combat in level={Level}; keeping client safely at native entrance instead of teleporting into fight.",
                    currentLevelId);
                return;
            }

            // Validate against this client's own map. A missing room here means the worlds diverged.
            object? hostRoom;
            try
            {
                hostRoom = level.map.getRoomAt(anchor.Cx, anchor.Cy);
            }
            catch (Exception ex)
            {
                Logger.Warning("[NetMod][Session] Join spawn validation failed: {Message}", ex.Message);
                _joinSpawnArmed = false;
                return;
            }

            if (hostRoom == null)
            {
                Logger.Error(
                    "[NetMod][Session] WORLD DESYNC: host stands at {Cx}:{Cy} in {Level}, but this client has no room there. " +
                    "Keeping the native entrance instead of relocating.",
                    anchor.Cx,
                    anchor.Cy,
                    currentLevelId);
                _joinSpawnArmed = false;
                return;
            }

            // Find a safe, non-solid anchor point near host with solid ground beneath to avoid clipping into tiles or void.
            if (!TryFindSafeSpawnAnchor(level.map, anchor.Cx, anchor.Cy, out var safeCx, out var safeCy))
            {
                Logger.Warning(
                    "[NetMod][Session] No safe non-solid ground found near host at {Cx}:{Cy} in {Level}; " +
                    "keeping native entrance to avoid clipping.",
                    anchor.Cx,
                    anchor.Cy,
                    currentLevelId);
                _joinSpawnArmed = false;
                return;
            }

            try
            {
                hero.setPosCase(safeCx, safeCy, null, null);
                _joinSpawnArmed = false;
                Logger.Information(
                    "[NetMod][Session] Applied host-approved safe join spawn at {Cx}:{Cy} (host was at {HostCx}:{HostCy}) in {Level}",
                    safeCx,
                    safeCy,
                    anchor.Cx,
                    anchor.Cy,
                    currentLevelId);

                // Snap camera and viewport immediately to the newly placed hero
                try
                {
                    vp.track(hero, true);
                }
                catch (Exception ex)
                {
                    Logger.Warning("[NetMod][Session] Viewport tracking after join spawn failed: {Message}", ex.Message);
                }

                MarkDiveNetGuardAfterSpawnOrRoomChange();
                SendCurrentRoomTarget(force: true);
            }
            catch (Exception ex)
            {
                _joinSpawnArmed = false;
                Logger.Warning("[NetMod][Session] Join spawn relocation failed: {Message}", ex.Message);
            }
        }

        /// <summary>
        /// Finds a safe anchor point near the target tile that is inside a room, non-solid for the hero body/head,
        /// and has solid ground or a platform directly beneath to prevent clipping into void or death pits.
        /// </summary>
        internal static bool TryFindSafeSpawnAnchor(dc.level.LevelMap? map, int targetCx, int targetCy, out int safeCx, out int safeCy)
        {
            safeCx = targetCx;
            safeCy = targetCy;

            if (map == null || map.wid <= 0 || map.hei <= 0)
                return false;

            // Search offsets starting at target position (0,0), then horizontally adjacent, then vertically
            // to find a non-solid position inside a valid room that has solid ground beneath it.
            var offsets = new (int dx, int dy)[]
            {
                (0, 0),
                (0, 1),
                (0, -1),
                (-1, 0),
                (1, 0),
                (-1, 1),
                (1, 1),
                (-2, 0),
                (2, 0),
                (-2, 1),
                (2, 1),
                (-3, 0),
                (3, 0),
                (0, 2),
                (-1, -1),
                (1, -1)
            };

            for (int i = 0; i < offsets.Length; i++)
            {
                var candidateX = targetCx + offsets[i].dx;
                var candidateY = targetCy + offsets[i].dy;

                // Candidate must allow room for head (candidateY - 1 >= 0) and ground beneath (candidateY + 1 < map.hei)
                if (candidateX < 0 || candidateX >= map.wid || candidateY < 1 || candidateY + 1 >= map.hei)
                    continue;

                // 1. Must be in a valid room
                object? room;
                try
                {
                    room = map.getRoomAt(candidateX, candidateY);
                }
                catch
                {
                    continue;
                }

                if (room == null)
                    continue;

                // 2. Head and body space must NOT be solid (not embedded in wall or ceiling)
                if (Ghost.CombatSyncHelpers.IsSolidTile(map, candidateX, candidateY))
                    continue;

                if (Ghost.CombatSyncHelpers.IsSolidTile(map, candidateX, candidateY - 1))
                    continue;

                // 3. Must have solid ground or wood platform beneath (not empty void or death pit)
                if (Ghost.CombatSyncHelpers.IsSolidTile(map, candidateX, candidateY + 1) ||
                    Ghost.CombatSyncHelpers.IsWoodTile(map, candidateX, candidateY + 1))
                {
                    safeCx = candidateX;
                    safeCy = candidateY;
                    return true;
                }
            }

            return false;
        }

        private bool IsHostInCombat(Hero? hero)
        {
            if (hero == null)
                return false;

            try
            {
                if (hero.destroyed || hero.life <= 0)
                    return false;

                if (IsLocalBossIntroCineActive())
                    return true;

                if (DeadCellsMultiplayerMod.Mobs.MobsSynchronization.MobsSynchronization.HasLivingTrackedBoss())
                    return true;

                var level = hero._level;
                if (level?.boss != null && !level.boss.destroyed && level.boss.life > 0)
                    return true;

                return DeadCellsMultiplayerMod.Mobs.MobsSynchronization.MobsSynchronization.IsHostHeroEngagedInCombat(hero);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Host: publish the current hero cell so a joining client has a validated target.</summary>
        private void PublishHostSpawnAnchor()
        {
            var net = _net;
            if (net == null || !net.IsAlive || !net.IsHost)
                return;

            var now = Stopwatch.GetTimestamp();
            if (_nextHostSpawnAnchorTicks != 0 && now < _nextHostSpawnAnchorTicks)
                return;
            _nextHostSpawnAnchorTicks = now + (long)(Stopwatch.Frequency * HostSpawnAnchorIntervalSeconds);

            try
            {
                var hero = me;
                if (hero == null || hero.destroyed || hero.life <= 0)
                    return;

                if (hero._level == null || hero._level.destroyed || hero._level.map == null)
                    return;

                var levelId = hero._level.map.id?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(levelId))
                    return;

                var inCombat = IsHostInCombat(hero);
                net.SendHostSpawnAnchor(hero.cx, hero.cy, levelId, inCombat);
            }
            catch
            {
                // Anchor publishing is best-effort; never let it disturb the hero update.
            }
        }

        private long _nextHostSpawnAnchorTicks;
        private const double HostSpawnAnchorIntervalSeconds = 1.0;
    }
}
