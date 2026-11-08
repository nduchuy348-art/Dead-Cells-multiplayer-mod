using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using HaxeProxy.Runtime;
using ModCore.Utilities;

namespace DeadCellsMultiplayerMod.Ghost;

public static class CombatSyncHelpers
{
    private static readonly ConcurrentDictionary<string, bool> s_attackAnimCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, bool> s_loopAnimCache = new(StringComparer.Ordinal);

    public static bool IsAttackAnim(string anim)
    {
        if (string.IsNullOrWhiteSpace(anim)) return false;

        if (s_attackAnimCache.TryGetValue(anim, out var cached))
            return cached;

        var verdict = IsAttackAnimCore(anim);
        if (s_attackAnimCache.Count >= 512)
            s_attackAnimCache.Clear();
        s_attackAnimCache[anim] = verdict;
        return verdict;
    }

    public static bool ShouldLoopRemoteAnim(string anim)
    {
        if (string.IsNullOrWhiteSpace(anim)) return false;

        if (s_loopAnimCache.TryGetValue(anim, out var cached))
            return cached;

        var verdict = ShouldLoopRemoteAnimCore(anim);
        if (s_loopAnimCache.Count >= 512)
            s_loopAnimCache.Clear();
        s_loopAnimCache[anim] = verdict;
        return verdict;
    }

    public const double DefaultMaxSoundDistanceCases = 26.0;
    public const double VerticalDistanceWeight = 1.35;
    public const double MinimumAudibleThreshold = 0.035;

    public static double? CalculateSpatializedVolume(
        double heroX,
        double heroY,
        double srcX,
        double srcY,
        double? originalVolume,
        double maxDistanceCases = DefaultMaxSoundDistanceCases)
    {
        if (maxDistanceCases <= 0)
            return null;

        double baseVol = originalVolume ?? 1.0;
        if (baseVol <= 0 || double.IsNaN(baseVol) || double.IsInfinity(baseVol))
            return null;

        double dx = heroX - srcX;
        double dy = heroY - srcY;

        // In 2D multi-layer/platformer biomes (such as Promenade or Sewers), sounds on different vertical
        // floors bleed through ground/ceilings and produce map-wide reverberation. Weighting vertical distance
        // prevents cross-floor audio bleed while preserving exact horizontal sound falloff on the same plane.
        double weightedDy = dy * VerticalDistanceWeight;
        double distCases = Math.Sqrt(dx * dx + weightedDy * weightedDy);

        if (double.IsNaN(distCases) || distCases >= maxDistanceCases)
            return null;

        double factor = 1.0 - (distCases / maxDistanceCases);
        double volumeScale = factor * factor;

        double attenuated = baseVol * volumeScale;
        // Suppress very faint residual volume that would otherwise trigger engine-side biome reverb / echo bleed across map
        if (attenuated < MinimumAudibleThreshold)
            return null;

        return attenuated;
    }

    public static bool IsAirborneAnim(string? anim)
    {
        if (string.IsNullOrWhiteSpace(anim)) return false;
        var a = anim.Trim();
        return a.IndexOf("jump", StringComparison.OrdinalIgnoreCase) >= 0 ||
               a.IndexOf("fall", StringComparison.OrdinalIgnoreCase) >= 0 ||
               a.IndexOf("pipe", StringComparison.OrdinalIgnoreCase) >= 0 ||
               a.IndexOf("chute", StringComparison.OrdinalIgnoreCase) >= 0 ||
               a.IndexOf("dive", StringComparison.OrdinalIgnoreCase) >= 0 ||
               a.IndexOf("downAtk", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool IsLandingTransition(string? previousAnim, string? newAnim)
    {
        if (string.IsNullOrWhiteSpace(newAnim)) return false;
        var next = newAnim.Trim();

        if (next.IndexOf("land", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        if (string.IsNullOrWhiteSpace(previousAnim))
        {
            return next.IndexOf("crouch", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        if (IsAirborneAnim(previousAnim))
        {
            if (next.IndexOf("crouch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                next.StartsWith("idle", StringComparison.OrdinalIgnoreCase) ||
                next.StartsWith("run", StringComparison.OrdinalIgnoreCase) ||
                next.StartsWith("walk", StringComparison.OrdinalIgnoreCase) ||
                next.IndexOf("remain", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsHardLanding(string? anim)
    {
        if (string.IsNullOrWhiteSpace(anim)) return false;
        var a = anim.Trim();
        return a.IndexOf("hardLand", StringComparison.OrdinalIgnoreCase) >= 0 ||
               a.IndexOf("heavyLand", StringComparison.OrdinalIgnoreCase) >= 0 ||
               a.IndexOf("dive", StringComparison.OrdinalIgnoreCase) >= 0 ||
               a.IndexOf("downAtk", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool IsJumpTransition(string? previousAnim, string? newAnim)
    {
        if (string.IsNullOrWhiteSpace(newAnim)) return false;
        var next = newAnim.Trim();
        if (IsAirborneAnim(previousAnim)) return false;

        return next.StartsWith("jump", StringComparison.OrdinalIgnoreCase) ||
               next.StartsWith("leap", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRollTransition(string? previousAnim, string? newAnim)
    {
        if (string.IsNullOrWhiteSpace(newAnim)) return false;
        var next = newAnim.Trim();
        if (!string.IsNullOrWhiteSpace(previousAnim))
        {
            var prev = previousAnim.Trim();
            if (prev.IndexOf("roll", StringComparison.OrdinalIgnoreCase) >= 0 ||
                prev.IndexOf("dodge", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
        }

        return next.IndexOf("roll", StringComparison.OrdinalIgnoreCase) >= 0 ||
               next.IndexOf("dodge", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool IsWallTransition(string? previousAnim, string? newAnim)
    {
        if (string.IsNullOrWhiteSpace(newAnim)) return false;
        var next = newAnim.Trim();
        if (!string.IsNullOrWhiteSpace(previousAnim))
        {
            var prev = previousAnim.Trim();
            if (prev.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0 ||
                prev.IndexOf("climb", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
        }

        return next.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0 ||
               next.IndexOf("climb", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool IsDiveTransition(string? previousAnim, string? newAnim)
    {
        if (string.IsNullOrWhiteSpace(newAnim)) return false;
        var next = newAnim.Trim();
        if (!string.IsNullOrWhiteSpace(previousAnim))
        {
            var prev = previousAnim.Trim();
            if (prev.IndexOf("dive", StringComparison.OrdinalIgnoreCase) >= 0 ||
                prev.IndexOf("downAtk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                prev.IndexOf("fastFall", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
        }

        return next.IndexOf("dive", StringComparison.OrdinalIgnoreCase) >= 0 ||
               next.IndexOf("downAtk", StringComparison.OrdinalIgnoreCase) >= 0 ||
               next.IndexOf("fastFall", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static double SmoothStepPosition(
        double current,
        double target,
        double lerpFactor = 0.65,
        double snapThreshold = 120.0,
        double closeThreshold = 0.25)
    {
        if (double.IsNaN(current) || double.IsInfinity(current))
            return target;
        if (double.IsNaN(target) || double.IsInfinity(target))
            return current;

        double delta = target - current;
        double absDelta = Math.Abs(delta);
        if (absDelta < closeThreshold)
            return target;
        if (absDelta > snapThreshold)
            return target;

        double dynamicFactor = lerpFactor;
        if (absDelta > 32.0)
        {
            dynamicFactor = Math.Min(0.90, lerpFactor + (absDelta - 32.0) / 100.0);
        }

        return current + delta * dynamicFactor;
    }

    /// <summary>
    /// Performs cubic Hermite spline interpolation between (p0, v0) and (p1, v1) over normalized time t in [0, 1].
    /// duration is the time interval between p0 and p1 (used to scale velocities).
    /// Applies Fritsch-Carlson monotone tangent clamping to guarantee strictly monotonic interpolation
    /// without overshoot, undershoot, or stationary jitter/floor dipping.
    /// </summary>
    public static double HermiteInterpolate(double p0, double v0, double p1, double v1, double t, double duration = 1.0)
    {
        if (t <= 0.0) return p0;
        if (t >= 1.0) return p1;

        double delta = p1 - p0;
        if (Math.Abs(delta) < 0.0001)
        {
            // Position did not change: strictly clamp to constant to prevent overshoot/jitter
            return p0;
        }

        double safeDuration = duration > 0.0001 ? duration : 1.0;
        double m0 = v0 * safeDuration;
        double m1 = v1 * safeDuration;

        // Fritsch-Carlson monotonicity: clamp tangents so spline never overshoots or oscillates
        if (delta > 0.0)
        {
            if (m0 < 0.0) m0 = 0.0;
            else if (m0 > 3.0 * delta) m0 = 3.0 * delta;

            if (m1 < 0.0) m1 = 0.0;
            else if (m1 > 3.0 * delta) m1 = 3.0 * delta;
        }
        else // delta < 0.0
        {
            if (m0 > 0.0) m0 = 0.0;
            else if (m0 < 3.0 * delta) m0 = 3.0 * delta;

            if (m1 > 0.0) m1 = 0.0;
            else if (m1 < 3.0 * delta) m1 = 3.0 * delta;
        }

        double t2 = t * t;
        double t3 = t2 * t;

        double h00 = 2.0 * t3 - 3.0 * t2 + 1.0;
        double h10 = t3 - 2.0 * t2 + t;
        double h01 = -2.0 * t3 + 3.0 * t2;
        double h11 = t3 - t2;

        return h00 * p0 + h10 * m0 + h01 * p1 + h11 * m1;
    }

    private static bool ShouldLoopRemoteAnimCore(string anim)
    {
        var a = anim.Trim();

        // Never loop attacks or defensive stances; those are driven by weapon replication.
        if (IsAttackAnim(a)) return false;
        if (a.IndexOf("guard", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        if (a.IndexOf("defend", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        if (a.IndexOf("shield", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        if (a.IndexOf("parry", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        if (a.IndexOf("block", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        // Landing animations must play once and stop; looping them keeps the ghost frozen in landing pose.
        if (a.IndexOf("land", StringComparison.OrdinalIgnoreCase) >= 0) return false;

        if (a.StartsWith("idle", StringComparison.OrdinalIgnoreCase)) return true;
        if (a.StartsWith("run", StringComparison.OrdinalIgnoreCase)) return true;
        if (a.StartsWith("walk", StringComparison.OrdinalIgnoreCase)) return true;
        if (a.IndexOf("move", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("jump", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("fall", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("climb", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("ladder", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("crouch", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("volte", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("remain", StringComparison.OrdinalIgnoreCase) >= 0) return true;

        return false;
    }

    private static bool IsAttackAnimCore(string anim)
    {
        var a = anim.Trim();
        if (a.StartsWith("w_", StringComparison.OrdinalIgnoreCase)) return true;
        if (a.StartsWith("attack", StringComparison.OrdinalIgnoreCase)) return true;
        if (a.StartsWith("atk", StringComparison.OrdinalIgnoreCase)) return true;
        if (a.IndexOf("shoot", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("charge", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("hold", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("bow", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("crossbow", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("xbow", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("shield", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("block", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("parry", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("whip", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("punch", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("kick", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("fist", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("slash", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("strike", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("swing", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("smash", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("thrust", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("stab", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("cast", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("throw", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("fire", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("dive", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("hit", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("prep", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("aim", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("windup", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("anticip", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("ready", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("channel", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("flash", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("focus", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (a.IndexOf("warn", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    /// <summary>
    /// Resolves the weapon slash effect (group name and HiLo gradient colors) for remote weapons.
    /// StartSword (the starter sword) in data.cdb configures atkBackStabber animations with fxAtkBackstabberA/B
    /// and dull grey colors (7901874), which lack crescent slash wave trails. This method maps starter sword
    /// strikes to canonical crescent wave VFX (fxSaberA / fxSaberB) with vibrant crescent slash gradient colors
    /// matching other working swords in Dead Cells.
    /// </summary>
    public static (string? resolvedFxId, int? innerColor, int? outerColor) ResolveWeaponSlashFx(
        string? fxId,
        string? animId,
        int? innerColor = null,
        int? outerColor = null)
    {
        string? rawFx = fxId?.Trim();
        string? rawAnim = animId?.Trim();

        const int DefaultSlashInnerColor = 16777215; // 0xFFFFFF (pure white blade core)
        const int DefaultSlashOuterColor = 16758355; // 0xFFA053 (vibrant warm orange crescent trail)

        // 1. Starter Sword (StartSword) strikes 1 & 2 mapping:
        // fxAtkBackstabberA / atkBackStabberA -> fxSaberA (outward crescent slash wave)
        if (string.Equals(rawFx, "fxAtkBackstabberA", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rawAnim, "atkBackStabberA", StringComparison.OrdinalIgnoreCase))
        {
            var resInner = (!innerColor.HasValue || innerColor.Value <= 7901874) ? DefaultSlashInnerColor : innerColor.Value;
            var resOuter = (!outerColor.HasValue || outerColor.Value <= 7901874) ? DefaultSlashOuterColor : outerColor.Value;
            return ("fxSaberA", resInner, resOuter);
        }

        // fxAtkBackstabberB / atkBackStabberB -> fxSaberB (inward return crescent slash wave)
        if (string.Equals(rawFx, "fxAtkBackstabberB", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rawAnim, "atkBackStabberB", StringComparison.OrdinalIgnoreCase))
        {
            var resInner = (!innerColor.HasValue || innerColor.Value <= 7901874) ? DefaultSlashInnerColor : innerColor.Value;
            var resOuter = (!outerColor.HasValue || outerColor.Value <= 7901874) ? DefaultSlashOuterColor : outerColor.Value;
            return ("fxSaberB", resInner, resOuter);
        }

        // 2. If fxId is empty/null, deduce from sword animation:
        if (string.IsNullOrEmpty(rawFx) && !string.IsNullOrEmpty(rawAnim))
        {
            if (string.Equals(rawAnim, "atkSaberA", StringComparison.OrdinalIgnoreCase))
                return ("fxSaberA", innerColor ?? DefaultSlashInnerColor, outerColor ?? DefaultSlashOuterColor);
            if (string.Equals(rawAnim, "atkSaberB", StringComparison.OrdinalIgnoreCase))
                return ("fxSaberB", innerColor ?? DefaultSlashInnerColor, outerColor ?? DefaultSlashOuterColor);
            if (string.Equals(rawAnim, "atkSwordA", StringComparison.OrdinalIgnoreCase))
                return ("fxSwordAtkA", innerColor ?? DefaultSlashInnerColor, outerColor ?? DefaultSlashOuterColor);
            if (string.Equals(rawAnim, "atkSwordB", StringComparison.OrdinalIgnoreCase))
                return ("fxSwordAtkB", innerColor ?? DefaultSlashInnerColor, outerColor ?? DefaultSlashOuterColor);
        }

        // 3. For fxSaberA / fxSaberB with dull/dark colors (e.g. 7901874), upgrade to vibrant trail colors:
        if (string.Equals(rawFx, "fxSaberA", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rawFx, "fxSaberB", StringComparison.OrdinalIgnoreCase))
        {
            var resInner = (!innerColor.HasValue || innerColor.Value <= 7901874) ? DefaultSlashInnerColor : innerColor.Value;
            var resOuter = (!outerColor.HasValue || outerColor.Value <= 7901874) ? DefaultSlashOuterColor : outerColor.Value;
            return (rawFx, resInner, resOuter);
        }

        // 4. Default: pass-through unchanged for all other weapons
        return (rawFx, innerColor, outerColor);
    }

    public enum LocomotionSurface
    {
        Stone,
        Wood,
        Pipe,
        Water
    }

    public enum ClimbingSurface
    {
        Ladder,
        Chain,
        Vine
    }

    public static double CalculateLandingStunDuration(double height, bool isMapTransition = false)
    {
        if (double.IsNaN(height) || double.IsInfinity(height))
            return isMapTransition ? 0.4 : 0.1;

        if (height >= 3.0)
            return 2.0;
        if (height >= 2.0)
            return 1.0;
        if (isMapTransition)
            return 0.4;
        if (height >= 1.0)
            return 0.1;
        return 0.01;
    }

    public static LocomotionSurface ResolveLocomotionSurface(string? biomeId, bool isInWater, bool isWoodTile)
    {
        if (isInWater)
            return LocomotionSurface.Water;

        if (isWoodTile)
            return LocomotionSurface.Wood;

        if (!string.IsNullOrWhiteSpace(biomeId))
        {
            var b = biomeId.Trim();
            if (string.Equals(b, "Cliff", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(b, "Greenhouse", StringComparison.OrdinalIgnoreCase))
            {
                return LocomotionSurface.Wood;
            }

            if (string.Equals(b, "Sewer", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(b, "SewerOld", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(b, "LighthouseBottom", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(b, "LighthouseTop", StringComparison.OrdinalIgnoreCase))
            {
                return LocomotionSurface.Pipe;
            }
        }

        return LocomotionSurface.Stone;
    }

    public static ClimbingSurface ResolveClimbingSurface(string? biomeId, bool isChainSpot = false, bool isVineSpot = false)
    {
        if (isVineSpot)
            return ClimbingSurface.Vine;

        if (isChainSpot)
            return ClimbingSurface.Chain;

        if (!string.IsNullOrWhiteSpace(biomeId))
        {
            var b = biomeId.Trim();
            if (string.Equals(b, "Swamp", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(b, "GardenerStage", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(b, "PurpleGarden", StringComparison.OrdinalIgnoreCase))
            {
                return ClimbingSurface.Vine;
            }

            if (string.Equals(b, "ClockTower", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(b, "Astrolab", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(b, "Distillery", StringComparison.OrdinalIgnoreCase))
            {
                return ClimbingSurface.Chain;
            }
        }

        return ClimbingSurface.Ladder;
    }

    public static bool IsBombOrGrenadeKind(string? kindId)
    {
        if (string.IsNullOrWhiteSpace(kindId))
            return false;

        var k = kindId.Trim();
        return k.IndexOf("bomb", StringComparison.OrdinalIgnoreCase) >= 0 ||
               k.IndexOf("grenade", StringComparison.OrdinalIgnoreCase) >= 0 ||
               k.IndexOf("cluster", StringComparison.OrdinalIgnoreCase) >= 0 ||
               k.IndexOf("explosive", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool IsDeployableSkillKind(string? kindId)
    {
        if (string.IsNullOrWhiteSpace(kindId))
            return false;

        var k = kindId.Trim();
        return k.IndexOf("turret", StringComparison.OrdinalIgnoreCase) >= 0 ||
               k.IndexOf("trap", StringComparison.OrdinalIgnoreCase) >= 0 ||
               k.IndexOf("slicer", StringComparison.OrdinalIgnoreCase) >= 0 ||
               k.IndexOf("sinew", StringComparison.OrdinalIgnoreCase) >= 0 ||
               k.IndexOf("deploy", StringComparison.OrdinalIgnoreCase) >= 0 ||
               k.IndexOf("cleaver", StringComparison.OrdinalIgnoreCase) >= 0 ||
               k.IndexOf("crossbowmatic", StringComparison.OrdinalIgnoreCase) >= 0 ||
               k.IndexOf("heavy_turret", StringComparison.OrdinalIgnoreCase) >= 0 ||
               k.IndexOf("crusher", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static void StepSkillProjectile(
        ref double x,
        ref double y,
        ref double vx,
        ref double vy,
        double gravity)
    {
        x += vx;
        y += vy;
        vy += gravity;
    }

    public static bool IsSolidTileValue(int tileCollisionBits)
    {
        return (tileCollisionBits & 1) != 0 || (tileCollisionBits & 8) != 0;
    }

    public static bool IsSolidTile(IntPtr bytes, int wid, int hei, int cx, int cy)
    {
        if (bytes == IntPtr.Zero || wid <= 0 || hei <= 0)
            return false;

        if (cx < 0 || cx >= wid || cy < 0 || cy >= hei)
            return true;

        int idx = cy * wid + cx;
        int val = Marshal.ReadInt32(bytes, idx << 2);
        return IsSolidTileValue(val);
    }

    public static bool IsSolidTile(dc.level.LevelMap? map, int cx, int cy)
    {
        if (map == null)
            return false;

        try
        {
            var cols = map.collisions;
            if (cols == null || cols.bytes == IntPtr.Zero)
                return false;

            return IsSolidTile(cols.bytes, map.wid, map.hei, cx, cy);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsWoodTileValue(int tileCollisionBits)
    {
        return (tileCollisionBits & 8) != 0;
    }

    public static bool IsWoodTile(IntPtr bytes, int wid, int hei, int cx, int cy)
    {
        if (bytes == IntPtr.Zero || wid <= 0 || hei <= 0)
            return false;

        if (cx < 0 || cx >= wid || cy < 0 || cy >= hei)
            return false;

        int idx = cy * wid + cx;
        int val = Marshal.ReadInt32(bytes, idx << 2);
        return IsWoodTileValue(val);
    }

    public static bool IsWoodTile(dc.level.LevelMap? map, int cx, int cy)
    {
        if (map == null)
            return false;

        try
        {
            var cols = map.collisions;
            if (cols == null || cols.bytes == IntPtr.Zero)
                return false;

            return IsWoodTile(cols.bytes, map.wid, map.hei, cx, cy);
        }
        catch
        {
            return false;
        }
    }

    public static double ClampLandingStunDuration(double duration, double maxStun = 0.35)
    {
        if (double.IsNaN(duration) || duration <= 0.0)
            return 0.0;
        return System.Math.Min(duration, maxStun);
    }

    public static bool TryParseDoorKey(string key, out int cx, out int cy)
    {
        cx = 0;
        cy = 0;
        if (string.IsNullOrWhiteSpace(key))
            return false;

        var sep = key.IndexOf(':');
        if (sep <= 0 || sep >= key.Length - 1)
            return false;

        return int.TryParse(key.AsSpan(0, sep), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out cx) &&
               int.TryParse(key.AsSpan(sep + 1), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out cy);
    }

    public static bool IsDoorProximityMatch(string doorKey1, string doorKey2, int threshold = 6)
    {
        if (string.Equals(doorKey1, doorKey2, StringComparison.Ordinal))
            return true;
        if (!TryParseDoorKey(doorKey1, out var cx1, out var cy1) ||
            !TryParseDoorKey(doorKey2, out var cx2, out var cy2))
            return false;
        var safeThreshold = System.Math.Max(0, threshold);
        return System.Math.Abs(cx1 - cx2) <= safeThreshold && System.Math.Abs(cy1 - cy2) <= safeThreshold;
    }

    public static dc.hxd.res.Sound? TryLoadSound(string soundPath)
    {
        try
        {
            var res = dc.hxd.Res.Class;
            var loadFunc = res?.load;
            if (loadFunc != null)
            {
                var any = loadFunc.Invoke(soundPath.AsHaxeString());
                return any?.toSound();
            }
        }
        catch { }
        return null;
    }

    public static string BuildBossCinePayload(
        string levelId,
        string? genericEventId,
        double? x,
        double? y,
        int? dir,
        double? finalX,
        double? finalY,
        int? finalDir)
    {
        var safeLevelId = string.IsNullOrWhiteSpace(levelId)
            ? string.Empty
            : levelId.Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal)
                .Trim();
        if (string.IsNullOrEmpty(safeLevelId))
            return string.Empty;

        var safeEventId = string.IsNullOrWhiteSpace(genericEventId)
            ? string.Empty
            : genericEventId.Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal)
                .Trim();

        if (!x.HasValue || !y.HasValue)
            return string.IsNullOrEmpty(safeEventId) ? safeLevelId : $"{safeLevelId}|{safeEventId}";

        var resolvedFinalX = finalX ?? x.Value;
        var resolvedFinalY = finalY ?? y.Value;
        var resolvedFinalDir = finalDir ?? dir ?? 0;

        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{safeLevelId}|{safeEventId}|{x.Value}|{y.Value}|{dir ?? 0}|{resolvedFinalX}|{resolvedFinalY}|{resolvedFinalDir}");
    }

    public static bool TryParseBossCinePayload(
        string? payload,
        out string levelId,
        out string? genericEventId,
        out double? snapX,
        out double? snapY,
        out int? snapDir,
        out double? finalX,
        out double? finalY,
        out int? finalDir)
    {
        levelId = string.Empty;
        genericEventId = null;
        snapX = null;
        snapY = null;
        snapDir = null;
        finalX = null;
        finalY = null;
        finalDir = null;

        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var normalized = payload
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Trim();
        if (normalized.Length == 0)
            return false;

        var parts = normalized.Split('|');
        if (parts.Length == 0)
            return false;

        levelId = parts[0].Trim();
        if (string.IsNullOrWhiteSpace(levelId))
            return false;

        if (parts.Length >= 2)
        {
            var eventId = parts[1].Trim();
            if (eventId.Length > 0)
                genericEventId = eventId;
        }

        if (parts.Length >= 4)
        {
            if (double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedX) && double.IsFinite(parsedX))
                snapX = parsedX;
            if (double.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedY) && double.IsFinite(parsedY))
                snapY = parsedY;
        }

        if (parts.Length >= 5 &&
            int.TryParse(parts[4], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedDir))
        {
            snapDir = parsedDir;
        }

        if (parts.Length >= 7)
        {
            if (double.TryParse(parts[5], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedFinalX) && double.IsFinite(parsedFinalX))
                finalX = parsedFinalX;
            if (double.TryParse(parts[6], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedFinalY) && double.IsFinite(parsedFinalY))
                finalY = parsedFinalY;
        }

        if (parts.Length >= 8 &&
            int.TryParse(parts[7], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedFinalDir))
        {
            finalDir = parsedFinalDir;
        }

        return true;
    }

    public static int ComputeExpectedExitPlayerCount(
        bool isHost,
        int localId,
        bool isLocalDowned,
        System.Collections.Generic.IEnumerable<int>? activePeerIds,
        Func<int, bool>? isPeerDowned,
        Func<int, bool>? isPeerActiveAndFresh)
    {
        if (localId <= 0)
            return 1;

        if (isHost)
        {
            var hostExpected = isLocalDowned ? 0 : 1;
            if (activePeerIds != null)
            {
                foreach (var peerId in activePeerIds)
                {
                    if (peerId <= 0 || peerId == localId)
                        continue;
                    if (isPeerDowned != null && isPeerDowned(peerId))
                        continue;
                    if (isPeerActiveAndFresh != null && !isPeerActiveAndFresh(peerId))
                        continue;
                    hostExpected++;
                }
            }
            return Math.Max(1, hostExpected);
        }
        else
        {
            var expected = 0;
            if (activePeerIds != null)
            {
                foreach (var peerId in activePeerIds)
                {
                    if (peerId <= 0)
                        continue;
                    if (peerId == localId)
                    {
                        if (!isLocalDowned)
                            expected++;
                        continue;
                    }
                    if (isPeerDowned != null && isPeerDowned(peerId))
                        continue;
                    expected++;
                }
            }
            return Math.Max(1, expected);
        }
    }
}
