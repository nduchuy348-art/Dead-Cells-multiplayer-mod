using System;
using System.Collections.Generic;
using System.Threading;

namespace DeadCellsMultiplayerMod.Interaction;

/// <summary>
/// Authoritative ledger managing thread-safe cell deposits, blueprint unlocks,
/// Legendary Forge upgrades, and distributed mutual exclusion locks.
/// Guarantees bounded inputs, monotonic progression, and anti-duplicate sequence integrity.
/// </summary>
public sealed class CollectorForgeLedger
{
    public const long DefaultLockTimeoutMs = 30_000;

    private readonly object _sync = new();
    private readonly long _lockTimeoutMs;

    private int _activeCollectorUserId;
    private long _collectorLockTickMs;

    private int _activeForgeUserId;
    private long _forgeLockTickMs;

    private long _nextCollectorLockSeq;
    private long _nextCollectorDepositSeq;
    private long _nextBlueprintUnlockSeq;
    private long _nextForgeUpgradeSeq;

    private readonly Dictionary<string, int> _trackedInvestedCellsByItemId = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unlockedBlueprints = new(StringComparer.Ordinal);
    private readonly int[] _trackedForgeInvested = new int[InterForgeUpgradeEvent.MaxForgeTier + 1];

    private readonly Dictionary<(int UserId, string ItemId), long> _lastCollectorSeqByUserAndItem = new();
    private readonly Dictionary<(int UserId, string ItemId), long> _lastBlueprintSeqByUserAndItem = new();
    private readonly Dictionary<(int UserId, int Tier), long> _lastForgeSeqByUserAndTier = new();
    private readonly Dictionary<(int UserId, string Kind), long> _lastLockSeqByUserAndKind = new();

    public CollectorForgeLedger(long lockTimeoutMs = DefaultLockTimeoutMs)
    {
        _lockTimeoutMs = Math.Max(1000, lockTimeoutMs);
    }

    public static string NormalizeKind(string? kind)
    {
        return string.Equals(kind, "Forge", StringComparison.OrdinalIgnoreCase) ? "Forge" : "Collector";
    }

    public static string SanitizeItemId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var trimmed = raw.Trim()
            .Replace("|", "/", StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);

        return trimmed.Length <= InterCollectorDepositEvent.MaxItemIdLength
            ? trimmed
            : trimmed.Substring(0, InterCollectorDepositEvent.MaxItemIdLength);
    }

    private bool IsLockExpired(long lockTick, long nowTick)
    {
        return lockTick <= 0 || (nowTick - lockTick) > _lockTimeoutMs;
    }

    // -------------------------------------------------------------------------
    // Distributed Mutual Exclusion Locks
    // -------------------------------------------------------------------------

    public bool TryAcquireLock(int userId, string kind, long nowTick, out long sequence)
    {
        sequence = 0;
        if (userId <= 0)
            return false;

        var normKind = NormalizeKind(kind);
        lock (_sync)
        {
            if (string.Equals(normKind, "Forge", StringComparison.Ordinal))
            {
                if (_activeForgeUserId > 0 && _activeForgeUserId != userId && !IsLockExpired(_forgeLockTickMs, nowTick))
                    return false;

                _activeForgeUserId = userId;
                _forgeLockTickMs = nowTick;
                sequence = Interlocked.Increment(ref _nextCollectorLockSeq);
                _lastLockSeqByUserAndKind[(userId, normKind)] = sequence;
                return true;
            }
            else
            {
                if (_activeCollectorUserId > 0 && _activeCollectorUserId != userId && !IsLockExpired(_collectorLockTickMs, nowTick))
                    return false;

                _activeCollectorUserId = userId;
                _collectorLockTickMs = nowTick;
                sequence = Interlocked.Increment(ref _nextCollectorLockSeq);
                _lastLockSeqByUserAndKind[(userId, normKind)] = sequence;
                return true;
            }
        }
    }

    public bool TryReleaseLock(int userId, string kind, out long sequence)
    {
        sequence = 0;
        if (userId <= 0)
            return false;

        var normKind = NormalizeKind(kind);
        lock (_sync)
        {
            if (string.Equals(normKind, "Forge", StringComparison.Ordinal))
            {
                if (_activeForgeUserId == userId)
                {
                    _activeForgeUserId = 0;
                    _forgeLockTickMs = 0;
                    sequence = Interlocked.Increment(ref _nextCollectorLockSeq);
                    _lastLockSeqByUserAndKind[(userId, normKind)] = sequence;
                    return true;
                }
                return false;
            }
            else
            {
                if (_activeCollectorUserId == userId)
                {
                    _activeCollectorUserId = 0;
                    _collectorLockTickMs = 0;
                    sequence = Interlocked.Increment(ref _nextCollectorLockSeq);
                    _lastLockSeqByUserAndKind[(userId, normKind)] = sequence;
                    return true;
                }
                return false;
            }
        }
    }

    public bool IsLockedByOther(int localUserId, string kind, long nowTick, out int occupyingUser)
    {
        occupyingUser = 0;
        var normKind = NormalizeKind(kind);
        lock (_sync)
        {
            if (string.Equals(normKind, "Forge", StringComparison.Ordinal))
            {
                if (_activeForgeUserId > 0 && _activeForgeUserId != localUserId && !IsLockExpired(_forgeLockTickMs, nowTick))
                {
                    occupyingUser = _activeForgeUserId;
                    return true;
                }
                return false;
            }
            else
            {
                if (_activeCollectorUserId > 0 && _activeCollectorUserId != localUserId && !IsLockExpired(_collectorLockTickMs, nowTick))
                {
                    occupyingUser = _activeCollectorUserId;
                    return true;
                }
                return false;
            }
        }
    }

    public void ApplyRemoteLock(int userId, bool inUse, string kind, long sequence, long nowTick)
    {
        if (userId <= 0 || sequence < 0)
            return;

        var normKind = NormalizeKind(kind);
        lock (_sync)
        {
            if (_lastLockSeqByUserAndKind.TryGetValue((userId, normKind), out var lastSeq) && sequence <= lastSeq)
                return;

            _lastLockSeqByUserAndKind[(userId, normKind)] = sequence;

            if (string.Equals(normKind, "Forge", StringComparison.Ordinal))
            {
                if (inUse)
                {
                    _activeForgeUserId = userId;
                    _forgeLockTickMs = nowTick;
                }
                else if (_activeForgeUserId == userId)
                {
                    _activeForgeUserId = 0;
                    _forgeLockTickMs = 0;
                }
            }
            else
            {
                if (inUse)
                {
                    _activeCollectorUserId = userId;
                    _collectorLockTickMs = nowTick;
                }
                else if (_activeCollectorUserId == userId)
                {
                    _activeCollectorUserId = 0;
                    _collectorLockTickMs = 0;
                }
            }
        }
    }

    public void ReleaseLocksForUser(int userId)
    {
        if (userId <= 0)
            return;

        lock (_sync)
        {
            if (_activeCollectorUserId == userId)
            {
                _activeCollectorUserId = 0;
                _collectorLockTickMs = 0;
            }
            if (_activeForgeUserId == userId)
            {
                _activeForgeUserId = 0;
                _forgeLockTickMs = 0;
            }
        }
    }

    public void ReleaseRemoteLocks(int localUserId)
    {
        lock (_sync)
        {
            if (_activeCollectorUserId > 0 && _activeCollectorUserId != localUserId)
            {
                _activeCollectorUserId = 0;
                _collectorLockTickMs = 0;
            }
            if (_activeForgeUserId > 0 && _activeForgeUserId != localUserId)
            {
                _activeForgeUserId = 0;
                _forgeLockTickMs = 0;
            }
        }
    }

    public void ResetLocks()
    {
        lock (_sync)
        {
            _activeCollectorUserId = 0;
            _collectorLockTickMs = 0;
            _activeForgeUserId = 0;
            _forgeLockTickMs = 0;
            _lastLockSeqByUserAndKind.Clear();
        }
    }

    // -------------------------------------------------------------------------
    // Collector Deposits & Blueprint Unlocks
    // -------------------------------------------------------------------------

    public bool IsBlueprintUnlocked(string itemId)
    {
        var cleanId = SanitizeItemId(itemId);
        if (string.IsNullOrWhiteSpace(cleanId))
            return false;

        lock (_sync)
        {
            return _unlockedBlueprints.Contains(cleanId);
        }
    }

    public int GetInvestedCells(string itemId)
    {
        var cleanId = SanitizeItemId(itemId);
        if (string.IsNullOrWhiteSpace(cleanId))
            return 0;

        lock (_sync)
        {
            return _trackedInvestedCellsByItemId.GetValueOrDefault(cleanId, 0);
        }
    }

    public void RecordLocalCollectorDeposit(string itemId, int totalInvested, out long sequence)
    {
        var cleanId = SanitizeItemId(itemId);
        var clampedTotal = Math.Clamp(totalInvested, 0, int.MaxValue);

        lock (_sync)
        {
            var current = _trackedInvestedCellsByItemId.GetValueOrDefault(cleanId, 0);
            _trackedInvestedCellsByItemId[cleanId] = Math.Max(current, clampedTotal);
            sequence = Interlocked.Increment(ref _nextCollectorDepositSeq);
        }
    }

    public void RecordLocalBlueprintUnlock(string itemId, out long sequence)
    {
        var cleanId = SanitizeItemId(itemId);

        lock (_sync)
        {
            _unlockedBlueprints.Add(cleanId);
            sequence = Interlocked.Increment(ref _nextBlueprintUnlockSeq);
        }
    }

    public bool ValidateAndRecordRemoteCollectorDeposit(
        int userId,
        string itemId,
        int cellsDeposited,
        int totalInvested,
        long sequence)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(itemId) || cellsDeposited < 0 || totalInvested < 0 || sequence < 0)
            return false;

        var cleanId = SanitizeItemId(itemId);
        if (string.IsNullOrWhiteSpace(cleanId))
            return false;

        lock (_sync)
        {
            if (_unlockedBlueprints.Contains(cleanId))
                return false;

            if (_lastCollectorSeqByUserAndItem.TryGetValue((userId, cleanId), out var lastSeq) && sequence <= lastSeq)
                return false;

            var current = _trackedInvestedCellsByItemId.GetValueOrDefault(cleanId, 0);
            if (totalInvested < current)
                return false;

            _lastCollectorSeqByUserAndItem[(userId, cleanId)] = sequence;
            _trackedInvestedCellsByItemId[cleanId] = totalInvested;
            return true;
        }
    }

    public bool ValidateAndRecordRemoteBlueprintUnlock(int userId, string itemId, long sequence)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(itemId) || sequence < 0)
            return false;

        var cleanId = SanitizeItemId(itemId);
        if (string.IsNullOrWhiteSpace(cleanId))
            return false;

        lock (_sync)
        {
            if (_lastBlueprintSeqByUserAndItem.TryGetValue((userId, cleanId), out var lastSeq) && sequence <= lastSeq)
                return false;

            _lastBlueprintSeqByUserAndItem[(userId, cleanId)] = sequence;
            _unlockedBlueprints.Add(cleanId);
            return true;
        }
    }

    // -------------------------------------------------------------------------
    // Legendary Forge Upgrades
    // -------------------------------------------------------------------------

    public int GetForgeInvestedCells(int tier)
    {
        if (tier < 0 || tier > InterForgeUpgradeEvent.MaxForgeTier)
            return 0;

        lock (_sync)
        {
            return _trackedForgeInvested[tier];
        }
    }

    public void RecordLocalForgeUpgrade(int tier, int totalInvested, out long sequence)
    {
        var clampedTier = Math.Clamp(tier, 0, InterForgeUpgradeEvent.MaxForgeTier);
        var clampedTotal = Math.Clamp(totalInvested, 0, int.MaxValue);

        lock (_sync)
        {
            var current = _trackedForgeInvested[clampedTier];
            _trackedForgeInvested[clampedTier] = Math.Max(current, clampedTotal);
            sequence = Interlocked.Increment(ref _nextForgeUpgradeSeq);
        }
    }

    public bool ValidateAndRecordRemoteForgeUpgrade(
        int userId,
        int tier,
        int cellsDeposited,
        int totalInvested,
        long sequence)
    {
        if (userId <= 0 || tier < 0 || tier > InterForgeUpgradeEvent.MaxForgeTier || cellsDeposited < 0 || totalInvested < 0 || sequence < 0)
            return false;

        lock (_sync)
        {
            if (_lastForgeSeqByUserAndTier.TryGetValue((userId, tier), out var lastSeq) && sequence <= lastSeq)
                return false;

            var current = _trackedForgeInvested[tier];
            if (totalInvested < current)
                return false;

            _lastForgeSeqByUserAndTier[(userId, tier)] = sequence;
            _trackedForgeInvested[tier] = totalInvested;
            return true;
        }
    }

    // -------------------------------------------------------------------------
    // Full Reset
    // -------------------------------------------------------------------------

    public void ResetAll()
    {
        lock (_sync)
        {
            _activeCollectorUserId = 0;
            _collectorLockTickMs = 0;
            _activeForgeUserId = 0;
            _forgeLockTickMs = 0;

            _trackedInvestedCellsByItemId.Clear();
            _unlockedBlueprints.Clear();
            Array.Clear(_trackedForgeInvested, 0, _trackedForgeInvested.Length);

            _lastCollectorSeqByUserAndItem.Clear();
            _lastBlueprintSeqByUserAndItem.Clear();
            _lastForgeSeqByUserAndTier.Clear();
            _lastLockSeqByUserAndKind.Clear();
        }
    }
}
