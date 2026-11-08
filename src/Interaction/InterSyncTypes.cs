namespace DeadCellsMultiplayerMod.Interaction;

public readonly struct InterDoorEvent
{
    public readonly int UserId;
    public readonly double X;
    public readonly double Y;
    public readonly string Action;
    public readonly bool Broken;
    public readonly string LevelId;

    public InterDoorEvent(int userId, double x, double y, string action, bool broken, string levelId = "")
    {
        UserId = System.Math.Max(0, userId);
        X = double.IsFinite(x) ? x : 0.0;
        Y = double.IsFinite(y) ? y : 0.0;
        Action = action ?? string.Empty;
        Broken = broken;
        LevelId = levelId ?? string.Empty;
    }
}

public readonly struct InterElevatorEvent
{
    public readonly int UserId;
    public readonly double X;
    public readonly double Y;
    public readonly long Sequence;
    public readonly string LevelId;

    public InterElevatorEvent(int userId, double x, double y, long sequence, string levelId = "")
    {
        UserId = System.Math.Max(0, userId);
        X = double.IsFinite(x) ? x : 0.0;
        Y = double.IsFinite(y) ? y : 0.0;
        Sequence = System.Math.Max(0L, sequence);
        LevelId = levelId ?? string.Empty;
    }
}

public readonly struct InterElevatorStateEvent
{
    public readonly int UserId;
    public readonly double AnchorX;
    public readonly double AnchorY;
    public readonly long Sequence;
    public readonly double PlatformX;
    public readonly double PlatformY;
    public readonly bool Moving;
    public readonly string LevelId;

    public InterElevatorStateEvent(
        int userId,
        double anchorX,
        double anchorY,
        long sequence,
        double platformX,
        double platformY,
        bool moving,
        string levelId = "")
    {
        UserId = System.Math.Max(0, userId);
        AnchorX = double.IsFinite(anchorX) ? anchorX : 0.0;
        AnchorY = double.IsFinite(anchorY) ? anchorY : 0.0;
        Sequence = System.Math.Max(0L, sequence);
        PlatformX = double.IsFinite(platformX) ? platformX : 0.0;
        PlatformY = double.IsFinite(platformY) ? platformY : 0.0;
        Moving = moving;
        LevelId = levelId ?? string.Empty;
    }
}

public readonly struct InterPressurePlateEvent
{
    public readonly int UserId;
    public readonly double X;
    public readonly double Y;
    public readonly long Sequence;
    public readonly string LevelId;

    public InterPressurePlateEvent(int userId, double x, double y, long sequence, string levelId = "")
    {
        UserId = System.Math.Max(0, userId);
        X = double.IsFinite(x) ? x : 0.0;
        Y = double.IsFinite(y) ? y : 0.0;
        Sequence = System.Math.Max(0L, sequence);
        LevelId = levelId ?? string.Empty;
    }
}

public readonly struct InterTreasureChestEvent
{
    public readonly double X;
    public readonly double Y;
    public readonly string LevelId;

    public InterTreasureChestEvent(double x, double y, string levelId = "")
    {
        X = double.IsFinite(x) ? x : 0.0;
        Y = double.IsFinite(y) ? y : 0.0;
        LevelId = levelId ?? string.Empty;
    }
}

public readonly struct InterVineLadderEvent
{
    public readonly double X;
    public readonly double Y;
    public readonly string LevelId;

    public InterVineLadderEvent(double x, double y, string levelId = "")
    {
        X = double.IsFinite(x) ? x : 0.0;
        Y = double.IsFinite(y) ? y : 0.0;
        LevelId = levelId ?? string.Empty;
    }
}

public readonly struct InterTeleportEvent
{
    public readonly double X;
    public readonly double Y;
    public readonly string LevelId;

    public InterTeleportEvent(double x, double y, string levelId = "")
    {
        X = double.IsFinite(x) ? x : 0.0;
        Y = double.IsFinite(y) ? y : 0.0;
        LevelId = levelId ?? string.Empty;
    }
}

public readonly struct BossHeroTeleportEvent
{
    public readonly int UserId;
    public readonly double X;
    public readonly double Y;
    public readonly int Dir;

    public BossHeroTeleportEvent(int userId, double x, double y, int dir)
    {
        UserId = System.Math.Max(0, userId);
        X = double.IsFinite(x) ? x : 0.0;
        Y = double.IsFinite(y) ? y : 0.0;
        Dir = dir;
    }
}

public readonly struct InterBreakableGroundEvent
{
    public readonly double X;
    public readonly double Y;
    public readonly string LevelId;

    public InterBreakableGroundEvent(double x, double y, string levelId = "")
    {
        X = double.IsFinite(x) ? x : 0.0;
        Y = double.IsFinite(y) ? y : 0.0;
        LevelId = levelId ?? string.Empty;
    }
}

public readonly struct InterBossRuneUpdateCellsEvent
{
    public readonly int UserId;
    public readonly double X;
    public readonly double Y;
    public readonly bool Add;
    public readonly string LevelId;
    public readonly int? TargetBossCells;
    public readonly int? MaxAllowedBossCells;

    public InterBossRuneUpdateCellsEvent(double x, double y, bool add, string levelId = "")
        : this(0, x, y, add, levelId, null, null)
    {
    }

    public InterBossRuneUpdateCellsEvent(int userId, double x, double y, bool add, string levelId = "")
        : this(userId, x, y, add, levelId, null, null)
    {
    }

    public InterBossRuneUpdateCellsEvent(
        int userId,
        double x,
        double y,
        bool add,
        string levelId,
        int? targetBossCells,
        int? maxAllowedBossCells = null)
    {
        UserId = System.Math.Max(0, userId);
        X = double.IsFinite(x) ? x : 0.0;
        Y = double.IsFinite(y) ? y : 0.0;
        Add = add;
        LevelId = levelId ?? string.Empty;

        if (targetBossCells.HasValue)
        {
            var max = maxAllowedBossCells.HasValue
                ? BossStemCellSync.ClampBossCells(maxAllowedBossCells.Value, BossStemCellSync.MaxBossCells)
                : BossStemCellSync.MaxBossCells;
            TargetBossCells = BossStemCellSync.ClampBossCells(targetBossCells.Value, max);
            MaxAllowedBossCells = maxAllowedBossCells.HasValue
                ? BossStemCellSync.ClampBossCells(maxAllowedBossCells.Value, BossStemCellSync.MaxBossCells)
                : null;
        }
        else
        {
            TargetBossCells = null;
            MaxAllowedBossCells = maxAllowedBossCells.HasValue
                ? BossStemCellSync.ClampBossCells(maxAllowedBossCells.Value, BossStemCellSync.MaxBossCells)
                : null;
        }
    }
}

public readonly struct InterPortalEvent
{
    public readonly double X;
    public readonly double Y;
    public readonly string Action;
    public readonly string LevelId;

    public InterPortalEvent(double x, double y, string action, string levelId = "")
    {
        X = double.IsFinite(x) ? x : 0.0;
        Y = double.IsFinite(y) ? y : 0.0;
        Action = SanitizeString(action, 64);
        LevelId = SanitizeString(levelId, 128);
    }

    /// <summary>
    /// Checks whether this portal interaction requires a DLC pack that the local client does not possess.
    /// </summary>
    public bool RequiresMissingLocalDlc(out string missingDlcName)
    {
        missingDlcName = string.Empty;
        if (DlcEntitlementManager.RequiresDlc(LevelId, out _, out var internalId, out var displayName))
        {
            if (!DlcEntitlementManager.IsLevelAccessibleLocally(LevelId, out _, out _))
            {
                missingDlcName = displayName ?? internalId;
                return true;
            }
        }
        return false;
    }

    private static string SanitizeString(string? raw, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        var trimmed = raw.Trim().Replace("|", "/").Replace("\r", string.Empty).Replace("\n", string.Empty);
        return trimmed.Length <= maxLen ? trimmed : trimmed.Substring(0, maxLen);
    }
}

public readonly struct InterEliteShrineEvent
{
    public readonly int UserId;
    public readonly double X;
    public readonly double Y;
    public readonly string LevelId;

    public InterEliteShrineEvent(int userId, double x, double y, string levelId = "")
    {
        UserId = System.Math.Max(0, userId);
        X = double.IsFinite(x) ? x : 0.0;
        Y = double.IsFinite(y) ? y : 0.0;
        LevelId = levelId ?? string.Empty;
    }
}

public readonly struct InterCollectorDepositEvent
{
    public const int MaxItemIdLength = 64;
    public const int MaxDepositCells = 100_000;

    public readonly int UserId;
    public readonly string ItemId;
    public readonly int CellsDeposited;
    public readonly int TotalInvested;
    public readonly long Sequence;
    public readonly string LevelId;

    public InterCollectorDepositEvent(
        int userId,
        string itemId,
        int cellsDeposited,
        int totalInvested,
        long sequence,
        string levelId = "")
    {
        UserId = System.Math.Max(0, userId);
        ItemId = SanitizeString(itemId, MaxItemIdLength);
        CellsDeposited = System.Math.Clamp(cellsDeposited, 0, MaxDepositCells);
        TotalInvested = System.Math.Clamp(totalInvested, 0, int.MaxValue);
        Sequence = System.Math.Max(0L, sequence);
        LevelId = SanitizeString(levelId, 128);
    }

    private static string SanitizeString(string? raw, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        var trimmed = raw.Trim().Replace("|", "/").Replace("\r", string.Empty).Replace("\n", string.Empty);
        return trimmed.Length <= maxLen ? trimmed : trimmed.Substring(0, maxLen);
    }
}

public readonly struct InterBlueprintUnlockEvent
{
    public const int MaxItemIdLength = 64;

    public readonly int UserId;
    public readonly string ItemId;
    public readonly long Sequence;
    public readonly string LevelId;

    public InterBlueprintUnlockEvent(
        int userId,
        string itemId,
        long sequence,
        string levelId = "")
    {
        UserId = System.Math.Max(0, userId);
        ItemId = SanitizeString(itemId, MaxItemIdLength);
        Sequence = System.Math.Max(0L, sequence);
        LevelId = SanitizeString(levelId, 128);
    }

    private static string SanitizeString(string? raw, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        var trimmed = raw.Trim().Replace("|", "/").Replace("\r", string.Empty).Replace("\n", string.Empty);
        return trimmed.Length <= maxLen ? trimmed : trimmed.Substring(0, maxLen);
    }
}

public readonly struct InterForgeUpgradeEvent
{
    public const int MaxDepositCells = 100_000;
    public const int MaxForgeTier = 2; // Dead Cells has 3 tiers: +, ++, S (indices 0, 1, 2)

    public readonly int UserId;
    public readonly int UpgradeLevel;
    public readonly int CellsDeposited;
    public readonly int TotalInvested;
    public readonly long Sequence;
    public readonly string LevelId;

    public InterForgeUpgradeEvent(
        int userId,
        int upgradeLevel,
        int cellsDeposited,
        int totalInvested,
        long sequence,
        string levelId = "")
    {
        UserId = System.Math.Max(0, userId);
        UpgradeLevel = System.Math.Clamp(upgradeLevel, 0, MaxForgeTier);
        CellsDeposited = System.Math.Clamp(cellsDeposited, 0, MaxDepositCells);
        TotalInvested = System.Math.Clamp(totalInvested, 0, int.MaxValue);
        Sequence = System.Math.Max(0L, sequence);
        LevelId = SanitizeString(levelId, 128);
    }

    private static string SanitizeString(string? raw, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        var trimmed = raw.Trim().Replace("|", "/").Replace("\r", string.Empty).Replace("\n", string.Empty);
        return trimmed.Length <= maxLen ? trimmed : trimmed.Substring(0, maxLen);
    }
}

public readonly struct InterCollectorLockEvent
{
    public readonly int UserId;
    public readonly bool InUse;
    public readonly string Kind;
    public readonly long Sequence;
    public readonly string LevelId;

    public InterCollectorLockEvent(
        int userId,
        bool inUse,
        string kind,
        long sequence,
        string levelId = "")
    {
        UserId = System.Math.Max(0, userId);
        InUse = inUse;
        Kind = string.Equals(kind, "Forge", System.StringComparison.OrdinalIgnoreCase) ? "Forge" : "Collector";
        Sequence = System.Math.Max(0L, sequence);
        LevelId = SanitizeString(levelId, 128);
    }

    private static string SanitizeString(string? raw, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        var trimmed = raw.Trim().Replace("|", "/").Replace("\r", string.Empty).Replace("\n", string.Empty);
        return trimmed.Length <= maxLen ? trimmed : trimmed.Substring(0, maxLen);
    }
}

public readonly struct InterShopPurchaseEvent
{
    public const int MaxPrice = 2_000_000_000;

    public readonly int UserId;
    public readonly double X;
    public readonly double Y;
    public readonly int Price;
    public readonly long Sequence;
    public readonly bool IsMimic;
    public readonly string LevelId;

    public InterShopPurchaseEvent(
        int userId,
        double x,
        double y,
        int price,
        long sequence,
        bool isMimic = false,
        string levelId = "")
    {
        UserId = System.Math.Max(0, userId);
        X = double.IsFinite(x) ? x : 0.0;
        Y = double.IsFinite(y) ? y : 0.0;
        Price = System.Math.Clamp(price, 0, MaxPrice);
        Sequence = System.Math.Max(0L, sequence);
        IsMimic = isMimic;
        LevelId = SanitizeString(levelId, 128);
    }

    private static string SanitizeString(string? raw, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        var trimmed = raw.Trim().Replace("|", "/").Replace("\r", string.Empty).Replace("\n", string.Empty);
        return trimmed.Length <= maxLen ? trimmed : trimmed.Substring(0, maxLen);
    }
}

public readonly struct InterShopRerollEvent
{
    public const int MaxCost = 2_000_000_000;
    public const int MaxRemainUses = 10_000;

    public readonly int UserId;
    public readonly double X;
    public readonly double Y;
    public readonly int Cost;
    public readonly int RemainUses;
    public readonly long Sequence;
    public readonly string LevelId;

    public InterShopRerollEvent(
        int userId,
        double x,
        double y,
        int cost,
        int remainUses,
        long sequence,
        string levelId = "")
    {
        UserId = System.Math.Max(0, userId);
        X = double.IsFinite(x) ? x : 0.0;
        Y = double.IsFinite(y) ? y : 0.0;
        Cost = System.Math.Clamp(cost, 0, MaxCost);
        RemainUses = System.Math.Clamp(remainUses, 0, MaxRemainUses);
        Sequence = System.Math.Max(0L, sequence);
        LevelId = SanitizeString(levelId, 128);
    }

    private static string SanitizeString(string? raw, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        var trimmed = raw.Trim().Replace("|", "/").Replace("\r", string.Empty).Replace("\n", string.Empty);
        return trimmed.Length <= maxLen ? trimmed : trimmed.Substring(0, maxLen);
    }
}

public readonly struct InterPedestalPickupEvent
{
    public readonly int UserId;
    public readonly double X;
    public readonly double Y;
    public readonly long Sequence;
    public readonly string LevelId;

    public InterPedestalPickupEvent(
        int userId,
        double x,
        double y,
        long sequence,
        string levelId = "")
    {
        UserId = System.Math.Max(0, userId);
        X = double.IsFinite(x) ? x : 0.0;
        Y = double.IsFinite(y) ? y : 0.0;
        Sequence = System.Math.Max(0L, sequence);
        LevelId = SanitizeString(levelId, 128);
    }

    private static string SanitizeString(string? raw, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        var trimmed = raw.Trim().Replace("|", "/").Replace("\r", string.Empty).Replace("\n", string.Empty);
        return trimmed.Length <= maxLen ? trimmed : trimmed.Substring(0, maxLen);
    }
}

public readonly struct InterShopRejectEvent
{
    public readonly int UserId;
    public readonly double X;
    public readonly double Y;
    public readonly long Sequence;
    public readonly string LevelId;

    public InterShopRejectEvent(
        int userId,
        double x,
        double y,
        long sequence,
        string levelId = "")
    {
        UserId = System.Math.Max(0, userId);
        X = double.IsFinite(x) ? x : 0.0;
        Y = double.IsFinite(y) ? y : 0.0;
        Sequence = System.Math.Max(0L, sequence);
        LevelId = SanitizeString(levelId, 128);
    }

    private static string SanitizeString(string? raw, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        var trimmed = raw.Trim().Replace("|", "/").Replace("\r", string.Empty).Replace("\n", string.Empty);
        return trimmed.Length <= maxLen ? trimmed : trimmed.Substring(0, maxLen);
    }
}

public readonly struct InterPedestalRejectEvent
{
    public readonly int UserId;
    public readonly double X;
    public readonly double Y;
    public readonly long Sequence;
    public readonly string LevelId;

    public InterPedestalRejectEvent(
        int userId,
        double x,
        double y,
        long sequence,
        string levelId = "")
    {
        UserId = System.Math.Max(0, userId);
        X = double.IsFinite(x) ? x : 0.0;
        Y = double.IsFinite(y) ? y : 0.0;
        Sequence = System.Math.Max(0L, sequence);
        LevelId = SanitizeString(levelId, 128);
    }

    private static string SanitizeString(string? raw, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        var trimmed = raw.Trim().Replace("|", "/").Replace("\r", string.Empty).Replace("\n", string.Empty);
        return trimmed.Length <= maxLen ? trimmed : trimmed.Substring(0, maxLen);
    }
}
