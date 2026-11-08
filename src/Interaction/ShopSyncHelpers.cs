using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DeadCellsMultiplayerMod.Interaction;

/// <summary>
/// Record representing an active reservation or completed interaction claim
/// on a shop item, pedestal, reroll machine, or ground pickup.
/// </summary>
public readonly record struct ShopClaim(
    int UserId,
    double X,
    double Y,
    long Sequence,
    long TimestampTicks,
    string LevelId,
    long GroupId = 0
);

/// <summary>
/// Thread-safe manager that arbitrates shop items, pedestals, rerolls, and pickups
/// to prevent dual-pickup race conditions.
/// Guarantees: first-arrival wins; loser receives clean rejection with zero gold spent.
/// </summary>
public sealed class ShopReservationManager
{
    public const double DefaultSpatialTolerance = 24.0;
    public const int MaxClaimsPerLevel = 1024;
    private readonly object _sync = new();
    private readonly List<ShopClaim> _claims = new();
    private long _nextGroupId;

    public int ActiveClaimCount
    {
        get
        {
            lock (_sync)
            {
                return _claims.Count;
            }
        }
    }

    /// <summary>
    /// Attempts to claim an interactable at the given world coordinates.
    /// If an existing claim is within tolerance in the same level, returns false (loser rejected).
    /// If unclaimed, registers the claim and returns true (winner claims item).
    /// </summary>
    public bool TryClaim(
        string levelId,
        double x,
        double y,
        int userId,
        long sequence,
        double tolerancePx = DefaultSpatialTolerance)
    {
        var safeLevelId = levelId ?? string.Empty;
        var safeX = double.IsFinite(x) ? x : 0.0;
        var safeY = double.IsFinite(y) ? y : 0.0;
        var safeTolerance = double.IsFinite(tolerancePx) && tolerancePx > 0.0 ? tolerancePx : DefaultSpatialTolerance;
        var tolSq = safeTolerance * safeTolerance;
        var now = Stopwatch.GetTimestamp();

        lock (_sync)
        {
            for (int i = 0; i < _claims.Count; i++)
            {
                var c = _claims[i];
                if (string.Equals(c.LevelId, safeLevelId, StringComparison.Ordinal))
                {
                    double dx = c.X - safeX;
                    double dy = c.Y - safeY;
                    if (dx * dx + dy * dy <= tolSq)
                    {
                        return false;
                    }
                }
            }

            _claims.Add(new ShopClaim(userId, safeX, safeY, sequence, now, safeLevelId));
            PruneIfExceededLocked();
            return true;
        }
    }

    /// <summary>
    /// Attempts to atomically claim a primary altar along with all linked pedestals.
    /// If either the primary or any linked position is already claimed, the entire claim is rejected.
    /// If valid, claims all linked positions atomically so only one choice can ever be taken.
    /// </summary>
    public bool TryClaimLinked(
        string levelId,
        double primaryX,
        double primaryY,
        IReadOnlyList<(double X, double Y)>? linkedPositions,
        int userId,
        long sequence,
        double tolerancePx = DefaultSpatialTolerance)
    {
        var safeLevelId = levelId ?? string.Empty;
        var safeX = double.IsFinite(primaryX) ? primaryX : 0.0;
        var safeY = double.IsFinite(primaryY) ? primaryY : 0.0;
        var safeTolerance = double.IsFinite(tolerancePx) && tolerancePx > 0.0 ? tolerancePx : DefaultSpatialTolerance;
        var tolSq = safeTolerance * safeTolerance;
        var now = Stopwatch.GetTimestamp();

        lock (_sync)
        {
            // Verify neither the primary nor any linked position has already been claimed
            for (int i = 0; i < _claims.Count; i++)
            {
                var c = _claims[i];
                if (!string.Equals(c.LevelId, safeLevelId, StringComparison.Ordinal))
                    continue;

                double dx = c.X - safeX;
                double dy = c.Y - safeY;
                if (dx * dx + dy * dy <= tolSq)
                    return false;

                if (linkedPositions != null)
                {
                    for (int j = 0; j < linkedPositions.Count; j++)
                    {
                        var lp = linkedPositions[j];
                        var lx = double.IsFinite(lp.X) ? lp.X : 0.0;
                        var ly = double.IsFinite(lp.Y) ? lp.Y : 0.0;
                        double ldx = c.X - lx;
                        double ldy = c.Y - ly;
                        if (ldx * ldx + ldy * ldy <= tolSq)
                            return false;
                    }
                }
            }

            // Atomic claim: add primary and all linked positions
            long groupId = System.Threading.Interlocked.Increment(ref _nextGroupId);
            _claims.Add(new ShopClaim(userId, safeX, safeY, sequence, now, safeLevelId, groupId));
            if (linkedPositions != null)
            {
                for (int j = 0; j < linkedPositions.Count; j++)
                {
                    var lp = linkedPositions[j];
                    var lx = double.IsFinite(lp.X) ? lp.X : 0.0;
                    var ly = double.IsFinite(lp.Y) ? lp.Y : 0.0;
                    _claims.Add(new ShopClaim(userId, lx, ly, sequence, now, safeLevelId, groupId));
                }
            }

            PruneIfExceededLocked();
            return true;
        }
    }

    /// <summary>
    /// Attempts to claim a repeatable/debounced interactable such as a shop reroll station.
    /// If an interaction was already claimed within the debounce window, returns false.
    /// Once the debounce window elapses, permits a subsequent activation.
    /// </summary>
    public bool TryClaimDebounced(
        string levelId,
        double x,
        double y,
        int userId,
        long sequence,
        long debounceMs,
        double tolerancePx = DefaultSpatialTolerance)
    {
        var safeLevelId = levelId ?? string.Empty;
        var safeX = double.IsFinite(x) ? x : 0.0;
        var safeY = double.IsFinite(y) ? y : 0.0;
        var safeTolerance = double.IsFinite(tolerancePx) && tolerancePx > 0.0 ? tolerancePx : DefaultSpatialTolerance;
        var tolSq = safeTolerance * safeTolerance;
        var now = Stopwatch.GetTimestamp();
        var safeDebounceMs = Math.Max(0L, debounceMs);
        var debounceTicks = (long)(safeDebounceMs * (Stopwatch.Frequency / 1000.0));

        lock (_sync)
        {
            for (int i = 0; i < _claims.Count; i++)
            {
                var c = _claims[i];
                if (string.Equals(c.LevelId, safeLevelId, StringComparison.Ordinal))
                {
                    double dx = c.X - safeX;
                    double dy = c.Y - safeY;
                    if (dx * dx + dy * dy <= tolSq)
                    {
                        if (now - c.TimestampTicks < debounceTicks)
                            return false; // In debounce window

                        // Debounce expired: update timestamp and claimer
                        _claims[i] = new ShopClaim(userId, safeX, safeY, sequence, now, safeLevelId);
                        return true;
                    }
                }
            }

            _claims.Add(new ShopClaim(userId, safeX, safeY, sequence, now, safeLevelId));
            PruneIfExceededLocked();
            return true;
        }
    }

    /// <summary>
    /// Checks if a position is currently claimed in the given level.
    /// </summary>
    public bool IsClaimed(
        string levelId,
        double x,
        double y,
        double tolerancePx = DefaultSpatialTolerance)
    {
        var safeLevelId = levelId ?? string.Empty;
        var safeX = double.IsFinite(x) ? x : 0.0;
        var safeY = double.IsFinite(y) ? y : 0.0;
        var safeTolerance = double.IsFinite(tolerancePx) && tolerancePx > 0.0 ? tolerancePx : DefaultSpatialTolerance;
        var tolSq = safeTolerance * safeTolerance;

        lock (_sync)
        {
            for (int i = 0; i < _claims.Count; i++)
            {
                var c = _claims[i];
                if (string.Equals(c.LevelId, safeLevelId, StringComparison.Ordinal))
                {
                    double dx = c.X - safeX;
                    double dy = c.Y - safeY;
                    if (dx * dx + dy * dy <= tolSq)
                        return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Releases a claim at the given coordinates, e.g. if validation fails after reservation.
    /// If the claimed interactable belongs to a linked group, all linked pedestals in the group
    /// are atomically released as well.
    /// </summary>
    public bool Release(
        string levelId,
        double x,
        double y,
        double tolerancePx = DefaultSpatialTolerance)
    {
        var safeLevelId = levelId ?? string.Empty;
        var safeX = double.IsFinite(x) ? x : 0.0;
        var safeY = double.IsFinite(y) ? y : 0.0;
        var safeTolerance = double.IsFinite(tolerancePx) && tolerancePx > 0.0 ? tolerancePx : DefaultSpatialTolerance;
        var tolSq = safeTolerance * safeTolerance;

        lock (_sync)
        {
            long targetGroupId = 0;
            bool found = false;

            for (int i = 0; i < _claims.Count; i++)
            {
                var c = _claims[i];
                if (string.Equals(c.LevelId, safeLevelId, StringComparison.Ordinal))
                {
                    double dx = c.X - safeX;
                    double dy = c.Y - safeY;
                    if (dx * dx + dy * dy <= tolSq)
                    {
                        targetGroupId = c.GroupId;
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
                return false;

            if (targetGroupId != 0)
            {
                int removed = _claims.RemoveAll(c =>
                    string.Equals(c.LevelId, safeLevelId, StringComparison.Ordinal) && c.GroupId == targetGroupId);
                return removed > 0;
            }

            for (int i = _claims.Count - 1; i >= 0; i--)
            {
                var c = _claims[i];
                if (string.Equals(c.LevelId, safeLevelId, StringComparison.Ordinal))
                {
                    double dx = c.X - safeX;
                    double dy = c.Y - safeY;
                    if (dx * dx + dy * dy <= tolSq)
                    {
                        _claims.RemoveAt(i);
                        return true;
                    }
                }
            }
            return false;
        }
    }

    private void PruneIfExceededLocked()
    {
        if (_claims.Count <= MaxClaimsPerLevel)
            return;

        int toRemove = _claims.Count - MaxClaimsPerLevel;
        _claims.RemoveRange(0, toRemove);
    }

    /// <summary>
    /// Clears all claims for a given level when transitioning biomes.
    /// </summary>
    public void ClearLevel(string levelId)
    {
        var safeLevelId = levelId ?? string.Empty;
        lock (_sync)
        {
            _claims.RemoveAll(c => string.Equals(c.LevelId, safeLevelId, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Clears all claims globally.
    /// </summary>
    public void ClearAll()
    {
        lock (_sync)
        {
            _claims.Clear();
        }
    }
}

/// <summary>
/// Validation helpers for gold transactions, reroll counters, and non-negative clamping.
/// </summary>
public static class ShopSyncHelpers
{
    public const int MaxReasonableTransaction = 2_000_000_000;

    /// <summary>
    /// Validates and computes a gold transaction strictly ensuring balances remain non-negative.
    /// Rejects negative prices, excessive prices, negative current money, and insufficient funds.
    /// </summary>
    public static bool ValidateGoldTransaction(
        int currentMoney,
        int price,
        out int newMoney,
        out string? rejectionReason)
    {
        if (price < 0)
        {
            newMoney = Math.Max(0, currentMoney);
            rejectionReason = "Price cannot be negative";
            return false;
        }

        if (price > MaxReasonableTransaction)
        {
            newMoney = Math.Max(0, currentMoney);
            rejectionReason = "Price exceeds maximum allowable transaction limit";
            return false;
        }

        if (currentMoney < 0)
        {
            newMoney = 0;
            rejectionReason = "Current money balance is invalid (negative)";
            return false;
        }

        if (currentMoney < price)
        {
            newMoney = currentMoney;
            rejectionReason = "Insufficient funds for purchase";
            return false;
        }

        newMoney = currentMoney - price;
        rejectionReason = null;
        return true;
    }

    /// <summary>
    /// Validates that a reroll station has available uses and strictly enforces non-negative decrement.
    /// </summary>
    public static bool ValidateRerollCounter(
        int currentRemainUses,
        out int newRemainUses,
        out string? rejectionReason)
    {
        if (currentRemainUses <= 0)
        {
            newRemainUses = 0;
            rejectionReason = "No reroll uses remaining";
            return false;
        }

        newRemainUses = Math.Max(0, currentRemainUses - 1);
        rejectionReason = null;
        return true;
    }

    /// <summary>
    /// Clamps an integer value to non-negative bounds [0, int.MaxValue].
    /// </summary>
    public static int ClampNonNegative(int value)
    {
        return value < 0 ? 0 : value;
    }

    /// <summary>
    /// Clamps a 64-bit integer value to non-negative 32-bit bounds [0, int.MaxValue].
    /// </summary>
    public static int ClampNonNegative(long value)
    {
        if (value < 0L) return 0;
        if (value > (long)int.MaxValue) return int.MaxValue;
        return (int)value;
    }
}
