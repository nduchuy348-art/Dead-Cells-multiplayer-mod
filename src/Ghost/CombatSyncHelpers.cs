using System;
using System.Collections.Concurrent;

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
        double distCases = Math.Sqrt(dx * dx + dy * dy);

        if (double.IsNaN(distCases) || distCases >= maxDistanceCases)
            return null;

        double factor = 1.0 - (distCases / maxDistanceCases);
        double volumeScale = factor * factor;

        double attenuated = baseVol * volumeScale;
        if (attenuated < 0.02)
            return null;

        return attenuated;
    }

    public static bool IsAirborneAnim(string? anim)
    {
        if (string.IsNullOrWhiteSpace(anim)) return false;
        var a = anim.Trim();
        return a.IndexOf("jump", StringComparison.OrdinalIgnoreCase) >= 0 ||
               a.IndexOf("fall", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool IsLandingTransition(string? previousAnim, string? newAnim)
    {
        if (string.IsNullOrWhiteSpace(newAnim)) return false;
        var next = newAnim.Trim();

        if (next.IndexOf("land", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        if (IsAirborneAnim(previousAnim))
        {
            if (next.StartsWith("idle", StringComparison.OrdinalIgnoreCase) ||
                next.StartsWith("run", StringComparison.OrdinalIgnoreCase) ||
                next.StartsWith("walk", StringComparison.OrdinalIgnoreCase) ||
                next.IndexOf("crouch", StringComparison.OrdinalIgnoreCase) >= 0 ||
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
            return ("fxSaberA", DefaultSlashInnerColor, DefaultSlashOuterColor);
        }

        // fxAtkBackstabberB / atkBackStabberB -> fxSaberB (inward return crescent slash wave)
        if (string.Equals(rawFx, "fxAtkBackstabberB", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rawAnim, "atkBackStabberB", StringComparison.OrdinalIgnoreCase))
        {
            return ("fxSaberB", DefaultSlashInnerColor, DefaultSlashOuterColor);
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
}