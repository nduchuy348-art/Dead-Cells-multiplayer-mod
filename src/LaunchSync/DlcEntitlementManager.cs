using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace DeadCellsMultiplayerMod;

/// <summary>
/// DLC Entitlement manager and transition reconciler.
/// Tracks local and remote peer DLC ownership (Rise of the Giant, The Bad Seed, Fatal Falls,
/// The Queen and the Sea, Return to Castlevania) to ensure level exit transitions and portal
/// interactions never crash an entitlement-divergent peer into a black screen or native exception.
/// </summary>
public static class DlcEntitlementManager
{
    [Flags]
    public enum DlcMask : int
    {
        None = 0,
        RiseOfTheGiant = 1 << 0,       // 1
        TheBadSeed = 1 << 1,           // 2
        FatalFalls = 1 << 2,           // 4
        TheQueenAndTheSea = 1 << 3,    // 8
        ReturnToCastlevania = 1 << 4,  // 16 (Game internal CDB id: "Purple")
        All = RiseOfTheGiant | TheBadSeed | FatalFalls | TheQueenAndTheSea | ReturnToCastlevania // 31
    }

    public sealed record DlcInfo(
        DlcMask Mask,
        string InternalId,
        string DisplayName,
        uint SteamAppId);

    public static readonly DlcInfo[] AllDlcs = new[]
    {
        new DlcInfo(DlcMask.RiseOfTheGiant, "RiseOfTheGiant", "Rise of the Giant", 1049440),
        new DlcInfo(DlcMask.TheBadSeed, "TheBadSeed", "The Bad Seed", 1204910),
        new DlcInfo(DlcMask.FatalFalls, "FatalFalls", "Fatal Falls", 1451460),
        new DlcInfo(DlcMask.TheQueenAndTheSea, "TheQueenAndTheSea", "The Queen and the Sea", 1580050),
        new DlcInfo(DlcMask.ReturnToCastlevania, "Purple", "Return to Castlevania", 1909750),
    };

    private static readonly Dictionary<string, DlcMask> KnownLevelDlcMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Rise of the Giant (Free DLC, but uses DLC subsystem & separate biomes)
        { "Cavern", DlcMask.RiseOfTheGiant },
        { "Giant", DlcMask.RiseOfTheGiant },
        { "Astrolab", DlcMask.RiseOfTheGiant },
        { "Observatory", DlcMask.RiseOfTheGiant },
        { "T_Cavern", DlcMask.RiseOfTheGiant },
        { "T_Giant", DlcMask.RiseOfTheGiant },
        { "T_Astrolab", DlcMask.RiseOfTheGiant },
        { "T_Observatory", DlcMask.RiseOfTheGiant },
        { "T_CavernAfter", DlcMask.RiseOfTheGiant },
        { "T_GiantAfter", DlcMask.RiseOfTheGiant },

        // The Bad Seed
        { "Greenhouse", DlcMask.TheBadSeed },
        { "Swamp", DlcMask.TheBadSeed },
        { "SwampHeart", DlcMask.TheBadSeed },
        { "T_Greenhouse", DlcMask.TheBadSeed },
        { "T_Swamp", DlcMask.TheBadSeed },
        { "T_SwampAfterPrison", DlcMask.TheBadSeed },
        { "T_SwampHeart", DlcMask.TheBadSeed },
        { "T_SwampAfter", DlcMask.TheBadSeed },
        { "T_AfterSwamp", DlcMask.TheBadSeed },

        // Fatal Falls
        { "Tumulus", DlcMask.FatalFalls },
        { "Cliff", DlcMask.FatalFalls },
        { "GardenerStage", DlcMask.FatalFalls },
        { "T_Tumulus", DlcMask.FatalFalls },
        { "T_AfterTumulus", DlcMask.FatalFalls },
        { "T_Cliff", DlcMask.FatalFalls },
        { "T_GardenerStage", DlcMask.FatalFalls },
        { "T_CliffAfter", DlcMask.FatalFalls },

        // The Queen and the Sea
        { "Shipwreck", DlcMask.TheQueenAndTheSea },
        { "Lighthouse", DlcMask.TheQueenAndTheSea },
        { "QueenArena", DlcMask.TheQueenAndTheSea },
        { "LighthouseTop", DlcMask.TheQueenAndTheSea },
        { "LighthouseBottom", DlcMask.TheQueenAndTheSea },
        { "FisherBarque", DlcMask.TheQueenAndTheSea },
        { "Crown", DlcMask.TheQueenAndTheSea },
        { "T_Shipwreck", DlcMask.TheQueenAndTheSea },
        { "T_Queen", DlcMask.TheQueenAndTheSea },
        { "T_Lighthouse", DlcMask.TheQueenAndTheSea },
        { "T_FisherBarque", DlcMask.TheQueenAndTheSea },

        // Return to Castlevania ("Purple")
        { "PurpleGarden", DlcMask.ReturnToCastlevania },
        { "DookuCastle", DlcMask.ReturnToCastlevania },
        { "DookuCastleHard", DlcMask.ReturnToCastlevania },
        { "DeathArena", DlcMask.ReturnToCastlevania },
        { "DookuArena", DlcMask.ReturnToCastlevania },
        { "RichterCastle", DlcMask.ReturnToCastlevania },
        { "DebugRTC", DlcMask.ReturnToCastlevania },
        { "T_PurpleGarden", DlcMask.ReturnToCastlevania },
        { "T_DookuCastle", DlcMask.ReturnToCastlevania },
        { "T_DookuCastleHard", DlcMask.ReturnToCastlevania },
        { "T_DeathArena", DlcMask.ReturnToCastlevania },
        { "T_AfterDeathArena", DlcMask.ReturnToCastlevania },
        { "T_DookuArena", DlcMask.ReturnToCastlevania },
        { "T_RichterCastle", DlcMask.ReturnToCastlevania },
    };

    private static readonly ConcurrentDictionary<int, int> _peerDlcMasks = new();
    private static int? _localMaskOverride;
    private static int _cachedLocalMask = -1;
    private static long _lastLocalMaskQueryTicks;
    private const double LocalMaskCacheTtlSeconds = 2.0;

    /// <summary>
    /// For unit tests: override the local DLC mask to simulate different DLC ownership configurations.
    /// Pass null to restore real runtime detection.
    /// </summary>
    public static void SetLocalDlcMaskOverride(int? mask)
    {
        _localMaskOverride = mask;
        _cachedLocalMask = -1;
    }

    /// <summary>
    /// Reset all tracked peer DLC entitlements and any local override.
    /// </summary>
    public static void Reset()
    {
        _peerDlcMasks.Clear();
        _localMaskOverride = null;
        _cachedLocalMask = -1;
        _lastLocalMaskQueryTicks = 0;
    }

    /// <summary>
    /// Register or update the DLC bitmask reported by a connected peer.
    /// </summary>
    public static void SetPeerDlcMask(int userId, int mask)
    {
        if (userId <= 0)
            return;
        _peerDlcMasks[userId] = mask;
    }

    /// <summary>
    /// Remove a peer's tracked DLC entitlement when they disconnect.
    /// </summary>
    public static void RemovePeerDlcMask(int userId)
    {
        if (userId <= 0)
            return;
        _peerDlcMasks.TryRemove(userId, out _);
    }

    /// <summary>
    /// Query the tracked DLC bitmask for a peer.
    /// </summary>
    public static bool TryGetPeerDlcMask(int userId, out int mask)
    {
        return _peerDlcMasks.TryGetValue(userId, out mask);
    }

    /// <summary>
    /// Returns all currently registered peer DLC masks as key-value pairs of (userId, mask).
    /// </summary>
    public static IEnumerable<KeyValuePair<int, int>> GetAllPeerDlcMasks()
    {
        return _peerDlcMasks.ToArray();
    }

    /// <summary>
    /// Determines whether the given level ID requires an official Dead Cells DLC pack.
    /// </summary>
    public static bool RequiresDlc(
        string? levelId,
        out DlcMask requiredMask,
        [NotNullWhen(true)] out string? internalId,
        [NotNullWhen(true)] out string? displayName)
    {
        requiredMask = DlcMask.None;
        internalId = null;
        displayName = null;

        if (string.IsNullOrWhiteSpace(levelId))
            return false;

        var clean = levelId.Trim();

        // Exact match in official CDB mapping
        if (KnownLevelDlcMap.TryGetValue(clean, out var mask))
        {
            requiredMask = mask;
            return TryGetDlcInfoByMask(mask, out internalId, out displayName);
        }

        // Direct match against known DLC names / internal IDs (e.g. from portal.destMap.infos.dlc or CDB metadata)
        var parsed = ParseDlcMask(clean);
        if (parsed != DlcMask.None)
        {
            requiredMask = parsed;
            return TryGetDlcInfoByMask(requiredMask, out internalId, out displayName);
        }

        // Heuristics for modded variants, aliases, and transitions
        if (clean.StartsWith("DLCP_", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Dooku", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Richter", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Castlevania", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Dracula", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Alucard", StringComparison.OrdinalIgnoreCase))
        {
            requiredMask = DlcMask.ReturnToCastlevania;
            return TryGetDlcInfoByMask(requiredMask, out internalId, out displayName);
        }

        if (clean.Contains("Lighthouse", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Shipwreck", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("QueenArena", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("FisherBarque", StringComparison.OrdinalIgnoreCase) ||
            clean.StartsWith("T_Queen", StringComparison.OrdinalIgnoreCase) ||
            clean.StartsWith("T_Lighthouse", StringComparison.OrdinalIgnoreCase) ||
            clean.StartsWith("T_Fisher", StringComparison.OrdinalIgnoreCase))
        {
            requiredMask = DlcMask.TheQueenAndTheSea;
            return TryGetDlcInfoByMask(requiredMask, out internalId, out displayName);
        }

        if (clean.Contains("Tumulus", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Cliff", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("GardenerStage", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Mausoleum", StringComparison.OrdinalIgnoreCase))
        {
            requiredMask = DlcMask.FatalFalls;
            return TryGetDlcInfoByMask(requiredMask, out internalId, out displayName);
        }

        if (clean.Contains("Greenhouse", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("SwampHeart", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Swamp", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Arboretum", StringComparison.OrdinalIgnoreCase))
        {
            requiredMask = DlcMask.TheBadSeed;
            return TryGetDlcInfoByMask(requiredMask, out internalId, out displayName);
        }

        if (clean.Contains("Cavern", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Astrolab", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Observatory", StringComparison.OrdinalIgnoreCase))
        {
            requiredMask = DlcMask.RiseOfTheGiant;
            return TryGetDlcInfoByMask(requiredMask, out internalId, out displayName);
        }

        return false;
    }

    /// <summary>
    /// Check whether a specific level is accessible on this local client given installed DLCs.
    /// Base-game levels return true unconditionally.
    /// </summary>
    public static bool IsLevelAccessibleLocally(
        string? levelId,
        out string requiredDlcName,
        out string dlcDisplayName)
    {
        requiredDlcName = string.Empty;
        dlcDisplayName = string.Empty;

        if (!RequiresDlc(levelId, out var requiredMask, out var internalId, out var dispName))
            return true;

        requiredDlcName = internalId;
        dlcDisplayName = dispName;

        var localMask = GetLocalDlcMask();
        return (localMask & (int)requiredMask) == (int)requiredMask;
    }

    /// <summary>
    /// Check whether a specific level is accessible for a known remote peer.
    /// If the peer's entitlement mask is not yet known, returns false when safety requires it.
    /// </summary>
    public static bool IsLevelAccessibleForPeer(
        int userId,
        string? levelId,
        out string requiredDlcName,
        out string dlcDisplayName)
    {
        requiredDlcName = string.Empty;
        dlcDisplayName = string.Empty;

        if (!RequiresDlc(levelId, out var requiredMask, out var internalId, out var dispName))
            return true;

        requiredDlcName = internalId;
        dlcDisplayName = dispName;

        if (_peerDlcMasks.TryGetValue(userId, out var peerMask))
        {
            return (peerMask & (int)requiredMask) == (int)requiredMask;
        }

        // Unknown peer entitlement: unsafe to assume peer has DLC
        return false;
    }

    /// <summary>
    /// Validates that all active connected peers possess the requisite DLC for a target level.
    /// If any peer lacks the DLC, outputs that peer's ID and returns false.
    /// </summary>
    public static bool IsLevelAccessibleForActivePeers(
        IEnumerable<int> activeUserIds,
        int localUserId,
        string? levelId,
        out int missingPeerId)
    {
        missingPeerId = 0;

        if (!RequiresDlc(levelId, out var requiredMask, out _, out _))
            return true;

        foreach (var uid in activeUserIds)
        {
            if (uid <= 0 || uid == localUserId)
                continue;

            if (_peerDlcMasks.TryGetValue(uid, out var peerMask))
            {
                if ((peerMask & (int)requiredMask) != (int)requiredMask)
                {
                    missingPeerId = uid;
                    return false;
                }
            }
            else
            {
                // Peer entitlement unknown; to be safe, treat as missing DLC
                missingPeerId = uid;
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Validates that both the local player and all active connected peers possess the requisite DLC for a target level.
    /// If local player is missing it, outputs localUserId. If a peer is missing it, outputs that peer's ID.
    /// </summary>
    public static bool IsLevelAccessibleForParty(
        IEnumerable<int> activeUserIds,
        int localUserId,
        string? levelId,
        out int missingPeerId)
    {
        missingPeerId = 0;

        if (!RequiresDlc(levelId, out _, out _, out _))
            return true;

        if (!IsLevelAccessibleLocally(levelId, out _, out _))
        {
            missingPeerId = localUserId;
            return false;
        }

        return IsLevelAccessibleForActivePeers(activeUserIds, localUserId, levelId, out missingPeerId);
    }

    /// <summary>
    /// Computes the effective bitmask of DLCs installed on the local system.
    /// Queries Steamworks and Dead Cells native engine runtime if available.
    /// </summary>
    public static int GetLocalDlcMask()
    {
        if (_localMaskOverride.HasValue)
            return _localMaskOverride.Value;

        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_cachedLocalMask >= 0 &&
            _lastLocalMaskQueryTicks > 0 &&
            System.Diagnostics.Stopwatch.GetElapsedTime(_lastLocalMaskQueryTicks).TotalSeconds < LocalMaskCacheTtlSeconds)
        {
            return _cachedLocalMask;
        }

        int mask = 0;

        // 1. Try querying Steamworks if initialized
        mask |= QuerySteamDlcMask();

        // 2. Try querying Dead Cells native DLC subsystem
        mask |= SafeQueryNativeGameDlcMask();

        _cachedLocalMask = mask;
        _lastLocalMaskQueryTicks = now;
        return mask;
    }

    private static int QuerySteamDlcMask()
    {
        int mask = 0;
        try
        {
            if (Steamworks.SteamAPI.IsSteamRunning())
            {
                foreach (var dlc in AllDlcs)
                {
                    if (Steamworks.SteamApps.BIsDlcInstalled(new Steamworks.AppId_t(dlc.SteamAppId)))
                    {
                        mask |= (int)dlc.Mask;
                    }
                }
            }
        }
        catch
        {
            // Steamworks not available, standalone or test environment
        }
        return mask;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int SafeQueryNativeGameDlcMask()
    {
        try
        {
            var dlcClass = dc.tool.DLC.Class;
            if (dlcClass != null)
            {
                var nativeMask = dlcClass.installMask;
                if (nativeMask > 0)
                {
                    return nativeMask & (int)DlcMask.All;
                }
            }
        }
        catch
        {
            // Dead Cells game proxy not present or not running
        }
        return 0;
    }

    private static bool TryGetDlcInfoByMask(
        DlcMask mask,
        [NotNullWhen(true)] out string? internalId,
        [NotNullWhen(true)] out string? displayName)
    {
        foreach (var info in AllDlcs)
        {
            if (info.Mask == mask)
            {
                internalId = info.InternalId;
                displayName = info.DisplayName;
                return true;
            }
        }

        internalId = null;
        displayName = null;
        return false;
    }

    /// <summary>
    /// Translates an internal DLC identifier string (e.g. from CDB "TheBadSeed" or "Purple") to a DlcMask.
    /// </summary>
    public static DlcMask ParseDlcMask(string? internalId)
    {
        if (string.IsNullOrWhiteSpace(internalId))
            return DlcMask.None;

        var clean = internalId.Trim();
        foreach (var info in AllDlcs)
        {
            if (string.Equals(info.InternalId, clean, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(info.DisplayName, clean, StringComparison.OrdinalIgnoreCase))
                return info.Mask;
        }

        // Standard Dead Cells DLC index designations
        if (clean.Equals("DLC4", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("DLC_4", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("DLCP", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("DLC_P", StringComparison.OrdinalIgnoreCase))
            return DlcMask.ReturnToCastlevania;

        if (clean.Equals("DLC3", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("DLC_3", StringComparison.OrdinalIgnoreCase))
            return DlcMask.TheQueenAndTheSea;

        if (clean.Equals("DLC2", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("DLC_2", StringComparison.OrdinalIgnoreCase))
            return DlcMask.FatalFalls;

        if (clean.Equals("DLC1", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("DLC_1", StringComparison.OrdinalIgnoreCase))
            return DlcMask.TheBadSeed;

        if (clean.Equals("DLC0", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("DLC_0", StringComparison.OrdinalIgnoreCase))
            return DlcMask.RiseOfTheGiant;

        if (clean.Contains("Castlevania", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Purple", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Dracula", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Alucard", StringComparison.OrdinalIgnoreCase))
            return DlcMask.ReturnToCastlevania;

        if (clean.Contains("Queen", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Shipwreck", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Lighthouse", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("FisherBarque", StringComparison.OrdinalIgnoreCase))
            return DlcMask.TheQueenAndTheSea;

        if (clean.Contains("Fatal", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Tumulus", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Cliff", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Gardener", StringComparison.OrdinalIgnoreCase))
            return DlcMask.FatalFalls;

        if (clean.Contains("Seed", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Greenhouse", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Swamp", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Arboretum", StringComparison.OrdinalIgnoreCase))
            return DlcMask.TheBadSeed;

        if (clean.Contains("Giant", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Cavern", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Astrolab", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Observatory", StringComparison.OrdinalIgnoreCase))
            return DlcMask.RiseOfTheGiant;

        return DlcMask.None;
    }
}
