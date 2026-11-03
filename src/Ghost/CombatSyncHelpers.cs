using System;
using System.Collections.Concurrent;

namespace DeadCellsMultiplayerMod.Ghost;

public static class CombatSyncHelpers
{
    private static readonly ConcurrentDictionary<string, bool> s_attackAnimCache = new(StringComparer.Ordinal);

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