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
    public readonly double X;
    public readonly double Y;
    public readonly bool Add;
    public readonly string LevelId;

    public InterBossRuneUpdateCellsEvent(double x, double y, bool add, string levelId = "")
    {
        X = double.IsFinite(x) ? x : 0.0;
        Y = double.IsFinite(y) ? y : 0.0;
        Add = add;
        LevelId = levelId ?? string.Empty;
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
        Action = action ?? string.Empty;
        LevelId = levelId ?? string.Empty;
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
