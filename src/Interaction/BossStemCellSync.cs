using System;
using System.Globalization;

namespace DeadCellsMultiplayerMod.Interaction;

/// <summary>
/// Authoritative validation and serialization helpers for Boss Stem Cells (BC) and difficulty level.
/// Enforces valid difficulty bounds [0..5], prevents clients from forcing or desyncing BC values
/// higher than the host's unlocked level, and protects against mid-run state corruption.
/// </summary>
public static class BossStemCellSync
{
    public const int MinBossCells = 0;
    public const int MaxBossCells = 5;

    /// <summary>
    /// Clamps a Boss Stem Cell value within [0..5] and never exceeds the host's unlocked level.
    /// </summary>
    public static int ClampBossCells(int bc, int maxAllowed = MaxBossCells)
    {
        var clampedMax = Math.Clamp(maxAllowed, MinBossCells, MaxBossCells);
        return Math.Clamp(bc, MinBossCells, clampedMax);
    }

    /// <summary>
    /// Checks whether the specified Boss Stem Cell value is within valid bounds [0..5]
    /// and does not exceed the allowed maximum.
    /// </summary>
    public static bool IsValidBossCells(int bc, int maxAllowed = MaxBossCells)
    {
        if (maxAllowed < MinBossCells)
            return false;
        var clampedMax = Math.Clamp(maxAllowed, MinBossCells, MaxBossCells);
        return bc >= MinBossCells && bc <= clampedMax;
    }

    /// <summary>
    /// Formats a BOSSRUNE payload string with authoritative BC and host max unlocked levels.
    /// </summary>
    public static string FormatBossRunePayload(int bossCells, int maxAllowed = MaxBossCells)
    {
        var clampedMax = Math.Clamp(maxAllowed, MinBossCells, MaxBossCells);
        var clampedBc = Math.Clamp(bossCells, MinBossCells, clampedMax);
        return $"{clampedBc.ToString(CultureInfo.InvariantCulture)}|{clampedMax.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Parses a BOSSRUNE wire payload (either "bc" or "bc|hostMaxUnlocked") and bounds checks the result.
    /// Returns true if a valid integer BC was parsed and clamped; false on null/empty/invalid payloads.
    /// </summary>
    public static bool TryParseBossRunePayload(string? payload, out int bossRune, out int maxAllowed)
    {
        return TryParseBossRunePayload(payload, out bossRune, out maxAllowed, out _);
    }

    /// <summary>
    /// Parses a BOSSRUNE wire payload, reporting whether hostMaxUnlocked was explicitly specified in the payload.
    /// </summary>
    public static bool TryParseBossRunePayload(string? payload, out int bossRune, out int maxAllowed, out bool hasExplicitMax)
    {
        bossRune = 0;
        maxAllowed = MaxBossCells;
        hasExplicitMax = false;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var separatorIndex = payload.IndexOf('|');
        if (separatorIndex >= 0)
        {
            var bcPart = payload.Substring(0, separatorIndex);
            var maxPart = payload.Substring(separatorIndex + 1);
            if (!int.TryParse(bcPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedBc))
                return false;
            if (int.TryParse(maxPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hostMax))
            {
                maxAllowed = Math.Clamp(hostMax, MinBossCells, MaxBossCells);
                hasExplicitMax = true;
            }
            bossRune = Math.Clamp(parsedBc, MinBossCells, maxAllowed);
            return true;
        }

        if (!int.TryParse(payload, NumberStyles.Integer, CultureInfo.InvariantCulture, out var singleBc))
            return false;

        bossRune = Math.Clamp(singleBc, MinBossCells, MaxBossCells);
        return true;
    }

    /// <summary>
    /// Validates whether a level ID represents the Prisoners' Quarters starting hub ("PrisonStart").
    /// In Dead Cells, Boss Stem Cell alterations and altar reloads are strictly valid only in the starting hub.
    /// </summary>
    public static bool IsStartingHubLevel(string? levelId)
    {
        if (string.IsNullOrWhiteSpace(levelId))
            return false;

        var clean = levelId.Trim().TrimEnd('/', '\\', '\0').Trim();
        return string.Equals(clean, "PrisonStart", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether Boss Stem Cells can be modified based on multiplayer role and level context.
    /// Only the host may modify difficulty, and only while in the starting hub.
    /// </summary>
    public static bool CanModifyBossCells(bool isHost, string? levelId)
    {
        return isHost && IsStartingHubLevel(levelId);
    }
}
