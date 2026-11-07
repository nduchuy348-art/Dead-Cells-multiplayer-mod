using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using dc;
using dc.en;
using dc.haxe.ds;
using dc.h3d.mat;
using dc.hl.types;
using dc.hxd;
using dc.libs.heaps.slib;
using dc.pr;
using dc.shader;
using dc.tool;
using dc.tool._AnimationTrack;
using dc.tool._Cooldown;
using dc.tool.mainSkills;
using Hashlink.Virtuals;
using ModCore.Storage;
using ModCore.Utilities;
using HaxeProxy.Runtime;

namespace DeadCellsMultiplayerMod.Ghost.GhostBase
{
    public class GhostKing : KingSkin, IHxbitSerializable<object>
    {
        private const string DefaultBodySkinId = "PrisonerDefault";
        public StringMap? animationTracks;

        public Inventory? inventory;
        public HeroHead? head;
        public string? RemoteSkinId;
        public string? RemoteHeadSkinId;
        public KingWeaponsManager? kingWeaponsManager;
        private DiveAttack? remoteDiveAttack;
        private virtual_cooldown_duration_flags_forbiddenItem_props_requiredItem_skill_? remoteDiveSkillInfos;
        private long _lastRemoteDiveStartTicks;
        private long _lastRemoteDiveLandTicks;
        private long _lastRemoteLandingTicks;
        private long _lastClimbSfxTicks;
        private int _lastFootStepFrame = -1;
        private bool _networkTransitionRetired;

        private const double RemoteDiveReplayMinSeconds = 0.08;
        private const int DiveAttackCooldownKey = 729808896;
        private const int HeroControlLockCooldownKey = 255852544;
        private const int HeroSkillLockCooldownKey = 174063616;
        private const int HeroDiveRuntimeCooldownKey = 719323136;

        ScarfManager? scarf;

        public GhostKing() : base(null, 0, 0)
        {
        }

        public GhostKing(Level lvl, int x, int y) : base(lvl, x, y)
        {
        }
        object IHxbitSerializable<object>.GetData()
        {
            return new();
        }

        void IHxbitSerializable<object>.SetData(object data)
        {
        }

        /// <summary>
        /// Multiplayer GhostKing is a live network avatar only. Persisting it into MSave makes
        /// LoadSave/Continue crash in KingSkin.initGfx (null cd.fastCheck) because Hxbit restore
        /// cannot rebuild our runtime-only fields.
        /// </summary>
        public override bool shouldSave()
        {
            return false;
        }

        public override bool prepareSave()
        {
            return false;
        }

        public override void onReload()
        {
            // Never revive a GhostKing from save data — destroy the husk instead of initGfx.
            try
            {
                if (!destroyed)
                    destroy();
            }
            catch
            {
            }
        }

        public override void init()
        {
            if (IsUnsafePersistedInstance())
            {
                try
                {
                    if (!destroyed)
                        destroy();
                }
                catch
                {
                }
                return;
            }

            EnsureRuntimeDependencies();
            base.init();
            base.initSpeechDeck();
        }

        private bool IsUnsafePersistedInstance()
        {
            try
            {
                // Fresh multiplayer kings always have a cooldown map after Entity construction /
                // EnsureRuntimeDependencies. A Hxbit-restored husk typically has cd == null or a
                // cooldown without fastCheck, which is exactly what crashes KingSkin.initGfx.
                if (cd == null)
                    return true;
                return cd.fastCheck == null;
            }
            catch
            {
                return true;
            }
        }

        private static Hero? ResolveLocalHero()
        {
            var hero = ModEntry.me;
            if (hero != null)
                return hero;

            try
            {
                return ModCore.Modules.Game.Instance?.HeroInstance;
            }
            catch
            {
                return null;
            }
        }

        private static Inventory? CreateDetachedInventory(Inventory? template)
        {
            try
            {
                return new Inventory();
            }
            catch
            {
            }

            if (template == null)
                return null;

            try
            {
                var cloned = template.clone();
                ClearInventoryContents(cloned);
                return cloned;
            }
            catch
            {
                return null;
            }
        }

        private static void ClearInventoryContents(Inventory inventory)
        {
            try
            {
                var items = inventory.items;
                if (items == null || items.array == null)
                    return;

                for (var i = items.length - 1; i >= 0; i--)
                {
                    if ((uint)i >= (uint)items.length)
                        continue;

                    if (items.array[i] is not InventItem item)
                        continue;

                    try { inventory.remove(item); } catch { }
                }
            }
            catch
            {
            }
        }

        private void EnsureRuntimeDependencies()
        {
            if (_networkTransitionRetired || destroyed)
                return;

            var localHero = ResolveLocalHero();
            if (inventory == null)
                inventory = CreateDetachedInventory(localHero?.inventory);

            if (kingWeaponsManager == null && localHero != null && inventory != null)
            {
                try
                {
                    kingWeaponsManager = new KingWeaponsManager(localHero, this);
                    kingWeaponsManager.init();
                }
                catch
                {
                    kingWeaponsManager = null;
                }
            }
        }

        public void TriggerRemoteDiveAttackStart()
        {
            if (destroyed || _level == null)
                return;

            if (IsReplayTooSoon(ref _lastRemoteDiveStartTicks, RemoteDiveReplayMinSeconds))
                return;

            // Keep visual anticipation but avoid mutating local Hero state on remote "start".
            try { spr?._animManager?.play("jumpDown".AsHaxeString(), null, null)?.stopOnLastFrame(Ref<bool>.Null); } catch { }
        }

        public void TriggerRemoteDiveAttackLand(double high)
        {
            if (destroyed || _level == null)
                return;

            if (IsReplayTooSoon(ref _lastRemoteDiveLandTicks, RemoteDiveReplayMinSeconds))
                return;

            DisposeRemoteDiveAttack();
            try
            {
                spr?._animManager?.play("jumpDown".AsHaxeString(), null, null)?.stopOnLastFrame(Ref<bool>.Null);
                var fx = _level?.fx ?? ModEntry.me?._level?.fx;
                var localHero = ResolveLocalHero();
                var isHigh = high >= 1.0;
                var baseScale = isHigh ? 1.0 : 0.7;
                var color = remoteDiveSkillInfos?.props?.color3 ??
                            remoteDiveSkillInfos?.props?.color2 ??
                            remoteDiveSkillInfos?.props?.color ??
                            0xFFFFFF;
                var r = isHigh ? 3 : 2;

                if (fx != null && spr != null)
                {
                    var px = spr.x;
                    var py = spr.y;
                    var cellX = (int)System.Math.Floor(px / 24.0);
                    var cellY = (int)System.Math.Floor(py / 24.0);



                    fx.breakGroundLight(cellX, cellY, 2, 2);
                    fx.groundStones(px, py, null, null);
                    fx.dust(px, py, 16.0, 12, 0);
                    fx.landHeavy(px, py, null, null);

                    for (int ox = -r; ox <= r; ox++)
                    {
                        var waveX = px + ox * 24.0;
                        var powerScale = (System.Math.Abs(ox) == r) ? (baseScale * 0.4) : (baseScale * 0.7);
                        try { fx.diveAttack(waveX, py, color, Ref<double>.From(ref powerScale)); } catch { }
                    }

                    try
                    {
                        FlashLight.Class.create(_level, px, py - 15.0, color, 24.0 * r, 1.5, 1.5, null);
                    }
                    catch { }
                }

                double? spatialVol = 1.0;
                if (localHero != null)
                {
                    var heroX = localHero.cx + localHero.xr;
                    var heroY = localHero.cy + localHero.yr;
                    var myX = cx + xr;
                    var myY = cy + yr;
                    spatialVol = CombatSyncHelpers.CalculateSpatializedVolume(heroX, heroY, myX, myY, 1.0);
                }

                if (spatialVol.HasValue && _level?.viewport != null)
                {
                    _level.viewport.shakeS(0.5, 1.0, 0.3 * spatialVol.Value);
                }

                var lvl = _level ?? localHero?._level;
                if (lvl?.lAudio != null && spatialVol.HasValue)
                {
                    try
                    {
                        var soundPath = isHigh ? "sfx/hero/stomp_char2.wav" : "sfx/hero/stomp_char1.wav";
                        var snd = CombatSyncHelpers.TryLoadSound(soundPath);
                        if (snd != null)
                        {
                            lvl.lAudio.playEvent(snd, spatialVol.Value, null, null);
                        }
                    }
                    catch { }
                }
            }
            catch
            {
            }
        }

        public void TriggerRemoteLanding(double dy, bool hardLand)
        {
            if (destroyed || _level == null)
                return;

            if (IsReplayTooSoon(ref _lastRemoteLandingTicks, 0.15))
                return;

            try
            {
                spr?._animManager?.play((hardLand ? "crouch" : "land").AsHaxeString(), null, null)?.stopOnLastFrame(Ref<bool>.Null);

                var fx = _level?.fx ?? ModEntry.me?._level?.fx;
                var localHero = ResolveLocalHero();

                if (fx != null && spr != null)
                {
                    var px = spr.x;
                    var py = spr.y;
                    var cellX = (int)System.Math.Floor(px / 24.0);
                    var cellY = (int)System.Math.Floor(py / 24.0);

                    fx.breakGroundLight(cellX, cellY, hardLand ? 2 : 1, hardLand ? 2 : 1);
                    fx.dust(px, py, hardLand ? 16.0 : 10.0, hardLand ? 8 : 4, 0);
                    fx.groundStones(px, py, null, null);
                    fx.landSmoke(px, py, hardLand ? 48 : 20);
                    if (hardLand)
                    {
                        fx.landHeavy(px, py, null, null);
                    }
                    if (dy > 2.0)
                    {
                        try
                        {
                            bool top = false;
                            fx.landDirt(px, py, Ref<bool>.From(ref top));
                        }
                        catch { }
                    }
                }

                double? spatialVol = 1.0;
                if (localHero != null)
                {
                    var heroX = localHero.cx + localHero.xr;
                    var heroY = localHero.cy + localHero.yr;
                    var myX = cx + xr;
                    var myY = cy + yr;
                    spatialVol = CombatSyncHelpers.CalculateSpatializedVolume(heroX, heroY, myX, myY, 1.0);
                }

                if (spatialVol.HasValue && _level?.viewport != null)
                {
                    if (dy > 3.0)
                    {
                        _level.viewport.shakeS(0.2, 0.5, 1.3 * spatialVol.Value);
                    }
                    else if (dy > 2.0 || hardLand)
                    {
                        _level.viewport.shakeS(0.2, 0.5, 0.5 * spatialVol.Value);
                    }
                }

                var lvl = _level ?? localHero?._level;
                if (lvl?.lAudio != null && dy >= 0.3 && spatialVol.HasValue)
                {
                    try
                    {
                        var landSound = CombatSyncHelpers.TryLoadSound("sfx/hero/land.wav");
                        if (landSound != null)
                        {
                            lvl.lAudio.playEvent(landSound, 0.25 * spatialVol.Value, null, null);
                        }
                    }
                    catch { }
                }

                PlayLocomotionFootstepSound();
            }
            catch { }
        }

        public void DrawMotionTrail(int? glowColor)
        {
            if (destroyed || spr == null)
                return;

            var color = glowColor ?? 0xFFFFFF;

            var fx = _level?.fx ?? ModEntry.me?._level?.fx;
            if (fx == null)
                return;

            try
            {
                var px = spr.x;
                var py = spr.y - 12.0;
                var dir = spr.scaleX < 0 ? -1 : 1;
                var sparkCount = 4;
                var deflectCount = 2;
                fx.hitLines(px + dir * 18.0, py, dir, color, null, Ref<int>.From(ref sparkCount), Ref<int>.From(ref deflectCount));
            }
            catch { }
        }

        public void PlayLocomotionFootstepSound()
        {
            if (destroyed)
                return;

            var localHero = ResolveLocalHero();
            var lvl = _level ?? localHero?._level;
            if (lvl?.lAudio == null || localHero == null)
                return;

            try
            {
                string? biomeId = null;
                try { biomeId = lvl.map?.id?.ToString(); } catch { }

                bool inWater = false;
                try { inWater = HasAffect(this, 29); } catch { }

                bool isWood = false;
                try
                {
                    var map = lvl.map;
                    if (map != null)
                    {
                        int cX = spr != null && spr.x != 0 ? (int)System.Math.Floor(spr.x / 24.0) : cx;
                        int cY = spr != null && spr.y != 0 ? (int)System.Math.Floor(spr.y / 24.0) : cy;
                        isWood = CombatSyncHelpers.IsWoodTile(map, cX, cY) || CombatSyncHelpers.IsWoodTile(map, cX, cY + 1);
                    }
                }
                catch { }

                var surface = CombatSyncHelpers.ResolveLocomotionSurface(biomeId, inWater, isWood);

                ArrayObj? soundArray = surface switch
                {
                    CombatSyncHelpers.LocomotionSurface.Water => localHero.footStepWaterSounds,
                    CombatSyncHelpers.LocomotionSurface.Pipe => localHero.footStepPipeSounds,
                    CombatSyncHelpers.LocomotionSurface.Wood => localHero.footStepWoodSounds,
                    _ => localHero.footStepStoneSounds
                };

                soundArray ??= localHero.footStepStoneSounds;
                if (soundArray != null && soundArray.length > 0)
                {
                    var idx = Random.Shared.Next(soundArray.length);
                    if ((uint)idx < (uint)soundArray.length && soundArray.array[idx] is dc.hxd.res.Sound sound)
                    {
                        var heroX = localHero.cx + localHero.xr;
                        var heroY = localHero.cy + localHero.yr;
                        var myX = cx + xr;
                        var myY = cy + yr;
                        var spatialVol = CombatSyncHelpers.CalculateSpatializedVolume(heroX, heroY, myX, myY, 0.45);
                        if (spatialVol.HasValue)
                        {
                            lvl.lAudio.playEvent(sound, spatialVol.Value, null, null);
                        }
                    }
                }

                if (spr != null)
                {
                    lvl.fx?.dust(spr.x, spr.y, 6.0, 3, 0);
                }
            }
            catch { }
        }

        public void PlayLocomotionClimbSound()
        {
            if (destroyed)
                return;

            if (IsReplayTooSoon(ref _lastClimbSfxTicks, 0.22))
                return;

            var localHero = ResolveLocalHero();
            var lvl = _level ?? localHero?._level;
            if (lvl?.lAudio == null || localHero == null)
                return;

            try
            {
                string? biomeId = null;
                try { biomeId = lvl.map?.id?.ToString(); } catch { }

                var surface = CombatSyncHelpers.ResolveClimbingSurface(biomeId);
                ArrayObj? soundArray = surface switch
                {
                    CombatSyncHelpers.ClimbingSurface.Vine => localHero.vineClimbSounds,
                    CombatSyncHelpers.ClimbingSurface.Chain => localHero.chainClimbSounds,
                    _ => localHero.chainClimbSounds ?? localHero.vineClimbSounds ?? localHero.footStepWoodSounds
                };

                if (soundArray != null && soundArray.length > 0)
                {
                    var idx = Random.Shared.Next(soundArray.length);
                    if ((uint)idx < (uint)soundArray.length && soundArray.array[idx] is dc.hxd.res.Sound sound)
                    {
                        var heroX = localHero.cx + localHero.xr;
                        var heroY = localHero.cy + localHero.yr;
                        var myX = cx + xr;
                        var myY = cy + yr;
                        var spatialVol = CombatSyncHelpers.CalculateSpatializedVolume(heroX, heroY, myX, myY, 0.4);
                        if (spatialVol.HasValue)
                        {
                            lvl.lAudio.playEvent(sound, spatialVol.Value, null, null);
                        }
                    }
                }
            }
            catch { }
        }

        public void TriggerAttackVocalization(double chance = 0.35)
        {
            if (destroyed)
                return;

            if (Random.Shared.NextDouble() > chance)
                return;

            var localHero = ResolveLocalHero();
            var lvl = _level ?? localHero?._level;
            if (lvl?.lAudio == null || localHero == null)
                return;

            try
            {
                var soundArray = localHero.customAtkSound;
                if (soundArray != null && soundArray.length > 0)
                {
                    var idx = Random.Shared.Next(soundArray.length);
                    if ((uint)idx < (uint)soundArray.length && soundArray.array[idx] is dc.hxd.res.Sound sound)
                    {
                        var heroX = localHero.cx + localHero.xr;
                        var heroY = localHero.cy + localHero.yr;
                        var myX = cx + xr;
                        var myY = cy + yr;
                        var spatialVol = CombatSyncHelpers.CalculateSpatializedVolume(heroX, heroY, myX, myY, 0.5);
                        if (spatialVol.HasValue)
                        {
                            lvl.lAudio.playEvent(sound, spatialVol.Value, null, null);
                        }
                    }
                }
            }
            catch { }
        }

        public void SetRemoteDiveSkillInfos(virtual_cooldown_duration_flags_forbiddenItem_props_requiredItem_skill_? skillInfos)
        {
            remoteDiveSkillInfos = CloneDiveSkillInfos(skillInfos);
            DisposeRemoteDiveAttack();
        }

        private static bool IsReplayTooSoon(ref long lastTicks, double minSeconds)
        {
            var now = Stopwatch.GetTimestamp();
            var minTicks = (long)(Stopwatch.Frequency * minSeconds);
            if (lastTicks != 0 && now - lastTicks < minTicks)
                return true;
            lastTicks = now;
            return false;
        }

        private DiveAttack? EnsureRemoteDiveAttack(Hero localHero, bool forceRecreate)
        {
            if (!IsRemoteDiveReplayContextValid(localHero))
                return null;

            if (forceRecreate)
                DisposeRemoteDiveAttack();

            if (remoteDiveAttack != null)
                return remoteDiveAttack;

            try
            {
                var manager = localHero.mainSkillsManager;
                var localDive = manager?.getMainSkill(DiveAttack.Class) as DiveAttack;
                if (localDive?.skillInfos == null)
                    return null;

                var currentGame = localHero._level?.game ?? dc.pr.Game.Class.ME;
                if (currentGame == null)
                    return null;

                var sourceSkillInfos = remoteDiveSkillInfos ?? localDive.skillInfos;
                var copiedSkillInfos = CloneDiveSkillInfos(sourceSkillInfos) ?? localDive.skillInfos;
                DiveAttack? created = null;
                KingWeaponSupport.WithKingContext(localHero, this, () =>
                {
                    created = new DiveAttack(localHero, currentGame, copiedSkillInfos);
                    created.init();
                });
                remoteDiveAttack = created;
                return created;
            }
            catch
            {
                return null;
            }
        }

        private void ExecuteRemoteDive(Hero localHero, DiveAttack dive, double high, bool startOnly)
        {
            if (!IsRemoteDiveReplayContextValid(localHero))
                return;

            var cooldownSnapshot = CaptureCooldown(localHero.cd, DiveAttackCooldownKey);
            var controlLockSnapshot = CaptureCooldown(localHero.cd, HeroControlLockCooldownKey);
            var skillLockSnapshot = CaptureCooldown(localHero.cd, HeroSkillLockCooldownKey);
            var diveRuntimeSnapshot = CaptureCooldown(localHero.cd, HeroDiveRuntimeCooldownKey);
            var heroSnapshot = CaptureLocalHeroDiveState(localHero);
            try
            {
                KingWeaponSupport.WithLocalHeroDamageAllowed(() =>
                {
                    KingWeaponSupport.WithKingContext(localHero, this, () =>
                    {
                        if (!EnsureDiveActive(localHero, dive))
                            return;

                        try { dive.activeFixedUpdate(); } catch { }

                        if (startOnly)
                            return;

                        if (!ModEntry.TryInvokeSafeDiveAttackOnOwnerLand(dive, high))
                        {
                            try
                            {
                                dive.onOwnerLand(high);
                            }
                            catch
                            {
                                try { dive.end(); } catch { }
                            }
                        }
                    });
                });
            }
            finally
            {
                RestoreCooldown(localHero.cd, HeroDiveRuntimeCooldownKey, diveRuntimeSnapshot);
                RestoreCooldown(localHero.cd, HeroSkillLockCooldownKey, skillLockSnapshot);
                RestoreCooldown(localHero.cd, HeroControlLockCooldownKey, controlLockSnapshot);
                RestoreCooldown(localHero.cd, DiveAttackCooldownKey, cooldownSnapshot);
                RestoreLocalHeroDiveState(localHero, heroSnapshot);
            }
        }

        private bool IsRemoteDiveReplayContextValid(Hero localHero)
        {
            if (localHero == null || destroyed || _level == null)
                return false;

            try
            {
                if (localHero.destroyed)
                    return false;
                if (ModEntry.IsEntityDownedForCombat(localHero))
                    return false;
                if (localHero.life <= 0 || !localHero._targetable)
                    return false;
                if (localHero.cd == null || localHero.cd.fastCheck == null)
                    return false;
            }
            catch
            {
                return false;
            }

            dc.pr.Level? localLevel;
            try
            {
                localLevel = localHero._level;
            }
            catch
            {
                return false;
            }

            if (localLevel == null || !ReferenceEquals(localLevel, _level))
                return false;

            try
            {
                var levelId = localLevel.map?.id?.ToString() ?? string.Empty;
                if (levelId.StartsWith("T_", StringComparison.Ordinal))
                    return false;
            }
            catch
            {
                return false;
            }

            try
            {
                if (localLevel.listCurrentQuadElements == null)
                    return false;
            }
            catch
            {
                return false;
            }

            try
            {
                return spr != null && spr.groupName != null;
            }
            catch
            {
                return false;
            }
        }

        private readonly struct LocalHeroDiveStateSnapshot
        {
            public readonly bool HadDiveAffect;
            public readonly bool HadLandAffect;
            public readonly object? CollisionMode;
            public readonly object? IgnoreGround;
            public readonly bool HasRepelling;
            public readonly bool HasRepellingKnown;
            public readonly double Bdx;
            public readonly bool BdxKnown;
            public readonly double Bdy;
            public readonly bool BdyKnown;

            public LocalHeroDiveStateSnapshot(
                bool hadDiveAffect,
                bool hadLandAffect,
                object? collisionMode,
                object? ignoreGround,
                bool hasRepelling,
                bool hasRepellingKnown,
                double bdx,
                bool bdxKnown,
                double bdy,
                bool bdyKnown)
            {
                HadDiveAffect = hadDiveAffect;
                HadLandAffect = hadLandAffect;
                CollisionMode = collisionMode;
                IgnoreGround = ignoreGround;
                HasRepelling = hasRepelling;
                HasRepellingKnown = hasRepellingKnown;
                Bdx = bdx;
                BdxKnown = bdxKnown;
                Bdy = bdy;
                BdyKnown = bdyKnown;
            }
        }

        private static LocalHeroDiveStateSnapshot CaptureLocalHeroDiveState(Hero hero)
        {
            var hadDiveAffect = HasAffect(hero, 11);
            var hadLandAffect = HasAffect(hero, 63);
            var collisionMode = TryReadMemberValue(hero, "collisionMode");
            var ignoreGround = TryReadMemberValue(hero, "ignoreGround");
            var hasRepellingKnown = TryReadBoolMember(hero, "hasRepelling", out var hasRepelling);
            var bdxKnown = TryReadDoubleMember(hero, "bdx", out var bdx);
            var bdyKnown = TryReadDoubleMember(hero, "bdy", out var bdy);
            return new LocalHeroDiveStateSnapshot(
                hadDiveAffect,
                hadLandAffect,
                collisionMode,
                ignoreGround,
                hasRepelling,
                hasRepellingKnown,
                bdx,
                bdxKnown,
                bdy,
                bdyKnown);
        }

        private static void RestoreLocalHeroDiveState(Hero hero, LocalHeroDiveStateSnapshot snapshot)
        {
            if (!snapshot.HadDiveAffect)
            {
                try { hero.removeAllAffects(11); } catch { }
            }

            if (!snapshot.HadLandAffect)
            {
                try { hero.removeAllAffects(63); } catch { }
            }

            TryWriteMemberValue(hero, "collisionMode", snapshot.CollisionMode);
            TryWriteMemberValue(hero, "ignoreGround", snapshot.IgnoreGround);

            if (snapshot.HasRepellingKnown)
            {
                TryWriteBoolMember(hero, "hasRepelling", snapshot.HasRepelling);
                if (snapshot.HasRepelling)
                {
                    try { hero.enableRepelling(); } catch { }
                }
            }

            if (snapshot.BdxKnown)
                TryWriteDoubleMember(hero, "bdx", snapshot.Bdx);
            if (snapshot.BdyKnown)
                TryWriteDoubleMember(hero, "bdy", snapshot.Bdy);
        }

        private static bool HasAffect(Entity entity, int affectId)
        {
            try
            {
                var affects = entity.affects;
                if (affects == null || affects.array == null)
                    return false;
                if ((uint)affectId >= (uint)affects.length)
                    return false;
                var bucket = affects.array[affectId] as ArrayObj;
                return bucket != null && bucket.length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static object? TryReadMemberValue(object target, string memberName)
        {
            if (target == null || string.IsNullOrWhiteSpace(memberName))
                return null;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                var type = target.GetType();
                var prop = type.GetProperty(memberName, flags);
                if (prop != null)
                    return prop.GetValue(target);
                var field = type.GetField(memberName, flags);
                if (field != null)
                    return field.GetValue(target);
            }
            catch
            {
            }

            return null;
        }

        private static void TryWriteMemberValue(object target, string memberName, object? value)
        {
            if (target == null || string.IsNullOrWhiteSpace(memberName))
                return;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                var type = target.GetType();
                var prop = type.GetProperty(memberName, flags);
                if (prop != null && prop.CanWrite)
                {
                    if (value == null)
                    {
                        if (!prop.PropertyType.IsValueType || Nullable.GetUnderlyingType(prop.PropertyType) != null)
                            prop.SetValue(target, null);
                        return;
                    }

                    if (prop.PropertyType.IsInstanceOfType(value))
                    {
                        prop.SetValue(target, value);
                        return;
                    }
                }

                var field = type.GetField(memberName, flags);
                if (field != null)
                {
                    if (value == null)
                    {
                        if (!field.FieldType.IsValueType || Nullable.GetUnderlyingType(field.FieldType) != null)
                            field.SetValue(target, null);
                        return;
                    }

                    if (field.FieldType.IsInstanceOfType(value))
                        field.SetValue(target, value);
                }
            }
            catch
            {
            }
        }

        private static bool TryReadBoolMember(object target, string memberName, out bool value)
        {
            value = default;
            var raw = TryReadMemberValue(target, memberName);
            if (raw is bool b)
            {
                value = b;
                return true;
            }

            return false;
        }

        private static void TryWriteBoolMember(object target, string memberName, bool value)
        {
            if (target == null || string.IsNullOrWhiteSpace(memberName))
                return;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                var type = target.GetType();
                var prop = type.GetProperty(memberName, flags);
                if (prop != null && prop.CanWrite)
                {
                    if (prop.PropertyType == typeof(bool) || prop.PropertyType == typeof(bool?))
                    {
                        prop.SetValue(target, value);
                        return;
                    }
                }

                var field = type.GetField(memberName, flags);
                if (field != null && (field.FieldType == typeof(bool) || field.FieldType == typeof(bool?)))
                    field.SetValue(target, value);
            }
            catch
            {
            }
        }

        private static bool TryReadDoubleMember(object target, string memberName, out double value)
        {
            value = default;
            var raw = TryReadMemberValue(target, memberName);

            if (raw is double d)
            {
                value = d;
                return true;
            }

            if (raw is float f)
            {
                value = f;
                return true;
            }

            if (raw is int i)
            {
                value = i;
                return true;
            }

            if (raw is long l)
            {
                value = l;
                return true;
            }

            if (raw is decimal m)
            {
                value = (double)m;
                return true;
            }

            if (raw != null)
            {
                try
                {
                    value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                    return true;
                }
                catch
                {
                }
            }

            return false;
        }

        private static void TryWriteDoubleMember(object target, string memberName, double value)
        {
            if (target == null || string.IsNullOrWhiteSpace(memberName))
                return;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                var type = target.GetType();
                var prop = type.GetProperty(memberName, flags);
                if (prop != null && prop.CanWrite)
                {
                    if (prop.PropertyType == typeof(double) || prop.PropertyType == typeof(double?))
                    {
                        prop.SetValue(target, value);
                        return;
                    }

                    if (prop.PropertyType == typeof(float) || prop.PropertyType == typeof(float?))
                    {
                        prop.SetValue(target, (float)value);
                        return;
                    }
                }

                var field = type.GetField(memberName, flags);
                if (field == null)
                    return;

                if (field.FieldType == typeof(double) || field.FieldType == typeof(double?))
                {
                    field.SetValue(target, value);
                    return;
                }

                if (field.FieldType == typeof(float) || field.FieldType == typeof(float?))
                {
                    field.SetValue(target, (float)value);
                }
            }
            catch
            {
            }
        }

        private static bool EnsureDiveActive(Hero localHero, DiveAttack dive)
        {
            try
            {
                if (dive.isActive())
                    return true;
            }
            catch
            {
            }

            ForceDiveActiveWithoutStart(localHero, dive);

            try
            {
                return dive.isActive();
            }
            catch
            {
                return true;
            }
        }

        private static void ForceDiveActiveWithoutStart(Hero localHero, DiveAttack dive)
        {
            // Intentionally disabled. Never manufacture entries in Hero/DiveAttack fastCheck maps.
            // Remote dive replay is visual-only and authoritative damage arrives through MobSync.
        }

        /// <summary>
        /// Returns true with a <see cref="CdInst"/> when the key holds one; if the slot holds any other HL type, removes it so mixed maps cannot corrupt combat/affect code.
        /// </summary>
        private static bool TryGetFastCheckCdInst(Cooldown cooldown, int key, out CdInst? inst)
        {
            inst = null;
            var fastCheck = cooldown.fastCheck;
            if (fastCheck == null || !fastCheck.exists(key))
                return false;

            try
            {
                var raw = fastCheck.get(key);
                if (raw is CdInst good)
                {
                    inst = good;
                    return true;
                }
            }
            catch
            {
            }

            EvictFastCheckKey(cooldown, key);
            return false;
        }

        private static void EvictFastCheckKey(Cooldown cooldown, int key)
        {
            try
            {
                var fastCheck = cooldown.fastCheck;
                if (fastCheck == null || !fastCheck.exists(key))
                    return;

                try
                {
                    var raw = fastCheck.get(key);
                    if (raw is CdInst inst)
                    {
                        try { cooldown.cdList?.remove(inst); } catch { }
                    }
                }
                catch
                {
                }

                try { fastCheck.remove(key); } catch { }
            }
            catch
            {
            }
        }

        private static void EnsureCooldownEntry(Cooldown? cooldown, int key, double frames)
        {
            // Legacy compatibility stub. Writing synthetic CdInst values into fastCheck is unsafe
            // because Dead Cells uses typed values in these maps. Keep this method as a no-op so old
            // call sites cannot corrupt the Hashlink runtime.
        }

        private readonly struct CooldownSnapshot
        {
            public readonly bool HadEntry;
            public readonly CdInst? Entry;
            public readonly double Frames;
            public readonly double Initial;
            public readonly int SubIndexBits;

            public CooldownSnapshot(bool hadEntry, CdInst? entry, double frames, double initial, int subIndexBits)
            {
                HadEntry = hadEntry;
                Entry = entry;
                Frames = frames;
                Initial = initial;
                SubIndexBits = subIndexBits;
            }
        }

        private static CooldownSnapshot CaptureCooldown(Cooldown? cooldown, int key)
        {
            if (cooldown?.fastCheck == null)
                return default;

            try
            {
                if (!TryGetFastCheckCdInst(cooldown, key, out var entry) || entry == null)
                    return default;

                return new CooldownSnapshot(
                    hadEntry: true,
                    entry: entry,
                    frames: entry.frames,
                    initial: entry.initial,
                    subIndexBits: entry.subIndexBits);
            }
            catch
            {
                return default;
            }
        }

        private static void RestoreCooldown(Cooldown? cooldown, int key, CooldownSnapshot snapshot)
        {
            if (cooldown?.fastCheck == null)
                return;

            try
            {
                var fastCheck = cooldown.fastCheck;
                TryGetFastCheckCdInst(cooldown, key, out var current);

                if (!snapshot.HadEntry || snapshot.Entry == null)
                {
                    if (current != null)
                    {
                        try { cooldown.cdList?.remove(current); } catch { }
                        try { fastCheck.remove(key); } catch { }
                    }
                    else if (fastCheck.exists(key))
                    {
                        EvictFastCheckKey(cooldown, key);
                    }
                    return;
                }

                // Never replace/reinsert entries in an engine-owned typed fastCheck map. Only
                // restore values when vanilla still owns the exact same CdInst instance.
                if (current == null || !ReferenceEquals(current, snapshot.Entry))
                    return;

                current.frames = snapshot.Frames;
                current.initial = snapshot.Initial;
                current.subIndexBits = snapshot.SubIndexBits;
            }
            catch
            {
            }
        }

        private void DisposeRemoteDiveAttack()
        {
            if (remoteDiveAttack == null)
                return;

            try { remoteDiveAttack.cancel(); } catch { }
            try { remoteDiveAttack.destroy(); } catch { }
            remoteDiveAttack = null;
        }

        private static virtual_cooldown_duration_flags_forbiddenItem_props_requiredItem_skill_? CloneDiveSkillInfos(
            virtual_cooldown_duration_flags_forbiddenItem_props_requiredItem_skill_? source)
        {
            if (source == null)
                return null;

            var clone = new virtual_cooldown_duration_flags_forbiddenItem_props_requiredItem_skill_();
            SafeAssign(() => clone.cooldown = SafeRead(() => source.cooldown, 0.0));
            SafeAssign(() => clone.duration = SafeRead(() => source.duration, 0.0));
            SafeAssign(() => clone.flags = SafeRead(() => source.flags, default(int?)));
            SafeAssign(() => clone.forbiddenItem = SafeRead(() => source.forbiddenItem, default(dc.String)));
            SafeAssign(() => clone.requiredItem = SafeRead(() => source.requiredItem, default(dc.String)));
            SafeAssign(() => clone.skill = SafeRead(() => source.skill, default(dc.String)));

            var sourceProps = SafeRead(() => source.props, default(virtual_affect_alpha_buff_buff2_color_color2_color3_count_duration2_duration3_pct_pct2_pct3_power_power2_power3_radius_radius2_speed_threshold_));
            SafeAssign(() => clone.props = CloneDiveProps(sourceProps));
            return clone;
        }

        private static virtual_affect_alpha_buff_buff2_color_color2_color3_count_duration2_duration3_pct_pct2_pct3_power_power2_power3_radius_radius2_speed_threshold_? CloneDiveProps(
            virtual_affect_alpha_buff_buff2_color_color2_color3_count_duration2_duration3_pct_pct2_pct3_power_power2_power3_radius_radius2_speed_threshold_? source)
        {
            if (source == null)
                return null;

            var clone = new virtual_affect_alpha_buff_buff2_color_color2_color3_count_duration2_duration3_pct_pct2_pct3_power_power2_power3_radius_radius2_speed_threshold_();
            SafeAssign(() => clone.affect = SafeRead(() => source.affect, default(double?)));
            SafeAssign(() => clone.alpha = SafeRead(() => source.alpha, default(double?)));
            SafeAssign(() => clone.buff = SafeRead(() => source.buff, default(double?)));
            SafeAssign(() => clone.buff2 = SafeRead(() => source.buff2, default(double?)));
            SafeAssign(() => clone.color = SafeRead(() => source.color, default(int?)));
            SafeAssign(() => clone.color2 = SafeRead(() => source.color2, default(int?)));
            SafeAssign(() => clone.color3 = SafeRead(() => source.color3, default(int?)));
            SafeAssign(() => clone.count = SafeRead(() => source.count, default(int?)));
            SafeAssign(() => clone.duration2 = SafeRead(() => source.duration2, default(double?)));
            SafeAssign(() => clone.duration3 = SafeRead(() => source.duration3, default(double?)));
            SafeAssign(() => clone.pct = SafeRead(() => source.pct, default(double?)));
            SafeAssign(() => clone.pct2 = SafeRead(() => source.pct2, default(double?)));
            SafeAssign(() => clone.pct3 = SafeRead(() => source.pct3, default(double?)));
            SafeAssign(() => clone.power = SafeRead(() => source.power, default(double?)));
            SafeAssign(() => clone.power2 = SafeRead(() => source.power2, default(double?)));
            SafeAssign(() => clone.power3 = SafeRead(() => source.power3, default(double?)));
            SafeAssign(() => clone.radius = SafeRead(() => source.radius, default(double?)));
            SafeAssign(() => clone.radius2 = SafeRead(() => source.radius2, default(double?)));
            SafeAssign(() => clone.speed = SafeRead(() => source.speed, default(double?)));
            SafeAssign(() => clone.threshold = SafeRead(() => source.threshold, default(double?)));
            return clone;
        }

        private static T SafeRead<T>(Func<T> getter, T fallback)
        {
            try
            {
                return getter();
            }
            catch
            {
                return fallback;
            }
        }

        private static void SafeAssign(Action setter)
        {
            try
            {
                setter();
            }
            catch
            {
            }
        }

        private static string NormalizeBodySkinId(string? skin)
        {
            return string.IsNullOrWhiteSpace(skin)
                ? DefaultBodySkinId
                : skin.Replace("|", "/").Trim();
        }

        private static virtual_colorMap_consoleCmdId_glowData_group_head_incompatibleHeads_item_model_onlyDefaultHead_scarfBlendMode_scarfs_? ResolveBodySkinInfo(string? skin, out string resolvedSkinId)
        {
            resolvedSkinId = NormalizeBodySkinId(skin);

            virtual_colorMap_consoleCmdId_glowData_group_head_incompatibleHeads_item_model_onlyDefaultHead_scarfBlendMode_scarfs_? info = null;
            try { info = Cdb.Class.getSkinInfo(resolvedSkinId.AsHaxeString()); } catch { }
            if (info != null)
                return info;

            resolvedSkinId = DefaultBodySkinId;
            try { return Cdb.Class.getSkinInfo(DefaultBodySkinId.AsHaxeString()); } catch { return null; }
        }


        public void initScarf()
        {
            var skinInfo = ResolveBodySkinInfo(RemoteSkinId ?? ModEntry.Instance?.remoteSkin, out var resolvedSkinId);
            if (skinInfo == null)
            {
                DisposeScarf();
                return;
            }
            RemoteSkinId = resolvedSkinId;

            if(scarf != null)
                scarf.dispose();

            var item = skinInfo.item;
            if (item == null)
            {
                DisposeScarf();
                return;
            }
            var newScarf = ScarfManager.Class.create(this, item);
            if (newScarf == null)
            {
                DisposeScarf();
                return;
            }
            newScarf.owner = this;
            scarf = newScarf;
        }

        public void DisposeScarf()
        {
            if (scarf == null)
                return;

            try { scarf.dispose(); } catch { }
            scarf = null;
        }

        public override void disposeGfx()
        {
            DisposeScarf();
            base.disposeGfx();
        }

        public override void dispose()
        {
            PrepareForNetworkTransition();
            DisposeScarf();
            base.dispose();
        }

        /// <summary>
        /// Stops remote combat runtimes before a room or level tears down. GhostKing.fixedUpdate can
        /// otherwise recreate its weapon manager while the old process is waiting for native disposal,
        /// leaving a delayed HashLink skill/cooldown callback alive in the outgoing level.
        /// </summary>
        internal void PrepareForNetworkTransition()
        {
            _networkTransitionRetired = true;
            DisposeKingWeaponsManager();
            DisposeRemoteDiveAttack();
            remoteDiveSkillInfos = null;
            _lastRemoteDiveStartTicks = 0;
            _lastRemoteDiveLandTicks = 0;
            _lastRemoteLandingTicks = 0;
            _lastClimbSfxTicks = 0;
            _lastFootStepFrame = -1;
        }

        public void SetPuppetVisible(bool visible)
        {
            try { spr?.set_visible(visible); } catch { }
            try { head?.customHeadSpr?.set_visible(visible); } catch { }
            try { head?.customBackSpr?.set_visible(visible); } catch { }
            try { head?.headNormalSb?.set_visible(visible); } catch { }
            try { head?.headAddSb?.set_visible(visible); } catch { }
            try { head?.eye?.set_visible(visible); } catch { }
        }

        private void DisposeKingWeaponsManager()
        {
            if(kingWeaponsManager == null)
                return;

            try
            {
                kingWeaponsManager.DisposeManagedWeapon();
            }
            catch
            {
            }

            kingWeaponsManager = null;
        }


        public override void initGfx()
        {
            if (IsUnsafePersistedInstance())
                return;

            base.initGfx();
            var skinInfo = ResolveBodySkinInfo(RemoteSkinId ?? ModEntry.Instance?.remoteSkin, out var resolvedSkinId);
            if (skinInfo == null)
                return;

            RemoteSkinId = resolvedSkinId;
            animationTracks = ResolveAnimationTracks(skinInfo);
            dc.String group = "idle".AsHaxeString();
            SpriteLib heroLib = Assets.Class.getHeroLib(skinInfo);
            if (heroLib == null)
                return;

            Texture normalMapFromGroup = heroLib.getNormalMapFromGroup(group);
            if (!TryRetargetCurrentSprite(heroLib, group, normalMapFromGroup))
            {
                int? dp_ROOM_MAIN_HERO = Const.Class.DP_ROOM_MAIN_HERO;
                this.initSprite(heroLib, group, 0.5, 0.5, dp_ROOM_MAIN_HERO, true, null, normalMapFromGroup);
            }
            this.initColorMap(skinInfo);

            ArrayObj glowData = CdbTypeConverter.Class.getGlowData(skinInfo);
            if (glowData != null && glowData.length > 0)
            {
                GlowKey glowKey = (GlowKey)this.spr.getShader(GlowKey.Class);
                if (glowKey == null)
                {
                    glowKey = new GlowKey(null);
                    this.spr.addShader(glowKey);
                }
                glowKey.setGlowDatas(glowData);
                
            }


            // Ambient light
            var General = 1.0;
            var radiusCase = 1.2 * General;
            var Math = dc.Math.Class.random() * 0.20000000000000007;
            General = 0.9 + Math;
            var decayStart = 5.0 * General;
            this.createLight(1161471, radiusCase, decayStart, 0.35);


            // Scarf
            initScarf();

            ModEntry.EnsureGhostKingRenderSafe(this, "GhostKing.initGfx", detachForTransition: false);
        }

        private bool TryRetargetCurrentSprite(SpriteLib heroLib, dc.String group, Texture normalMap)
        {
            var currentSprite = spr;
            if (currentSprite == null)
                return false;

            try
            {
                int startFrame = 0;
                bool stopAllAnims = true;
                currentSprite.set(heroLib, group, Ref<int>.From(ref startFrame), Ref<bool>.From(ref stopAllAnims));
                if (normalMap != null)
                    currentSprite.addOrUpdateNormalMapTexture(normalMap);

                return true;
            }
            catch
            {
                return false;
            }
        }

        public void ApplyRemoteSkin(string? skin)
        {
            var cleaned = NormalizeBodySkinId(skin);
            if (string.Equals(RemoteSkinId, cleaned, StringComparison.Ordinal))
                return;

            RemoteSkinId = cleaned;
            if (this.spr != null)
            {
                try
                {
                    this.disposeGfx();
                    this.initGfx();
                    ModEntry.EnsureGhostKingRenderSafe(this, "GhostKing.ApplyRemoteSkin", detachForTransition: false);
                }
                catch
                {
                    RemoteSkinId = DefaultBodySkinId;
                    try
                    {
                        this.disposeGfx();
                        this.initGfx();
                        ModEntry.EnsureGhostKingRenderSafe(this, "GhostKing.ApplyRemoteSkin.fallback", detachForTransition: false);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static StringMap? ResolveAnimationTracks(
            virtual_colorMap_consoleCmdId_glowData_group_head_incompatibleHeads_item_model_onlyDefaultHead_scarfBlendMode_scarfs_ skinInfo)
        {
            if (skinInfo == null)
            {
                return null;
            }

            dc._String _String = dc.String.Class;
            dc.String path = "atlas/".AsHaxeString();
            path = _String.__add__(_String.__add__(path, skinInfo.model), "_tracks.json".AsHaxeString());
            if (!Res.Class.get_loader().exists(path))
            {
                return null;
            }

            return Assets.Class.getAnimationTracks(Res.Class.load(path));
        }

        public override void onActivate(Hero by, bool longPress)
        {
            base.onActivate(by, longPress);
        }

        public override void fixedUpdate()
        {
            EnsureRuntimeDependencies();
            base.fixedUpdate();
            scarf?.push(0.0, Ref<bool>.Null);
        }

        public override void postUpdate()
        {
            base.postUpdate();
            scarf?.postUpdate();

            if (spr?.groupName != null)
            {
                var grp = spr.groupName.ToString();
                if (grp != null && (grp.StartsWith("run", StringComparison.OrdinalIgnoreCase) || grp.StartsWith("walk", StringComparison.OrdinalIgnoreCase)))
                {
                    var frame = spr.frame;
                    if ((frame == 2 || frame == 6) && _lastFootStepFrame != frame)
                    {
                        _lastFootStepFrame = frame;
                        PlayLocomotionFootstepSound();
                    }
                    else if (frame != 2 && frame != 6)
                    {
                        _lastFootStepFrame = -1;
                    }
                }
                else if (grp != null && (grp.IndexOf("climb", StringComparison.OrdinalIgnoreCase) >= 0 || grp.IndexOf("ladder", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    PlayLocomotionClimbSound();
                }
            }
        }

        public override double get_headX()
        {
            if (life <= 0 || destroyed || spr == null || spr.frameData == null || spr.pivot == null || animationTracks == null)
            {
                return base.get_headX();
            }

            var headBone = ResolveHeadSkeleton();
            if (headBone == null)
            {
                return base.get_headX();
            }

            double baseX = spr.x - spr.frameData.realWid * spr.pivot.centerFactorX * dir;
            double x = baseX + AnimationTrack_Impl_.Class.x(headBone, spr.frame) * dir;
            return x == 0.0 ? base.get_headX() : x;
        }

        public override double get_headY()
        {
            if (life <= 0 || destroyed || spr == null || spr.frameData == null || spr.pivot == null || animationTracks == null)
            {
                return base.get_headY();
            }

            var headBone = ResolveHeadSkeleton();
            if (headBone == null)
            {
                return base.get_headY();
            }

            double baseY = spr.y - spr.frameData.realHei * spr.pivot.centerFactorY;
            double y = baseY + AnimationTrack_Impl_.Class.y(headBone, spr.frame);
            return y == 0.0 ? base.get_headY() : y;
        }

        private ArrayBytes_Int? ResolveHeadSkeleton()
        {
            var tracks = animationTracks;
            var groupName = spr?.groupName;
            if (tracks == null || groupName == null)
            {
                return null;
            }

            var groupTracks = tracks.get(groupName) as StringMap;
            if (groupTracks == null)
            {
                return null;
            }

            return groupTracks.get("headBone".AsHaxeString()) as ArrayBytes_Int;
        }

    }
}
