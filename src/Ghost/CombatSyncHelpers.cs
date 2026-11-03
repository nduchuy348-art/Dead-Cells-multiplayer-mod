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
}