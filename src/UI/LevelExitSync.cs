using System.Diagnostics;
using System.Globalization;
using dc;
using dc.en;
using dc.en.inter;
using dc.en.inter.door;
using dc.h2d;
using dc.pr;
using dc.ui;
using DeadCellsMultiplayerMod.Interface.ModuleInitializing;
using DeadCellsMultiplayerMod.MultiplayerModUI.lifeUI;
using DeadCellsMultiplayerMod;
using HaxeProxy.Runtime;
using ModCore.Events;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Utilities;
using Serilog;

namespace DeadCellsMultiplayerMod.MultiplayerModUI.LevelExit;

public class LevelExitSync :
    IEventReceiver,
    IOnAdvancedModuleInitializing,
    IOnHeroUpdate
{
    private sealed class DoorVisual
    {
        public Entity? Door;
        public string DoorKey = string.Empty;
        public Graphics? Circle;
        public dc.ui.Text? Counter;
        public bool? LastActive;
        public int LastTextWidth = -1;
        public int LastReadyCount = -1;
        public int LastExpectedCount = -1;
    }

    private sealed class PlayerExitState
    {
        public int UserId;
        public string DoorKey = string.Empty;
        public int DoorCx;
        public int DoorCy;
        public bool Pressed;
        public bool InsideCircle;
        public bool IsOutOfGame;
        public bool IsOnScreen;
        public long LastTick;
        /// <summary>Level this readiness was reported for; empty means unknown/legacy.</summary>
        public string LevelId = string.Empty;
    }

    private const double ExitCircleRadiusPx = 84.0;
    private const double ExitCounterYOffsetPx = 100.0;
    private const double ExitStateResendSeconds = 0.20;
    private const double CounterScale = 1.10;
    private const int CounterColor = 0xFFFFFF;
    private const int MarkerColor = 0x68AD3D;
    private const int CircleColor = 0x59D5FF;
    private const double CircleAlphaIdle = 0.10;
    private const double CircleAlphaActive = 0.22;
    private const int PointerFxSuppressionKey = 188743680;

    private readonly ILogger _log;

    private readonly Dictionary<string, DoorVisual> _doorVisuals = new(StringComparer.Ordinal);
    private readonly Dictionary<int, PlayerExitState> _playerStates = new();
    private readonly HashSet<int> _activePlayerIds = new();
    private readonly HashSet<int> _activePlayerScratchIds = new();
    private readonly Dictionary<string, int> _readyPlayerCounts = new(StringComparer.Ordinal);
    private readonly List<int> _stalePlayerIds = new();
    private readonly List<string> _staleDoorVisualKeys = new();
    private readonly Dictionary<string, Entity> _exitTargetsByDoorKey = new(StringComparer.Ordinal);
    private Pointer? _exitPointer;
    private string _exitPointerDoorKey = string.Empty;

    private Level? _lastLevel;
    private string _localDoorKey = string.Empty;
    private int _localDoorCx;
    private int _localDoorCy;
    private bool _localPressed;
    private bool _localInsideCircle;
    private bool _localDoorOutOfGame;
    private bool _localDoorOnScreen;
    private bool _hasLastSentState;
    private int _lastSentDoorCx;
    private int _lastSentDoorCy;
    private byte _lastSentStateFlags;
    private long _lastLocalStateSendTick;
    private bool _suppressDoorActivateHook;
    private string _transitionDoorKey = string.Empty;
    private bool _timerPausedByExit;
    private string _downedExitFollowDoorKey = string.Empty;
    private long _downedExitFollowStartedTicks;
    private const double DownedExitFollowDelaySeconds = 15.0;

    // Protocol 17 Boss Rush launch barrier (non-blocking, polled per frame by the door coordinator).
    private string _bossRushGateDoorKey = string.Empty;
    private long _bossRushGateStartTicks;
    private const double BossRushLaunchGateTimeoutSeconds = 15.0;

    /// <summary>
    /// Diagnostics for a rendezvous that never completes. The exit is a mutual "everyone at the
    /// same door" gate with no timeout by design (leaving alone would strand the other player), so
    /// when it does not resolve the game simply appears frozen at the doorway with the run timer
    /// paused. The overwhelmingly common cause is a WORLD DESYNC: the peers generated different
    /// layouts, so their exits sit at different grid coordinates and the door keys can never match.
    /// Naming that out loud is the difference between an unexplained hang and an actionable report.
    /// </summary>
    private string _exitStallDoorKey = string.Empty;
    private long _exitStallStartedTicks;
    private long _nextExitStallLogTicks;
    private const double ExitStallReportAfterSeconds = 12.0;
    private const double ExitStallReportIntervalSeconds = 10.0;

    /// <summary>Host-side monotonic id for transition decisions; 0 = none issued this session.</summary>
    private long _nextExitTransitionSequence;

    /// <summary>Highest transition sequence this peer has acted on. Rejects duplicates and replays.</summary>
    private long _lastAppliedExitTransitionSequence;

    /// <summary>
    /// Client fallback deadline. The host owns the decision, but a client that has satisfied the
    /// rendezvous and never receives a commit must not be stranded at the door forever, so after
    /// this long it proceeds on its own and says so. This is a recovery path, not the normal one.
    /// </summary>
    private long _clientAwaitingCommitSinceTicks;
    private const double ClientTransitionCommitFallbackSeconds = 6.0;
    private const double ClientTransitionTimeoutSeconds = 15.0;
    private long _hostAwaitingPeersSinceTicks;
    private const double HostTransitionTimeoutSeconds = 15.0;
    private long _lastHostDlcWarningTick;

    /// <summary>Exit/portal/boss-door entities only — avoids scanning <c>level.entities</c> every hero frame.</summary>
    private readonly List<Entity?> _exitTargetCandidates = new();

    private Level? _exitCandidatesLevel;
    private int _exitTargetCandidatesVersion;
    private Level? _nearestExitCacheLevel;
    private bool _nearestExitCacheHasValue;
    private double _nearestExitCacheHeroX = double.NaN;
    private double _nearestExitCacheHeroY = double.NaN;
    private bool _nearestExitCacheInsideCircle;
    private int _nearestExitCacheCandidatesVersion = -1;
    private Entity? _nearestExitCacheTarget;
    private bool _readyStateCacheDirty = true;
    private bool _watchedDoorCacheDirty = true;
    private bool _doorVisualRefreshDirty = true;
    private bool _exitPointerDirty = true;
    private int _cachedExpectedPlayerCount = 1;
    private string _cachedWatchedDoorKey = string.Empty;
    private int _cachedDownedSignature;
    private bool _hasCachedDownedSignature;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(int cx, int cy), string> _doorKeyCache = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<global::System.Type, bool> _supportedExitTypeCache = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<global::System.Type, string> _exitDestinationByTypeCache = new();
    private static long _lastBossFightCheckTicks;
    private static bool _lastBossFightResult;

    private const double NearestExitCacheReuseDistancePx = 18.0;
    private const double NearestExitCacheReuseDistanceSq = NearestExitCacheReuseDistancePx * NearestExitCacheReuseDistancePx;

    public static LevelExitSync? Instance { get; private set; }

    public LevelExitSync(ModEntry entry)
    {
        _log = entry.Logger;
        Instance = this;
        EventSystem.AddReceiver(this);
    }

    /// <summary>
    /// Resets the Boss Rush transition gate, unpauses run timer, clears pending transition keys,
    /// and unlocks local player controls. Called by watchdogs, disconnect handlers, or timeout handlers.
    /// </summary>
    public void ResetBossRushGateAndUnlock(string reason)
    {
        ClearBossRushGate();
        _localPressed = false;
        _transitionDoorKey = string.Empty;
        _hostAwaitingPeersSinceTicks = 0;
        _clientAwaitingCommitSinceTicks = 0;
        _suppressDoorActivateHook = false;
        ApplyLocalTimerPause(false);

        var hero = ModEntry.me;
        try { hero?.cancelSkillControlLock(); } catch { }
        try { hero?.unlockControls(); } catch { }

        _log.Information("[ExitSync] Boss Rush gate and player controls reset cleanly ({Reason})", reason);
    }

    /// <summary>
    /// Idempotently clears all transition tokens, gate locks, door wait states, and timers during run restart.
    /// </summary>
    public void ResetForRunRestart()
    {
        ResetLevelState(null);
        _transitionDoorKey = string.Empty;
        _localPressed = false;
        _localInsideCircle = false;
        _suppressDoorActivateHook = false;
        _clientAwaitingCommitSinceTicks = 0;
        _hostAwaitingPeersSinceTicks = 0;
        _exitStallDoorKey = string.Empty;
        _exitStallStartedTicks = 0;
        _nextExitStallLogTicks = 0;
        _downedExitFollowDoorKey = string.Empty;
        _downedExitFollowStartedTicks = 0;
        ClearBossRushGate();
        ClearExitPointer();
        ApplyLocalTimerPause(false);

        var hero = ModEntry.me;
        try { hero?.cancelSkillControlLock(); } catch { }
        try { hero?.unlockControls(); } catch { }

        _log.Information("[ExitSync] Run restart reset complete: transition tokens and exit states wiped.");
    }

    void IOnAdvancedModuleInitializing.OnAdvancedModuleInitializing(ModEntry entry)
    {
        entry.Logger.Information("\x1b[32m[[ModEntry.LevelExitSync] Initializing LevelExitSync...]\x1b[0m ");
        Hook_Exit.postUpdate += Hook_Exit_postUpdate;
        Hook_Exit.onActivate += Hook_Exit_onActivate;
        Hook_Portal.onActivate += Hook_Portal_onActivate;
        Hook_BossRushDoor.onActivate += Hook_BossRushDoor_onActivate;
        Hook_Level.registerEntity += Hook_Level_registerEntity;
        Hook_Level.unregisterEntity += Hook_Level_unregisterEntity;
        Hook_Level.onDispose += Hook_Level_onDispose;
    }

    private void Hook_Exit_postUpdate(Hook_Exit.orig_postUpdate orig, Exit self)
    {
        orig(self);
        EnsureDoorVisual(self);
    }

    private void Hook_Exit_onActivate(Hook_Exit.orig_onActivate orig, Exit self, Hero by, bool inf)
    {
        HandleExitTargetActivate(self, by, () => orig(self, by, inf), null);
    }

    private void Hook_Portal_onActivate(Hook_Portal.orig_onActivate orig, Portal self, Hero by, bool lp)
    {
        HandleExitTargetActivate(
            self,
            by,
            () => orig(self, by, lp),
            target => SafeRead(() => target.visible, false));
    }

    private void Hook_BossRushDoor_onActivate(Hook_BossRushDoor.orig_onActivate orig, BossRushDoor self, Hero by, bool cine)
    {
        TryPrecommitBossRushEntranceSeed(self, by);
        HandleExitTargetActivate(
            self,
            by,
            () => orig(self, by, cine),
            target => !SafeRead(() => target.locked, true));
    }

    private void TryPrecommitBossRushEntranceSeed(BossRushDoor door, Hero by)
    {
        if (_suppressDoorActivateHook || door == null || by == null)
            return;

        var localHero = ModEntry.me;
        var net = LobbySession.NetRef;
        if (localHero == null || !ReferenceEquals(by, localHero) ||
            net == null || !net.IsAlive || !net.IsHost)
        {
            return;
        }

        if (door.cx < 0 || door.cy < 0)
        {
            _log.Warning("[ExitSync][BossRushSeed] Refusing to precommit Boss Rush seed: invalid door coordinates {DoorCx}:{DoorCy}", door.cx, door.cy);
            return;
        }

        if (SafeRead(() => door.locked, true) || !IsEntityInsideExitCircle(localHero, door))
            return;

        // Only stage a new run when entering Boss Rush from the normal world. BossRushDoor is also
        // used inside the mode for arena progression; those transitions must continue using the
        // already-running Boss Rush state instead of generating a new run seed.
        var alreadyInBossRush = SafeRead(() => localHero._level?.game?.isBossRush() ?? false, false);
        if (alreadyInBossRush)
            return;

        var bossRushType = SafeRead(() => door.bossRushType?.ToString() ?? string.Empty, string.Empty);
        if (!string.IsNullOrWhiteSpace(bossRushType) &&
            (!LobbySession.TryParseBossRushDoorIndex(bossRushType, out var doorIndex) ||
             !LobbySession.IsValidBossRushDoorIndex(doorIndex)))
        {
            _log.Warning("[ExitSync][BossRushSeed] Refusing to precommit Boss Rush seed: invalid/out-of-range door index (raw='{Type}')", bossRushType);
            return;
        }

        if (LobbySession.PrecommitHostBossRushRunSeed(
                bossRushType,
                door.cx,
                door.cy,
                out var seed,
                out var sequence))
        {
            _log.Information(
                "[ExitSync][BossRushSeed] Host armed Boss Rush door seq={Sequence} seed={Seed} type={BossRushType} door={DoorCx}:{DoorCy}",
                sequence,
                seed,
                string.IsNullOrWhiteSpace(bossRushType) ? "unknown" : bossRushType,
                door.cx,
                door.cy);
        }
    }

    private void Hook_Level_registerEntity(Hook_Level.orig_registerEntity orig, Level self, Entity clid)
    {
        orig(self, clid);
        TryTrackExitTargetCandidate(self, clid);
    }

    private void Hook_Level_unregisterEntity(Hook_Level.orig_unregisterEntity orig, Level self, Entity clid)
    {
        TryUntrackExitTargetCandidate(self, clid);
        orig(self, clid);
    }

    private void Hook_Level_onDispose(Hook_Level.orig_onDispose orig, Level self)
    {
        ModEntry.AbortActiveBossCinematicForTransition("level-onDispose");

        var localLevel = ModEntry.me?._level;
        if (localLevel != null && ReferenceEquals(localLevel, self))
            ModEntry.PrepareRemoteKingsForLevelTransition("level-onDispose-current");

        if (ReferenceEquals(_exitCandidatesLevel, self))
        {
            _exitCandidatesLevel = null;
            _exitTargetCandidates.Clear();
            _exitTargetsByDoorKey.Clear();
            InvalidateNearestExitCache();
            _exitPointerDirty = true;
        }

        orig(self);
    }

    private void HandleExitTargetActivate<T>(T target, Hero by, Action origActivate, Func<T, bool>? canCoordinate)
        where T : Entity
    {
        if (_suppressDoorActivateHook)
        {
            origActivate();
            return;
        }

        if (target == null)
        {
            origActivate();
            return;
        }

        if (canCoordinate != null && !canCoordinate(target))
        {
            origActivate();
            return;
        }

        var localHero = ModEntry.me;
        if (by == null || localHero == null || !ReferenceEquals(by, localHero))
        {
            origActivate();
            return;
        }

        var net = LobbySession.NetRef;
        if (net == null || !net.IsAlive || net.id <= 0)
        {
            origActivate();
            return;
        }

        // DLC Entitlement check: verify destination biome does not require an uninstalled DLC pack
        var destLevel = ResolveExitDestinationLevelId(target);
        if (!DlcEntitlementManager.IsLevelAccessibleLocally(destLevel, out var requiredDlc, out var dlcDisplayName))
        {
            _log.Warning(
                "[ExitSync] Local player cannot enter exit {DoorKey}: destination '{DestLevel}' requires DLC '{RequiredDlc}' ({DisplayName}) which is not installed.",
                BuildDoorKey(target.cx, target.cy),
                destLevel,
                requiredDlc,
                dlcDisplayName);

            MultiplayerUI.PushSystemMessage(
                FormatLocalized("Cannot enter: '{0}' DLC is required.", dlcDisplayName),
                6.0,
                1.0);

            _localPressed = false;
            _localDoorKey = string.Empty;
            _transitionDoorKey = string.Empty;
            ApplyLocalTimerPause(false);
            try { localHero?.cancelSkillControlLock(); } catch { }
            try { localHero?.unlockControls(); } catch { }
            try { UpdateLocalPlayerState(net, forceSend: true); } catch { }
            return;
        }

        // DLC Entitlement peer verification: ensure all connected active peers possess the requisite DLC BEFORE committing readiness or pausing timer!
        if (net != null && net.IsAlive)
        {
            RefreshActivePlayers(net);
            if (DlcEntitlementManager.RequiresDlc(destLevel, out var hostReqMask, out var hostReqDlcId, out var hostReqDlcDisplayName))
            {
                if (!DlcEntitlementManager.IsLevelAccessibleForActivePeers(_activePlayerIds, net.id, destLevel, out var missingPeerId))
                {
                    _log.Warning(
                        "[ExitSync] Activation of exit {DoorKey} to '{DestLevel}' blocked: peer #{PeerId} does not possess required DLC '{DlcId}' ({DlcName}).",
                        BuildDoorKey(target.cx, target.cy),
                        destLevel,
                        missingPeerId,
                        hostReqDlcId,
                        hostReqDlcDisplayName);

                    var missingPlayerLabel = missingPeerId == 1 ? Localize("Host") : ResolveUserDisplayName(missingPeerId, net);
                    MultiplayerUI.PushSystemMessage(
                        FormatLocalized("Cannot enter: {0} is missing '{1}' DLC.", missingPlayerLabel, hostReqDlcDisplayName),
                        7.0,
                        1.0);

                    _localPressed = false;
                    _localDoorKey = string.Empty;
                    _transitionDoorKey = string.Empty;
                    ApplyLocalTimerPause(false);
                    try { localHero?.cancelSkillControlLock(); } catch { }
                    try { localHero?.unlockControls(); } catch { }
                    try { UpdateLocalPlayerState(net, forceSend: true); } catch { }
                    return;
                }
            }
        }

        // Prevent boss arena doors from opening for clients during active boss fights.
        // Block the activation outright: falling through to origActivate() here let the
        // client transition natively and ALONE while the host kept coordinating at the door.
        if (net != null && !net.IsHost && IsInBossFight())
        {
            MultiplayerUI.PushSystemMessage(
                FormatLocalized("The exit stays sealed until the boss falls."),
                4.0,
                1.0);
            return;
        }

        if (!IsEntityInsideExitCircle(localHero, target))
        {
            origActivate();
            return;
        }

        var wasReadyHere = _localPressed &&
                           _localDoorCx == target.cx &&
                           _localDoorCy == target.cy &&
                           _localInsideCircle;
        var targetDoorKey = (_localDoorCx == target.cx && _localDoorCy == target.cy && !string.IsNullOrEmpty(_localDoorKey))
            ? _localDoorKey
            : BuildDoorKey(target.cx, target.cy);
        _localDoorKey = targetDoorKey;
        _localDoorCx = target.cx;
        _localDoorCy = target.cy;
        _localDoorOutOfGame = SafeRead(() => target.isOutOfGame, false);
        _localDoorOnScreen = SafeRead(() => target.isOnScreen, false);
        _localInsideCircle = true;
        _localPressed = true;

        UpdateLocalPlayerState(net!, forceSend: true);
        if (!wasReadyHere)
            PushReachedExitMessage(net!.id, target, net!);

        if (AreAllPlayersReadyForDoor(_localDoorKey, net!))
        {
            if (TryBeginCoordinatedTransition(net!, target, localHero))
            {
                ApplyLocalTimerPause(false);
                TriggerExitTransition(target, localHero, origActivate);
            }
            return;
        }

        // Either a teammate is not here yet, or (on a client) the host has not committed the
        // transition. Deliberately do NOT call origActivate: letting the native activation run
        // would take this player through alone. The hero-update coordinator retries every frame
        // and performs the transition as soon as the commit lands.
        ApplyLocalTimerPause(true);
    }

    void IOnHeroUpdate.OnHeroUpdate(double dt)
    {
        var hero = ModEntry.me;
        var net = LobbySession.NetRef;
        if (hero == null || net == null || !net.IsAlive || net.id <= 0)
        {
            if (_lastLevel != null || _doorVisuals.Count > 0 || _playerStates.Count > 0 || _exitPointer != null)
                ResetLevelState(null);
            ApplyLocalTimerPause(false);
            return;
        }

        var currentLevel = hero._level;
        if (!ReferenceEquals(_lastLevel, currentLevel))
            ResetLevelState(currentLevel);

        if (ConsumeIncomingExitReadyStates(net))
            MarkExitUiStateDirty();

        if (RefreshActivePlayers(net))
            MarkExitUiStateDirty();

        if (PrunePlayerStates(net.id))
            MarkExitUiStateDirty();

        PruneDoorVisuals(currentLevel);

        if (RefreshDownedSignature(net.id))
            MarkExitUiStateDirty();

        var nearestTarget = FindNearestExitTarget(hero, currentLevel, out var insideCircle);
        if (ApplyNearestExitSelection(nearestTarget, insideCircle))
            MarkExitUiStateDirty();
        if (nearestTarget != null)
            EnsureDoorVisual(nearestTarget);

        if (_localPressed && (!_localInsideCircle || string.IsNullOrEmpty(_localDoorKey)))
        {
            _localPressed = false;
            _transitionDoorKey = string.Empty;
            _hostAwaitingPeersSinceTicks = 0;
            MarkExitUiStateDirty();
        }

        UpdateLocalPlayerState(net, forceSend: false);
        ApplyLocalTimerPause(_localPressed && _localInsideCircle);
        RefreshDoorVisuals(net);
        TryDelayedDownedExitFollow(hero, currentLevel, net);

        // A host-issued commit always wins over the local rendezvous evaluation.
        ConsumeExitTransitionCommits(net, hero, currentLevel);

        if (_localPressed &&
            _localInsideCircle &&
            nearestTarget != null &&
            !string.IsNullOrEmpty(_localDoorKey) &&
            !string.Equals(_transitionDoorKey, _localDoorKey, StringComparison.Ordinal) &&
            AreAllPlayersReadyForDoor(_localDoorKey, net))
        {
            _hostAwaitingPeersSinceTicks = 0;
            if (TryBeginCoordinatedTransition(net, nearestTarget, hero))
                TriggerExitTransition(nearestTarget, hero, null);
        }
        else
        {
            _clientAwaitingCommitSinceTicks = 0;
            if (net.IsHost && _localPressed && _localInsideCircle)
            {
                if (_hostAwaitingPeersSinceTicks == 0)
                {
                    _hostAwaitingPeersSinceTicks = Stopwatch.GetTimestamp();
                }
                else
                {
                    var elapsed = Stopwatch.GetElapsedTime(_hostAwaitingPeersSinceTicks).TotalSeconds;
                    if (elapsed >= HostTransitionTimeoutSeconds)
                    {
                        _hostAwaitingPeersSinceTicks = 0;
                        _localPressed = false;
                        _transitionDoorKey = string.Empty;
                        _suppressDoorActivateHook = false;
                        ApplyLocalTimerPause(false);
                        _log.Warning(
                            "[ExitSync] Host timeout waiting for peers at door={DoorKey}. Releasing lock and unpausing timer.",
                            _localDoorKey);
                        MultiplayerUI.PushSystemMessage(
                            Localize("Teammate has not reached the exit. Resuming game."),
                            6.0,
                            1.0);
                        try { hero?.cancelSkillControlLock(); } catch { }
                        try { hero?.unlockControls(); } catch { }
                        if (nearestTarget is BossRushDoor)
                        {
                            ClearBossRushGate();
                            LobbySession.CancelBossRushLaunchGate("host_transition_timeout");
                        }
                    }
                }
            }
            else
            {
                _hostAwaitingPeersSinceTicks = 0;
            }
        }

        ReportStalledExitRendezvous(net);
        UpdateExitPointer(net);
    }

    /// <summary>
    /// Gate between "the rendezvous is satisfied" and "actually go through the door".
    /// </summary>
    /// <remarks>
    /// The host decides and publishes; the client waits for that decision. This is what makes the
    /// transition a transaction with an identity instead of two peers independently reacting to the
    /// same readiness state — which is what allowed a duplicated or replayed readiness burst to
    /// start a second transition. A connected client never falls back to an independent local
    /// transition; it re-announces readiness and waits for the authoritative host commit.
    /// </remarks>
    private bool TryBeginCoordinatedTransition(NetNode net, Entity target, Hero hero)
    {
        if (!net.IsAlive)
            return true;

        var levelId = SafeRead(() => hero._level?.map?.id?.ToString() ?? string.Empty, string.Empty);

        if (net.IsHost)
        {
            RefreshActivePlayers(net);
            var destLevel = ResolveExitDestinationLevelId(target);
            if (!DlcEntitlementManager.IsLevelAccessibleLocally(destLevel, out var hostReqDlc, out var hostDlcName))
            {
                _log.Warning("[ExitSync] Host lacks required DLC '{Dlc}' for destination '{DestLevel}'. Aborting transition.", hostReqDlc, destLevel);
                MultiplayerUI.PushSystemMessage(FormatLocalized("Cannot enter: '{0}' DLC is required.", hostDlcName), 6.0, 1.0);
                _localPressed = false;
                _localDoorKey = string.Empty;
                _transitionDoorKey = string.Empty;
                ApplyLocalTimerPause(false);
                try { hero?.cancelSkillControlLock(); } catch { }
                try { hero?.unlockControls(); } catch { }
                try { UpdateLocalPlayerState(net, forceSend: true); } catch { }
                return false;
            }

            if (DlcEntitlementManager.RequiresDlc(destLevel, out var reqMask, out var reqDlcId, out var reqDlcDisplayName))
            {
                if (!DlcEntitlementManager.IsLevelAccessibleForActivePeers(_activePlayerIds, net.id, destLevel, out var missingPeerId))
                {
                    _log.Warning(
                        "[ExitSync] Transition to '{DestLevel}' blocked: peer #{PeerId} does not possess the required DLC '{DlcId}' ({DlcName}).",
                        destLevel,
                        missingPeerId,
                        reqDlcId,
                        reqDlcDisplayName);

                    var missingPeerLabel = ResolveUserDisplayName(missingPeerId, net);
                    MultiplayerUI.PushSystemMessage(
                        FormatLocalized("Cannot enter: {0} is missing '{1}' DLC.", missingPeerLabel, reqDlcDisplayName),
                        7.0,
                        1.0);

                    _localPressed = false;
                    _localDoorKey = string.Empty;
                    _transitionDoorKey = string.Empty;
                    ApplyLocalTimerPause(false);
                    try { hero?.cancelSkillControlLock(); } catch { }
                    try { hero?.unlockControls(); } catch { }
                    try { UpdateLocalPlayerState(net, forceSend: true); } catch { }
                    return false;
                }
            }

            // Only commit to something we are actually going to do. TriggerExitTransition refuses a
            // fresh Boss Rush entry until its launch gate is synchronized, and that refusal can last
            // seconds. Minting the commit first meant two failures at once: EXITCOMMIT was
            // re-published every frame for the whole hold, and the client — which has no such gate —
            // would act on it and walk through the door alone while the host stayed behind.
            if (target is BossRushDoor && !TryPassBossRushLaunchGate(target))
                return false;

            var sequence = ++_nextExitTransitionSequence;
            _lastAppliedExitTransitionSequence = sequence;
            _clientAwaitingCommitSinceTicks = 0;
            _hostAwaitingPeersSinceTicks = 0;
            try
            {
                // Send redundant burst (3 packets) to protect against single-packet UDP drop
                for (int burst = 0; burst < 3; burst++)
                {
                    net.SendExitTransitionCommit(
                        sequence,
                        target.cx,
                        target.cy,
                        levelId,
                        ResolveExitDestinationLevelId(target));
                }
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "[ExitSync] Failed to publish transition commit");
            }
            return true;
        }

        // Client: if host is no longer connected, proceed with local exit transition to avoid deadlock
        if (!net.IsConnected)
        {
            _clientAwaitingCommitSinceTicks = 0;
            _hostAwaitingPeersSinceTicks = 0;
            var disconnectedDest = ResolveExitDestinationLevelId(target);
            if (!DlcEntitlementManager.IsLevelAccessibleLocally(disconnectedDest, out var discReqDlc, out var discDlcName))
            {
                _log.Warning(
                    "[ExitSync] Host disconnected; aborting transition to destination '{Dest}': missing required DLC '{Dlc}' ({DlcName})",
                    disconnectedDest,
                    discReqDlc,
                    discDlcName);
                MultiplayerUI.PushSystemMessage(
                    FormatLocalized("Cannot enter: '{0}' DLC is required.", discDlcName),
                    6.0,
                    1.0);
                _localPressed = false;
                _localDoorKey = string.Empty;
                _transitionDoorKey = string.Empty;
                ApplyLocalTimerPause(false);
                try { hero?.cancelSkillControlLock(); } catch { }
                try { hero?.unlockControls(); } catch { }
                return false;
            }
            _log.Information("[ExitSync] Host disconnected; proceeding with local exit transition to avoid deadlock");
            return true;
        }

        // Client: hold for the host's decision.
        if (_clientAwaitingCommitSinceTicks == 0)
        {
            _clientAwaitingCommitSinceTicks = Stopwatch.GetTimestamp();
            if (target is BossRushDoor)
            {
                TryPassBossRushLaunchGate(target);
            }
            return false;
        }

        if (target is BossRushDoor)
        {
            TryPassBossRushLaunchGate(target);
        }

        var elapsedSeconds = Stopwatch.GetElapsedTime(_clientAwaitingCommitSinceTicks).TotalSeconds;
        if (elapsedSeconds < ClientTransitionCommitFallbackSeconds)
            return false;

        // Transition Timeout (15s): If host died, crashed or network lagged for > 15 seconds,
        // recover cleanly and unlock client instead of stranding in an infinite black screen or lockup.
        if (elapsedSeconds >= ClientTransitionTimeoutSeconds)
        {
            _clientAwaitingCommitSinceTicks = 0;
            _hostAwaitingPeersSinceTicks = 0;
            _localPressed = false;
            _transitionDoorKey = string.Empty;
            _suppressDoorActivateHook = false;
            ApplyLocalTimerPause(false);
            _log.Warning(
                "[ExitSync] Transition timeout ({Timeout:F0}s) reached waiting for host at door={DoorKey}. Releasing lock and recovering normal state.",
                ClientTransitionTimeoutSeconds,
                _localDoorKey);

            MultiplayerUI.PushSystemMessage(
                Localize("Host transition timed out. Normal controls restored."),
                6.0,
                1.0);

            try { hero?.cancelSkillControlLock(); } catch { }
            try { hero?.unlockControls(); } catch { }

            if (target is BossRushDoor)
            {
                ClearBossRushGate();
                LobbySession.CancelBossRushLaunchGate("client_transition_timeout");
            }

            return false;
        }

        // Never let a connected client independently cross a passage just because a commit packet
        // is late. That fallback created exactly the reported "both used the passage, then ended up
        // desynced" failure: the host and client could invoke two independent native transitions.
        // Keep waiting, refresh our ready state, and let the host's transaction remain authoritative.
        try { UpdateLocalPlayerState(net, forceSend: true); } catch { }
        _log.Warning(
            "[ExitSync] Still waiting for host transition commit after {Seconds:F0}s at door={DoorKey}; re-sent ready state and will not transition locally",
            elapsedSeconds,
            _localDoorKey);
        return false;
    }

    /// <summary>Client: apply the host's authoritative transition decision.</summary>
    private void ConsumeExitTransitionCommits(NetNode net, Hero hero, Level? currentLevel)
    {
        if (!net.TryConsumeExitTransitionCommits(out var commits))
            return;

        try
        {
            if (net.IsHost || currentLevel == null)
                return;

            var localLevelId = SafeRead(() => currentLevel.map?.id?.ToString() ?? string.Empty, string.Empty);

            // Act on the NEWEST valid commit in the batch, not the first. Applying a commit performs
            // a level transition, so acting on an older one and dropping a newer would send this
            // peer through a door the host has already superseded.
            var hasSelected = false;
            var selected = default(NetNode.ExitTransitionCommit);
            for (var i = 0; i < commits.Count; i++)
            {
                var candidate = commits[i];

                // Stale/duplicate rejection by identity, not by timing.
                if (candidate.Sequence <= _lastAppliedExitTransitionSequence)
                    continue;

                // Level fence: a commit authored for a level we are no longer in must never move us.
                if (candidate.FromLevelId.Length > 0 &&
                    localLevelId.Length > 0 &&
                    !string.Equals(candidate.FromLevelId, localLevelId, StringComparison.Ordinal))
                {
                    _log.Warning(
                        "[ExitSync] Ignoring transition commit seq={Sequence} for level={From} while in level={Local}",
                        candidate.Sequence,
                        candidate.FromLevelId,
                        localLevelId);
                    continue;
                }

                if (!hasSelected || candidate.Sequence > selected.Sequence)
                {
                    hasSelected = true;
                    selected = candidate;
                }
            }

            if (hasSelected)
            {
                var commit = selected;
                var destLevel = commit.DestinationLevelId;
                var target = FindExitTargetByCoordinates(currentLevel, commit.DoorCx, commit.DoorCy);
                if (string.IsNullOrWhiteSpace(destLevel) && target != null)
                {
                    destLevel = ResolveExitDestinationLevelId(target);
                }

                // Safe guard: reject transition if destination biome requires a DLC not owned by this client
                if (!DlcEntitlementManager.IsLevelAccessibleLocally(destLevel, out var reqDlc, out var dlcDisplayName))
                {
                    _log.Warning(
                        "[ExitSync] REJECTING host transition commit seq={Sequence}: destination '{DestLevel}' requires DLC '{RequiredDlc}' ({DisplayName}), which is NOT installed on this client. Aborting transition to avoid native crash / black screen.",
                        commit.Sequence,
                        destLevel,
                        reqDlc,
                        dlcDisplayName);

                    MultiplayerUI.PushSystemMessage(
                        FormatLocalized("Host entered a '{0}' DLC biome which is not installed. Transition aborted.", dlcDisplayName),
                        8.0,
                        1.0);

                    _lastAppliedExitTransitionSequence = commit.Sequence;
                    _clientAwaitingCommitSinceTicks = 0;
                    _hostAwaitingPeersSinceTicks = 0;
                    _localPressed = false;
                    _transitionDoorKey = string.Empty;
                    _suppressDoorActivateHook = false;
                    ApplyLocalTimerPause(false);
                    try { hero?.cancelSkillControlLock(); } catch { }
                    try { hero?.unlockControls(); } catch { }
                    try { UpdateLocalPlayerState(net, forceSend: true); } catch { }
                    return;
                }

                if (target == null && !string.IsNullOrWhiteSpace(destLevel))
                {
                    target = FindExitTargetByDestination(currentLevel, destLevel);
                    if (target != null)
                    {
                        _log.Information(
                            "[ExitSync] Fallback located exit target by destination '{DestLevel}' at {DoorCx}:{DoorCy} (host commit was {CommitCx}:{CommitCy})",
                            destLevel,
                            target.cx,
                            target.cy,
                            commit.DoorCx,
                            commit.DoorCy);
                    }
                }

                if (target == null)
                {
                    // The host committed to a door this peer does not have. That is a world
                    // divergence, and silently ignoring it would strand this player behind.
                    _log.Error(
                        "[ExitSync] WORLD DESYNC: host committed transition seq={Sequence} at door {DoorCx}:{DoorCy} in {Level}, " +
                        "but no exit exists there on this client",
                        commit.Sequence,
                        commit.DoorCx,
                        commit.DoorCy,
                        localLevelId);
                    MultiplayerUI.PushSystemMessage(
                        Localize("Level failed to synchronize with the host. Return to the menu and rejoin."),
                        8.0,
                        1.0);
                    // Consume the sequence anyway: the host has moved on, and re-evaluating this
                    // same unusable commit on every later batch would only repeat the error.
                    _lastAppliedExitTransitionSequence = commit.Sequence;
                    return;
                }

                _lastAppliedExitTransitionSequence = commit.Sequence;
                _clientAwaitingCommitSinceTicks = 0;
                _hostAwaitingPeersSinceTicks = 0;
                _log.Information(
                    "[ExitSync] Applying host transition commit seq={Sequence} door={DoorCx}:{DoorCy} to={Dest}",
                    commit.Sequence,
                    commit.DoorCx,
                    commit.DoorCy,
                    commit.DestinationLevelId);

                // Ask immediately for the host-authored destination seed/graph. The host may still
                // be generating it; the LevelStruct/LevelGen gates will keep retrying while loading.
                // Doing this at commit time narrows the transition race substantially on Steam and
                // high-latency direct-IP links.
                if (!string.IsNullOrWhiteSpace(commit.DestinationLevelId))
                {
                    try { net.RequestLevelSeed(commit.DestinationLevelId); } catch { }
                    try { net.RequestLevelGraph(commit.DestinationLevelId); } catch { }
                }

                TriggerExitTransition(target, hero, null);
            }
        }
        finally
        {
            NetNode.ReleaseConsumedList(commits);
        }
    }

    /// <summary>Destination level id of an exit or portal, for cross-peer validation. Empty when unknown.</summary>
    private static string ResolveExitDestinationLevelId(Entity? target)
    {
        if (target == null)
            return string.Empty;

        if (target is Exit exit)
        {
            var dest = SafeRead(() => exit.destLevel?.ToString() ?? string.Empty, string.Empty);
            if (!string.IsNullOrWhiteSpace(dest))
                return dest;

            dest = SafeRead(() => exit.getDestBasedOnNextLevels()?.ToString() ?? string.Empty, string.Empty);
            if (!string.IsNullOrWhiteSpace(dest))
                return dest;

            dest = SafeRead(() => exit.destName?.ToString() ?? string.Empty, string.Empty);
            if (!string.IsNullOrWhiteSpace(dest))
                return dest;

            dest = SafeRead(() => exit.getDestName()?.ToString() ?? string.Empty, string.Empty);
            if (!string.IsNullOrWhiteSpace(dest))
                return dest;

            var dlc = SafeRead(() => (exit as dynamic)?.destMap?.infos?.dlc?.ToString() ?? (exit as dynamic)?.dlc?.ToString() ?? string.Empty, string.Empty);
            if (!string.IsNullOrWhiteSpace(dlc))
                return dlc;
        }

        if (target is Portal portal)
        {
            var dest = SafeRead(() => portal.destMap?.id?.ToString() ?? portal.destMap?.infos?.id?.ToString() ?? string.Empty, string.Empty);
            if (!string.IsNullOrWhiteSpace(dest))
                return dest;

            var dlc = SafeRead(() => portal.destMap?.infos?.dlc?.ToString() ?? (portal as dynamic)?.dlc?.ToString() ?? string.Empty, string.Empty);
            if (!string.IsNullOrWhiteSpace(dlc))
                return dlc;

            dest = SafeRead(() => (portal as dynamic)?.destLevel?.ToString() ?? string.Empty, string.Empty);
            if (!string.IsNullOrWhiteSpace(dest))
                return dest;

            dest = SafeRead(() => (portal as dynamic)?.destName?.ToString() ?? (portal as dynamic)?.destination?.ToString() ?? string.Empty, string.Empty);
            if (!string.IsNullOrWhiteSpace(dest))
                return dest;
        }

        if (target is BossRushDoor)
            return "BossRushZone";

        var cachedDest = _exitDestinationByTypeCache.GetOrAdd(target.GetType(), static type =>
        {
            var typeName = type.Name;
            if (typeName.Contains("RichterCastle", StringComparison.OrdinalIgnoreCase) || typeName.Contains("ExitToRichter", StringComparison.OrdinalIgnoreCase))
                return "RichterCastle";
            if (typeName.Contains("ExitToQueen", StringComparison.OrdinalIgnoreCase) || typeName.Contains("LighthouseExit", StringComparison.OrdinalIgnoreCase))
                return "QueenArena";
            if (typeName.Contains("ExitToDeath", StringComparison.OrdinalIgnoreCase))
                return "DeathArena";
            if (typeName.Contains("ExitToDooku", StringComparison.OrdinalIgnoreCase))
                return "DookuArena";
            if (typeName.Contains("FisherBarque", StringComparison.OrdinalIgnoreCase) || typeName.Contains("ExitToShipwreck", StringComparison.OrdinalIgnoreCase))
                return "Shipwreck";
            if (typeName.Contains("ExitToCastlevania", StringComparison.OrdinalIgnoreCase) || typeName.Contains("ExitToPurple", StringComparison.OrdinalIgnoreCase))
                return "PurpleGarden";
            if (typeName.Contains("ExitToGreenhouse", StringComparison.OrdinalIgnoreCase) || typeName.Contains("ArboretumExit", StringComparison.OrdinalIgnoreCase))
                return "Greenhouse";
            if (typeName.Contains("ExitToSwamp", StringComparison.OrdinalIgnoreCase))
                return "Swamp";
            if (typeName.Contains("ExitToTumulus", StringComparison.OrdinalIgnoreCase) || typeName.Contains("ShrinesExit", StringComparison.OrdinalIgnoreCase))
                return "Tumulus";
            if (typeName.Contains("ExitToCliff", StringComparison.OrdinalIgnoreCase) || typeName.Contains("ShoresExit", StringComparison.OrdinalIgnoreCase))
                return "Cliff";
            if (typeName.Contains("ExitToCavern", StringComparison.OrdinalIgnoreCase))
                return "Cavern";
            if (typeName.Contains("ExitToGiant", StringComparison.OrdinalIgnoreCase))
                return "Giant";
            return string.Empty;
        });

        if (!string.IsNullOrEmpty(cachedDest))
            return cachedDest;

        var dynDest = SafeRead(() => (target as dynamic)?.destLevel?.ToString() ?? (target as dynamic)?.destMap?.id?.ToString() ?? string.Empty, string.Empty);
        if (!string.IsNullOrWhiteSpace(dynDest))
            return dynDest;

        return string.Empty;
    }

    /// <summary>
    /// Emits a rate-limited explanation when the local player has been waiting at an exit far
    /// longer than a teammate could plausibly take to walk over. Reports every peer's door key so a
    /// coordinate mismatch (i.e. divergent world generation) is immediately visible in the log.
    /// </summary>
    private void ReportStalledExitRendezvous(NetNode net)
    {
        var waiting = _localPressed &&
                      _localInsideCircle &&
                      !string.IsNullOrEmpty(_localDoorKey) &&
                      !string.Equals(_transitionDoorKey, _localDoorKey, StringComparison.Ordinal);

        if (!waiting)
        {
            _exitStallDoorKey = string.Empty;
            _exitStallStartedTicks = 0;
            _nextExitStallLogTicks = 0;
            return;
        }

        var now = Stopwatch.GetTimestamp();
        if (!string.Equals(_exitStallDoorKey, _localDoorKey, StringComparison.Ordinal))
        {
            _exitStallDoorKey = _localDoorKey;
            _exitStallStartedTicks = now;
            _nextExitStallLogTicks = 0;
            return;
        }

        if (Stopwatch.GetElapsedTime(_exitStallStartedTicks).TotalSeconds < ExitStallReportAfterSeconds)
            return;
        if (_nextExitStallLogTicks != 0 && now < _nextExitStallLogTicks)
            return;

        _nextExitStallLogTicks = now + (long)(Stopwatch.Frequency * ExitStallReportIntervalSeconds);

        var builder = new System.Text.StringBuilder();
        foreach (var state in _playerStates.Values)
        {
            if (state == null || state.UserId <= 0)
                continue;
            if (builder.Length > 0)
                builder.Append(", ");
            builder.Append(CultureInfo.InvariantCulture, $"#{state.UserId}");
            builder.Append(state.Pressed && state.InsideCircle ? "@" : "~");
            builder.Append(string.IsNullOrEmpty(state.DoorKey) ? "none" : state.DoorKey);
            if (!string.IsNullOrEmpty(state.LevelId))
                builder.Append(CultureInfo.InvariantCulture, $"[{state.LevelId}]");
        }

        _log.Warning(
            "[ExitSync] Exit rendezvous stalled for {Seconds:F0}s at door={DoorKey} level={LevelId} expected={Expected} peers=[{Peers}]. " +
            "Door keys that never match usually mean the players generated different worlds.",
            Stopwatch.GetElapsedTime(_exitStallStartedTicks).TotalSeconds,
            _localDoorKey,
            SafeRead(() => ModEntry.me?._level?.map?.id?.ToString() ?? string.Empty, string.Empty),
            _cachedExpectedPlayerCount,
            builder.ToString());

        MultiplayerUI.PushSystemMessage(
            Localize("Still waiting for your friend at this exit."),
            5.0,
            1.0);
    }


    private void TryDelayedDownedExitFollow(Hero hero, Level? level, NetNode net)
    {
        if (hero == null || level == null || net == null || !net.IsAlive)
            return;
        if (!ModEntry.IsLocalPlayerDowned() || (_localPressed && _localInsideCircle))
        {
            _downedExitFollowDoorKey = string.Empty;
            _downedExitFollowStartedTicks = 0;
            return;
        }

        string candidateKey = string.Empty;
        var localLevelId = SafeRead(() => level.map?.id?.ToString() ?? string.Empty, string.Empty);
        foreach (var state in _playerStates.Values)
        {
            if (state == null || state.UserId <= 0)
                continue;
            if (IsPlayerDownedForExit(state.UserId, net.id))
                continue;

            // If a teammate has already advanced to another level, follow them immediately
            if (!string.IsNullOrEmpty(state.LevelId) &&
                !string.IsNullOrEmpty(localLevelId) &&
                !string.Equals(state.LevelId, localLevelId, StringComparison.Ordinal))
            {
                if (!DlcEntitlementManager.IsLevelAccessibleLocally(state.LevelId, out var followDlc, out var followDlcName))
                {
                    _log.Warning(
                        "[ExitSync] Downed player cannot follow teammate #{TeammateId} to level={DestLevel}: required DLC '{Dlc}' is not installed",
                        state.UserId,
                        state.LevelId,
                        followDlc);
                    _downedExitFollowStartedTicks = 0;
                    _downedExitFollowDoorKey = string.Empty;
                    continue;
                }

                var followTarget = FindExitTargetByDestination(level, state.LevelId);
                if (followTarget != null)
                {
                    _log.Warning(
                        "[ExitSync] Downed player following teammate {TeammateId} to level={DestLevel}",
                        state.UserId,
                        state.LevelId);
                    TriggerExitTransition(followTarget, hero, null);
                    _downedExitFollowDoorKey = string.Empty;
                    _downedExitFollowStartedTicks = 0;
                    return;
                }
            }

            if (!state.Pressed || !state.InsideCircle || string.IsNullOrWhiteSpace(state.DoorKey))
                continue;
            if (!AreAllPlayersReadyForDoor(state.DoorKey, net))
                continue;
            candidateKey = state.DoorKey;
            break;
        }

        if (string.IsNullOrWhiteSpace(candidateKey))
        {
            _downedExitFollowDoorKey = string.Empty;
            _downedExitFollowStartedTicks = 0;
            return;
        }

        var target = FindExitTargetByDoorKey(level, candidateKey);
        if (target == null)
            return;

        var targetDest = ResolveExitDestinationLevelId(target);
        if (!DlcEntitlementManager.IsLevelAccessibleLocally(targetDest, out var downedReqDlc, out var downedDlcName))
        {
            _downedExitFollowDoorKey = string.Empty;
            _downedExitFollowStartedTicks = 0;
            return;
        }

        if (!string.Equals(_downedExitFollowDoorKey, candidateKey, StringComparison.Ordinal))
        {
            _downedExitFollowDoorKey = candidateKey;
            _downedExitFollowStartedTicks = Stopwatch.GetTimestamp();
            MultiplayerUI.PushSystemMessage(
                FormatLocalized("Teammate reached the exit. Following in {0} seconds...", (int)DownedExitFollowDelaySeconds),
                6.0,
                1.0);
            return;
        }

        var elapsed = Stopwatch.GetElapsedTime(_downedExitFollowStartedTicks).TotalSeconds;
        if (elapsed < DownedExitFollowDelaySeconds)
            return;

        try
        {
            // Downed follow still belongs to the same host-authored EXITCOMMIT transaction. The
            // previous timer path invoked the native door directly on every peer and could send a
            // downed client into an independently generated destination. Clients now wait; only
            // the host can authorize and activate this transition.
            if (TryBeginCoordinatedTransition(net, target, hero))
            {
                TriggerExitTransition(target, hero, null);
                _downedExitFollowDoorKey = string.Empty;
                _downedExitFollowStartedTicks = 0;
            }
        }
        catch (Exception ex)
        {
            _log.Warning("[ExitSync] Delayed downed-player exit follow failed: {Message}", ex.Message);
        }
    }

    private void TriggerExitTransition(Entity target, Hero hero, Action? origActivate)
    {
        if (target == null || hero == null)
            return;

        // Safe guard: verify target destination does not require an uninstalled DLC pack
        var dest = ResolveExitDestinationLevelId(target);
        if (!DlcEntitlementManager.IsLevelAccessibleLocally(dest, out var reqDlc, out var dlcName))
        {
            _log.Warning(
                "[ExitSync] Aborting TriggerExitTransition: destination '{Dest}' requires DLC '{Dlc}' ({DlcName}) which is not installed.",
                dest,
                reqDlc,
                dlcName);
            MultiplayerUI.PushSystemMessage(
                FormatLocalized("Cannot transition: '{0}' DLC is required.", dlcName),
                6.0,
                1.0);

            _localPressed = false;
            _localDoorKey = string.Empty;
            _transitionDoorKey = string.Empty;
            _suppressDoorActivateHook = false;
            ApplyLocalTimerPause(false);
            try { hero?.cancelSkillControlLock(); } catch { }
            try { hero?.unlockControls(); } catch { }
            var net = LobbySession.NetRef;
            if (net != null)
            {
                try { UpdateLocalPlayerState(net, forceSend: true); } catch { }
            }
            return;
        }

        // Protocol 17 Boss Rush barrier: never begin a fresh Boss Rush native load until the launch
        // is synchronized (client holds the authoritative host seed; host has the client's RUNQUEUED).
        // Returning here without setting _transitionDoorKey lets the door coordinator re-poll on the
        // next hero frame, so no game/main thread is ever blocked while waiting.
        if (target is BossRushDoor && !TryPassBossRushLaunchGate(target))
            return;

        // Abort any active boss cutscene before initiating level transition
        ModEntry.AbortActiveBossCinematicForTransition(
            string.Create(
                CultureInfo.InvariantCulture,
                $"exit-activate:{target.GetType().Name}:{_transitionDoorKey}"));

        if (ModEntry.IsLocalPlayerDowned())
            ModEntry.ApplyLocalDownedExitPenaltyIfNeeded();

        _transitionDoorKey = BuildDoorKey(target.cx, target.cy);

        // Arm the level-boundary fence for the whole transition: from this activation until the new
        // level's registry commit, mob sync applies nothing and sends nothing. Every consumer of
        // this fence was already implemented and checked, but nothing ever armed it, so in-flight
        // packets for the level being torn down kept mutating mobs during the load — the source of
        // stale old-level state bleeding into the next biome. The commit
        // (ClearSyncQuiesceAfterRebuild) reopens it, and it also self-expires so a transition that
        // never completes cannot leave sync disabled forever.
        DeadCellsMultiplayerMod.Mobs.MobsSynchronization.MobsSynchronization.QuiesceForLevelTransition();

        ModEntry.PrepareRemoteKingsForLevelTransition(
            string.Create(
                CultureInfo.InvariantCulture,
                $"exit-activate:{target.GetType().Name}:{_transitionDoorKey}"));
        _suppressDoorActivateHook = true;
        try
        {
            if (origActivate != null)
                origActivate();
            else
                InvokeExitTargetActivate(target, hero);
        }
        catch (Exception ex)
        {
            _log.Warning("[ExitSync] Failed to trigger level exit transition: {Message}", ex.Message);
            _transitionDoorKey = string.Empty;
        }
        finally
        {
            _suppressDoorActivateHook = false;
        }
    }

    private static void InvokeExitTargetActivate(Entity target, Hero hero)
    {
        if (target is dc.en.Interactive interactive)
            interactive.onActivate(hero, false);
    }

    /// <summary>
    /// Non-blocking gate for a fresh Boss Rush entry. Returns true when the authoritative launch is
    /// synchronized and this peer may invoke the native loader. While it returns false the door
    /// coordinator simply retries next frame; after a bounded timeout it cancels the launch cleanly
    /// (never falling back to a locally generated Boss Rush).
    /// </summary>
    private bool TryPassBossRushLaunchGate(Entity target)
    {
        var net = LobbySession.NetRef;
        if (net == null || !net.IsAlive)
            return true;

        if (target == null)
            return false;

        // Guard against negative door coordinates
        if (target.cx < 0 || target.cy < 0)
        {
            BossSyncDiag.Warn("launch gate rejected door with negative coordinates {DoorCx}:{DoorCy}", target.cx, target.cy);
            return false;
        }

        // Guard against invalid, negative, or out-of-range Boss Rush door index
        if (target is BossRushDoor bossDoor)
        {
            var rawType = SafeRead(() => bossDoor.bossRushType?.ToString() ?? string.Empty, string.Empty);
            if (!string.IsNullOrWhiteSpace(rawType) &&
                (!LobbySession.TryParseBossRushDoorIndex(rawType, out var doorIndex) ||
                 !LobbySession.IsValidBossRushDoorIndex(doorIndex)))
            {
                BossSyncDiag.Warn("launch gate rejected door with invalid/out-of-range index (raw='{Raw}')", rawType);
                return false;
            }
        }

        // Internal Boss Rush arena progression reuses the running run; only a fresh entry from the
        // normal world is gated (mirrors TryPrecommitBossRushEntranceSeed).
        var alreadyInBossRush = SafeRead(() => ModEntry.me?._level?.game?.isBossRush() ?? false, false);
        if (alreadyInBossRush)
        {
            var currentLevelId = SafeRead(() => ModEntry.me?._level?.map?.id?.ToString() ?? string.Empty, string.Empty);
            if (!string.IsNullOrEmpty(currentLevelId) && !LobbySession.IsValidBossRushLevelId(currentLevelId))
            {
                BossSyncDiag.Warn("launch gate rejected invalid in-progress boss rush level id '{Level}'", currentLevelId);
                return false;
            }

            ClearBossRushGate();
            return true;
        }

        // Guard against invalid destination level ID
        var destLevel = ResolveExitDestinationLevelId(target);
        if (!string.IsNullOrEmpty(destLevel) && !LobbySession.IsValidBossRushLevelId(destLevel))
        {
            BossSyncDiag.Warn("launch gate rejected door with invalid destination level id '{Level}'", destLevel);
            return false;
        }

        // Watchdog: if peer dropped connection while waiting at the gate,
        // reset cleanly and unlock players without freezing.
        if (_bossRushGateStartTicks != 0 && (!net.HasRemote || !net.IsConnected))
        {
            BossSyncDiag.Warn("launch gate: peer disconnected while waiting at door {Door}; resetting cleanly", _bossRushGateDoorKey);
            ResetBossRushGateAndUnlock("peer disconnected at launch gate");
            LobbySession.CancelBossRushLaunchGate("peer disconnected during boss rush gate wait");
            MultiplayerUI.PushSystemMessage(
                Localize("Friend disconnected during Boss Rush launch. Controls restored."),
                6.0,
                1.0);
            return false;
        }

        if (LobbySession.TryBeginLocalBossRushLoad(out var reason))
        {
            // Validate the real, runtime-sourced Boss Rush variant (this door's bossRushType) against
            // the authoritative host Route before the client invokes the native loader. A mismatch is
            // a genuine divergence (different Boss Rush entry) and is surfaced in the logs.
            if (!net.IsHost)
            {
                var localVariant = SafeRead(() => (target as BossRushDoor)?.bossRushType?.ToString() ?? string.Empty, string.Empty);
                if (!string.IsNullOrWhiteSpace(localVariant) &&
                    (!LobbySession.TryParseBossRushDoorIndex(localVariant, out var localDoorIndex) ||
                     !LobbySession.IsValidBossRushDoorIndex(localDoorIndex)))
                {
                    BossSyncDiag.Warn("launch gate rejected client door with out-of-range index (raw='{Raw}')", localVariant);
                    return false;
                }
                LobbySession.ValidateClientBossRushVariant(localVariant);
            }
            if (!string.IsNullOrEmpty(_bossRushGateDoorKey))
                BossSyncDiag.Trace("launch gate cleared role={Role} door={Door}", BossSyncDiag.Role(net), _bossRushGateDoorKey);
            ClearBossRushGate();
            return true;
        }

        var key = BuildDoorKey(target.cx, target.cy);
        if (!string.Equals(_bossRushGateDoorKey, key, StringComparison.Ordinal))
        {
            _bossRushGateDoorKey = key;
            _bossRushGateStartTicks = Stopwatch.GetTimestamp();
            BossSyncDiag.Trace(
                "launch gate hold role={Role} door={Door} reason={Reason}",
                BossSyncDiag.Role(net),
                key,
                reason);
            return false;
        }

        // Host/Client watchdog: if synchronization times out, reset cleanly and unlock players.
        if (Stopwatch.GetElapsedTime(_bossRushGateStartTicks).TotalSeconds >= BossRushLaunchGateTimeoutSeconds)
        {
            BossSyncDiag.Warn(
                "launch gate TIMEOUT role={Role} door={Door} reason={Reason}",
                BossSyncDiag.Role(net),
                key,
                reason);
            ResetBossRushGateAndUnlock($"boss rush launch sync timed out ({reason})");
            LobbySession.CancelBossRushLaunchGate($"boss rush launch sync timed out ({reason})");
            MultiplayerUI.PushSystemMessage(
                Localize("Boss Rush could not synchronize with your friend. Approach the door again to retry."),
                7.0,
                1.0);
            return false;
        }

        return false;
    }

    private void ClearBossRushGate()
    {
        _bossRushGateDoorKey = string.Empty;
        _bossRushGateStartTicks = 0;
    }

    private bool ConsumeIncomingExitReadyStates(NetNode net)
    {
        if (!net.TryConsumeExitReadyStates(out var states))
            return false;

        try
        {
            var currentLevel = ModEntry.me?._level ?? _lastLevel;
            var localId = net.id;
            var anyChanged = false;
            for (int i = 0; i < states.Count; i++)
            {
                var state = states[i];
                if (state.UserId <= 0 || state.UserId == localId)
                    continue;

                var wasReady = _playerStates.TryGetValue(state.UserId, out var prev) &&
                               prev.Pressed &&
                               prev.InsideCircle &&
                               prev.DoorCx == state.DoorCx &&
                               prev.DoorCy == state.DoorCy;
                var isReady = state.Pressed && state.InsideCircle;

                // Level boundary: a door key is only meaningful inside the level it came from.
                // A peer that has already moved on (or has not arrived yet) must not be counted as
                // "ready at this door" just because the grid coordinates happen to collide.
                var reportedLevelId = state.LevelId ?? string.Empty;
                var localLevelId = SafeRead(() => currentLevel?.map?.id?.ToString() ?? string.Empty, string.Empty);
                if (reportedLevelId.Length > 0 &&
                    localLevelId.Length > 0 &&
                    !string.Equals(reportedLevelId, localLevelId, StringComparison.Ordinal))
                {
                    // Record presence (so the player still counts as connected) but never as ready.
                    var otherLevelState = GetOrCreatePlayerState(state.UserId);
                    anyChanged |= ApplyPlayerState(
                        otherLevelState,
                        string.Empty,
                        0,
                        0,
                        pressed: false,
                        insideCircle: false,
                        state.IsOutOfGame,
                        state.IsOnScreen);
                    otherLevelState.LevelId = reportedLevelId;
                    otherLevelState.LastTick = Stopwatch.GetTimestamp();

                    // If we are a client and the HOST has already moved to the next level,
                    // the host commit was lost in transit or client was downed/lagged.
                    // Follow the host immediately to avoid deadlock and party separation!
                    if (!net.IsHost && state.UserId == 1 && currentLevel != null)
                    {
                        if (!DlcEntitlementManager.IsLevelAccessibleLocally(reportedLevelId, out var hostFollowDlc, out var hostFollowDlcName))
                        {
                            var nowTick = Stopwatch.GetTimestamp();
                            if (_lastHostDlcWarningTick == 0 || Stopwatch.GetElapsedTime(_lastHostDlcWarningTick).TotalSeconds >= 6.0)
                            {
                                _lastHostDlcWarningTick = nowTick;
                                _log.Warning(
                                    "[ExitSync] Host advanced to level={HostLevel} requiring DLC '{Dlc}', but this client lacks that DLC. Cannot follow.",
                                    reportedLevelId,
                                    hostFollowDlc);
                                MultiplayerUI.PushSystemMessage(
                                    FormatLocalized("Host advanced to '{0}' DLC biome which is not installed.", hostFollowDlcName),
                                    6.0,
                                    1.0);
                            }

                            // Recover client state: unpause timer and restore controls so client isn't trapped frozen
                            _localPressed = false;
                            _localDoorKey = string.Empty;
                            _transitionDoorKey = string.Empty;
                            ApplyLocalTimerPause(false);
                            var hero = ModEntry.me;
                            try { hero?.cancelSkillControlLock(); } catch { }
                            try { hero?.unlockControls(); } catch { }
                            try { UpdateLocalPlayerState(net, forceSend: true); } catch { }
                            continue;
                        }

                        var isDowned = ModEntry.IsLocalPlayerDowned();
                        if (isDowned || _localPressed || _localInsideCircle)
                        {
                            var target = FindExitTargetByDestination(currentLevel, reportedLevelId);
                            if (target != null)
                            {
                                _log.Warning(
                                    "[ExitSync] Host already advanced to level={HostLevel} while client (downed={IsDowned}, pressed={Pressed}) is in level={LocalLevel}. Triggering follow transition.",
                                    reportedLevelId,
                                    isDowned,
                                    _localPressed,
                                    localLevelId);
                                TriggerExitTransition(target, ModEntry.me!, null);
                            }
                        }
                    }
                    continue;
                }

                var trackedState = GetOrCreatePlayerState(state.UserId);
                anyChanged |= ApplyPlayerState(
                    trackedState,
                    state.DoorCx,
                    state.DoorCy,
                    state.Pressed,
                    state.InsideCircle,
                    state.IsOutOfGame,
                    state.IsOnScreen);
                trackedState.LevelId = reportedLevelId;
                trackedState.LastTick = Stopwatch.GetTimestamp();

                if (!wasReady && isReady)
                {
                    var target = FindExitTargetByCoordinates(currentLevel, state.DoorCx, state.DoorCy);
                    PushReachedExitMessage(state.UserId, target, net);
                }
            }

            return anyChanged;
        }
        finally
        {
            NetNode.ReleaseConsumedList(states);
        }
    }

    private bool RefreshActivePlayers(NetNode net)
    {
        _activePlayerScratchIds.Clear();
        if (net.id > 0)
            _activePlayerScratchIds.Add(net.id);

        if (net.IsAlive && net.IsConnected)
        {
            net.CopyRemoteUserIdsTo(_activePlayerScratchIds);
        }

        if (_activePlayerIds.SetEquals(_activePlayerScratchIds))
            return false;

        _activePlayerIds.Clear();
        _activePlayerIds.UnionWith(_activePlayerScratchIds);
        return true;
    }

    private bool PrunePlayerStates(int localId)
    {
        if (_playerStates.Count == 0)
            return false;

        _stalePlayerIds.Clear();
        var now = Stopwatch.GetTimestamp();
        var staleTimeoutTicks = (long)(Stopwatch.Frequency * 10.0);

        foreach (var pair in _playerStates)
        {
            if (pair.Key == localId)
                continue;

            // Prune if no longer an active connected player
            if (!_activePlayerIds.Contains(pair.Key))
            {
                _stalePlayerIds.Add(pair.Key);
                continue;
            }

            // Prune if silent disconnect/packet drop where no update arrived for > 10s
            if (pair.Value.LastTick > 0 && now - pair.Value.LastTick > staleTimeoutTicks)
            {
                _stalePlayerIds.Add(pair.Key);
            }
        }

        if (_stalePlayerIds.Count == 0)
            return false;

        for (int i = 0; i < _stalePlayerIds.Count; i++)
            _playerStates.Remove(_stalePlayerIds[i]);

        return true;
    }

    private void UpdateLocalPlayerState(NetNode net, bool forceSend)
    {
        var localId = net.id;
        if (localId <= 0)
            return;

        var trackedState = GetOrCreatePlayerState(localId);
        var localStateChanged = ApplyPlayerState(
            trackedState,
            _localDoorKey,
            _localDoorCx,
            _localDoorCy,
            _localPressed,
            _localInsideCircle,
            _localDoorOutOfGame,
            _localDoorOnScreen);
        trackedState.LastTick = Stopwatch.GetTimestamp();
        if (localStateChanged)
            MarkExitUiStateDirty();

        var now = Stopwatch.GetTimestamp();
        var resendTicks = (long)(Stopwatch.Frequency * ExitStateResendSeconds);
        var stateFlags = BuildLocalStateFlags();
        var changed = !_hasLastSentState ||
                      _lastSentDoorCx != _localDoorCx ||
                      _lastSentDoorCy != _localDoorCy ||
                      _lastSentStateFlags != stateFlags;
        var timedOut = _lastLocalStateSendTick == 0 || now - _lastLocalStateSendTick >= resendTicks;
        if (!forceSend && !changed && !timedOut)
            return;

        net.SendExitReady(
            _localDoorCx,
            _localDoorCy,
            _localPressed,
            _localInsideCircle,
            _localDoorOutOfGame,
            _localDoorOnScreen,
            SafeRead(() => ModEntry.me?._level?.map?.id?.ToString() ?? string.Empty, string.Empty));
        _hasLastSentState = true;
        _lastSentDoorCx = _localDoorCx;
        _lastSentDoorCy = _localDoorCy;
        _lastSentStateFlags = stateFlags;
        _lastLocalStateSendTick = now;
    }

    private bool AreAllPlayersReadyForDoor(string doorKey, NetNode net)
    {
        if (string.IsNullOrWhiteSpace(doorKey))
            return false;

        EnsureReadyStateCache(net);
        var expected = _cachedExpectedPlayerCount;
        if (expected <= 1)
            return true;

        if (_readyPlayerCounts.TryGetValue(doorKey, out var ready) && ready >= expected)
            return true;

        if (TryParseDoorKey(doorKey, out _, out _))
        {
            var nearbyReady = 0;
            foreach (var kvp in _readyPlayerCounts)
            {
                if (IsDoorProximityMatch(doorKey, kvp.Key, 6))
                {
                    nearbyReady += kvp.Value;
                    if (nearbyReady >= expected)
                        return true;
                }
            }
        }

        return false;
    }

    private int ComputeExpectedPlayerCount(NetNode net)
    {
        var localId = net.id;
        if (!net.IsAlive || !net.IsConnected)
            return 1;

        var now = Stopwatch.GetTimestamp();
        var staleTimeoutTicks = (long)(Stopwatch.Frequency * 10.0);

        return DeadCellsMultiplayerMod.Ghost.CombatSyncHelpers.ComputeExpectedExitPlayerCount(
            net.IsHost,
            localId,
            IsPlayerDownedForExit(localId, localId),
            _activePlayerIds,
            peerId => IsPlayerDownedForExit(peerId, localId),
            peerId =>
            {
                if (!_playerStates.TryGetValue(peerId, out var state) || state == null)
                    return false;
                if (state.LastTick > 0 && (now - state.LastTick) > staleTimeoutTicks)
                    return false;
                return true;
            });
    }

    private static bool IsPlayerDownedForExit(int userId, int localId)
    {
        if (userId <= 0)
            return false;

        if (localId > 0 && userId == localId)
            return ModEntry.IsLocalPlayerDowned();

        return ModEntry.IsRemotePlayerDowned(userId);
    }

    private void PushReachedExitMessage(int userId, Entity? target, NetNode net)
    {
        var playerName = ResolveUserDisplayName(userId, net);
        var destination = ResolveExitDestinationName(target);
        MultiplayerUI.PushSystemMessage(FormatLocalized("{0} reached the exit to {1}", playerName, destination));
    }

    private static string ResolveExitDestinationName(Entity? target)
    {
        if (target == null)
            return Localize("next area");

        if (target is Exit exit)
        {
            var byFunc = SafeRead(() => exit.getDestName()?.ToString() ?? string.Empty, string.Empty);
            if (!string.IsNullOrWhiteSpace(byFunc))
                return byFunc.Trim();

            var byField = SafeRead(() => exit.destName?.ToString() ?? string.Empty, string.Empty);
            if (!string.IsNullOrWhiteSpace(byField))
                return byField.Trim();

            var byLevel = SafeRead(() => exit.destLevel?.ToString() ?? string.Empty, string.Empty);
            if (!string.IsNullOrWhiteSpace(byLevel))
                return byLevel.Trim();
        }

        if (target is Portal portal)
        {
            var mapId = SafeRead(() => portal.destMap?.id?.ToString() ?? string.Empty, string.Empty);
            if (!string.IsNullOrWhiteSpace(mapId))
                return mapId.Trim();
        }

        if (target is BossRushDoor bossDoor)
        {
            var type = SafeRead(() => bossDoor.bossRushType?.ToString() ?? string.Empty, string.Empty);
            if (!string.IsNullOrWhiteSpace(type))
                return type.Trim();
            return Localize("Boss Rush");
        }

        return Localize("next area");
    }

    private static string ResolveUserDisplayName(int userId, NetNode net)
    {
        if (userId <= 0)
            return Localize("Guest");

        if (net.id > 0 && userId == net.id)
            return string.IsNullOrWhiteSpace(LobbySession.Username) ? Localize("Guest") : LobbySession.Username.Trim();

        if (net.TryGetRemoteUsername(userId, out var username))
        {
            var name = username?.Trim();
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }

        if (userId == 1 && !string.IsNullOrWhiteSpace(LobbySession.RemoteUsername))
            return LobbySession.RemoteUsername.Trim();

        return FormatLocalized("Player {0}", userId);
    }

    private static string Localize(string value)
    {
        try
        {
            var localized = Lang.Class.t.get(value.AsHaxeString(), null)?.ToString();
            if (!string.IsNullOrWhiteSpace(localized))
                return localized;
        }
        catch
        {
        }

        return value;
    }

    private static string FormatLocalized(string format, params object[] args)
    {
        var localizedFormat = Localize(format);
        try
        {
            return string.Format(CultureInfo.InvariantCulture, localizedFormat, args);
        }
        catch
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }
    }

    private static string BuildDoorKey(int cx, int cy)
    {
        if (_doorKeyCache.TryGetValue((cx, cy), out var cached))
            return cached;

        var key = string.Create(CultureInfo.InvariantCulture, $"{cx}:{cy}");
        if (_doorKeyCache.Count < 512)
            _doorKeyCache.TryAdd((cx, cy), key);
        return key;
    }

    internal static bool IsDoorProximityMatch(string doorKey1, string doorKey2, int threshold = 6)
    {
        return DeadCellsMultiplayerMod.Ghost.CombatSyncHelpers.IsDoorProximityMatch(doorKey1, doorKey2, threshold);
    }

    internal static bool TryParseDoorKey(string key, out int cx, out int cy)
    {
        return DeadCellsMultiplayerMod.Ghost.CombatSyncHelpers.TryParseDoorKey(key, out cx, out cy);
    }

    private Entity? FindExitTargetByDoorKey(Level? level, string key)
    {
        if (level == null || string.IsNullOrWhiteSpace(key))
            return null;

        EnsureExitTargetCandidates(level);
        if (_exitTargetsByDoorKey.TryGetValue(key, out var direct))
        {
            if (IsTrackedExitTargetCandidate(level, direct))
                return direct;

            _exitTargetsByDoorKey.Remove(key);
        }

        if (!TryParseDoorKey(key, out var cx, out var cy))
            return null;
        var target = FindExitTargetByCoordinates(level, cx, cy);
        if (target != null)
            _exitTargetsByDoorKey[key] = target;
        return target;
    }

    private Entity? FindExitTargetByCoordinates(Level? level, int cx, int cy)
    {
        if (level == null)
            return null;

        EnsureExitTargetCandidates(level);

        var removedCandidates = 0;
        Entity? found = null;
        for (int i = _exitTargetCandidates.Count - 1; i >= 0; i--)
        {
            var entity = _exitTargetCandidates[i];
            if (!IsTrackedExitTargetCandidate(level, entity))
            {
                UnindexExitTargetCandidate(entity);
                _exitTargetCandidates.RemoveAt(i);
                removedCandidates++;
                continue;
            }

            if (found == null && entity!.cx == cx && entity.cy == cy)
            {
                found = entity;
                if (removedCandidates == 0)
                    return found;
            }
        }

        if (removedCandidates > 0)
            HandleRemovedExitTargetCandidates();

        return found;
    }

    private Entity? FindExitTargetByDestination(Level? level, string destinationLevelId)
    {
        if (level == null || string.IsNullOrWhiteSpace(destinationLevelId))
            return null;

        // If destination requires DLC not installed locally, never match an exit target to prevent native crash
        if (!DlcEntitlementManager.IsLevelAccessibleLocally(destinationLevelId, out _, out _))
            return null;

        EnsureExitTargetCandidates(level);
        for (int i = 0; i < _exitTargetCandidates.Count; i++)
        {
            var candidate = _exitTargetCandidates[i];
            if (!IsTrackedExitTargetCandidate(level, candidate) || !IsAvailableExitTarget(candidate))
                continue;

            var dest = ResolveExitDestinationLevelId(candidate);
            if (string.Equals(dest, destinationLevelId, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }
        return null;
    }

    private static bool IsSupportedExitTarget(Entity? entity)
    {
        if (entity is Exit
            or Portal
            or BossRushDoor
            or Teleport
            or ExitToDeathTrigger
            or ExitToQueenTrigger
            or ExitToRichterCastle
            or ExitToDookuTrigger)
        {
            return true;
        }

        if (entity == null)
            return false;

        return _supportedExitTypeCache.GetOrAdd(entity.GetType(), static type =>
        {
            var name = type.Name;
            return name.Contains("Exit", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Portal", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Barque", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Richter", StringComparison.OrdinalIgnoreCase);
        });
    }

    private static bool IsAvailableExitTarget(Entity? entity)
    {
        if (!IsSupportedExitTarget(entity))
            return false;
        if (!SafeRead(() => entity!.visible, true))
            return false;
        if (entity is BossRushDoor bossDoor && SafeRead(() => bossDoor.locked, false))
            return false;
        
        // Prevent boss arena doors from being available for clients during active boss fights
        var net = LobbySession.NetRef;
        if (net != null && !net.IsHost && IsInBossFight())
            return false;
            
        return true;
    }

    private static bool IsInBossFight()
    {
        var now = Environment.TickCount64;
        if (now - _lastBossFightCheckTicks < 500)
            return _lastBossFightResult;

        _lastBossFightCheckTicks = now;
        try
        {
            var hero = ModEntry.me;
            if (hero == null || hero._level == null)
            {
                _lastBossFightResult = false;
                return false;
            }

            var level = hero._level;
            var boss = level.boss;
            if (boss != null && !boss.destroyed && boss.life > 0)
            {
                _lastBossFightResult = true;
                return true;
            }

            // The old level-id fallback kept this true for the whole boss level — including
            // after the kill — which permanently disabled the client's exit door and made the
            // activate hook fall through to an uncoordinated native transition (one player
            // forwards, one stuck). Use the live tracked boss registry instead: it goes false
            // the moment the encounter actually ends and also covers Boss Rush duo clones.
            _lastBossFightResult = DeadCellsMultiplayerMod.Mobs.MobsSynchronization.MobsSynchronization.HasLivingTrackedBoss();
            return _lastBossFightResult;
        }
        catch
        {
            _lastBossFightResult = false;
        }
        return false;
    }

    private DoorVisual EnsureDoorVisual(Entity target)
    {
        var targetCx = target.cx;
        var targetCy = target.cy;
        var key = (targetCx == _localDoorCx && targetCy == _localDoorCy && !string.IsNullOrEmpty(_localDoorKey))
            ? _localDoorKey
            : BuildDoorKey(targetCx, targetCy);

        if (_doorVisuals.TryGetValue(key, out var existing))
        {
            existing.Door = target;
            existing.DoorKey = key;
            return existing;
        }

        var visual = new DoorVisual
        {
            Door = target,
            DoorKey = key
        };

        var parent = target.spr;
        if (parent != null)
        {
            try
            {
                visual.Circle = new Graphics(parent);
                DrawDoorCircle(visual.Circle, false);
                visual.Circle.visible = true;
            }
            catch
            {
                visual.Circle = null;
            }

            try
            {
                var net = LobbySession.NetRef;
                if (net != null && net.IsAlive)
                    EnsureReadyStateCache(net);

                var expected = net != null && net.IsAlive ? _cachedExpectedPlayerCount : System.Math.Max(1, _activePlayerIds.Count);
                var initialLabel = BuildCounterLabel(0, expected);
                visual.Counter = Assets.Class.makeText(initialLabel.AsHaxeString(), dc.ui.Text.Class.COLORS.get("WO".AsHaxeString()), false, parent);
                visual.Counter.customScale = CounterScale;
                visual.Counter.onResize();
                visual.Counter.textColor = CounterColor;
                visual.Counter.alpha = 1.0;
                visual.Counter.y = -ExitCounterYOffsetPx;
                visual.Counter.visible = true;
            }
            catch
            {
                visual.Counter = null;
            }
        }

        _doorVisuals[key] = visual;
        _doorVisualRefreshDirty = true;
        return visual;
    }

    private void UpdateDoorVisual(DoorVisual visual)
    {
        if (visual.Door == null)
            return;

        if (visual.Circle != null)
        {
            var isActive = !string.IsNullOrEmpty(_localDoorKey) &&
                           string.Equals(_localDoorKey, visual.DoorKey, StringComparison.Ordinal) &&
                           _localInsideCircle;
            if (!visual.LastActive.HasValue || visual.LastActive.Value != isActive)
            {
                DrawDoorCircle(visual.Circle, isActive);
                visual.LastActive = isActive;
            }
            visual.Circle.visible = true;
        }
    }

    private void RefreshDoorVisuals(NetNode? net)
    {
        if (_doorVisuals.Count == 0)
        {
            _doorVisualRefreshDirty = false;
            return;
        }

        if (!_doorVisualRefreshDirty)
            return;

        if (net != null && net.IsAlive)
            EnsureReadyStateCache(net);

        var expected = net != null && net.IsAlive ? _cachedExpectedPlayerCount : System.Math.Max(1, _activePlayerIds.Count);

        foreach (var visual in _doorVisuals.Values)
        {
            UpdateDoorVisual(visual);
            UpdateDoorCounterVisual(visual, expected);
        }

        _doorVisualRefreshDirty = false;
    }

    private void UpdateDoorCounterVisual(DoorVisual visual, int expected)
    {
        var counter = visual.Counter;
        if (counter == null)
            return;

        _readyPlayerCounts.TryGetValue(visual.DoorKey, out var ready);
        if (visual.LastReadyCount != ready || visual.LastExpectedCount != expected)
        {
            var label = BuildCounterLabel(ready, expected);
            counter.set_text(label.AsHaxeString());
            visual.LastReadyCount = ready;
            visual.LastExpectedCount = expected;
            visual.LastTextWidth = SafeRead(() => counter.textWidth, label.Length * 10);
            var textWidth = visual.LastTextWidth > 0 ? visual.LastTextWidth : label.Length * 10;
            counter.x = -(textWidth * counter.scaleX) * 0.5;
        }

        counter.visible = true;
    }

    private static string BuildCounterLabel(int ready, int expected)
    {
        if (ready < 0)
            ready = 0;
        if (expected < 1)
            expected = 1;
        if (ready > expected)
            ready = expected;
        return string.Create(CultureInfo.InvariantCulture, $"{ready}/{expected}");
    }

    private static void DrawDoorCircle(Graphics circle, bool active)
    {
        if (circle == null)
            return;

        try
        {
            Graphics g = circle;
            g.clear();
            int color = CircleColor;
            double alpha = active ? CircleAlphaActive : CircleAlphaIdle;
            g.beginFill(Ref<int>.From(ref color), Ref<double>.From(ref alpha));
            g.drawCircle(0.0, 0.0, ExitCircleRadiusPx, Ref<int>.Null);
            g.endFill();
        }
        catch
        {
        }
    }

    private void EnsureExitTargetCandidates(Level? level)
    {
        if (level == null || level.entities == null)
        {
            _exitTargetCandidates.Clear();
            _exitTargetsByDoorKey.Clear();
            _exitCandidatesLevel = null;
            InvalidateNearestExitCache();
            return;
        }

        if (ReferenceEquals(_exitCandidatesLevel, level))
            return;

        _exitCandidatesLevel = level;
        _exitTargetCandidates.Clear();
        _exitTargetsByDoorKey.Clear();
        var entities = level.entities;
        for (int i = 0; i < entities.length; i++)
        {
            var e = entities.getDyn(i) as Entity;
            if (IsSupportedExitTarget(e))
            {
                _exitTargetCandidates.Add(e);
                IndexExitTargetCandidate(e);
            }
        }
        _exitTargetCandidatesVersion++;
        InvalidateNearestExitCache();
        _exitPointerDirty = true;
    }

    private void TryTrackExitTargetCandidate(Level? level, Entity? entity)
    {
        if (!ReferenceEquals(_exitCandidatesLevel, level) || !IsSupportedExitTarget(entity))
            return;

        if (_exitTargetCandidates.Contains(entity))
            return;

        _exitTargetCandidates.Add(entity);
        IndexExitTargetCandidate(entity);
        _exitTargetCandidatesVersion++;
        InvalidateNearestExitCache();
        _exitPointerDirty = true;
    }

    private void TryUntrackExitTargetCandidate(Level? level, Entity? entity)
    {
        if (!ReferenceEquals(_exitCandidatesLevel, level) || entity == null)
            return;

        var removed = false;
        for (int i = _exitTargetCandidates.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_exitTargetCandidates[i], entity))
            {
                UnindexExitTargetCandidate(entity);
                _exitTargetCandidates.RemoveAt(i);
                removed = true;
            }
        }

        if (removed)
        {
            _exitTargetCandidatesVersion++;
            InvalidateNearestExitCache();
            _exitPointerDirty = true;
        }
    }

    private static bool IsTrackedExitTargetCandidate(Level level, Entity? entity)
    {
        if (!IsSupportedExitTarget(entity))
            return false;
        if (!ReferenceEquals(entity!._level, level))
            return false;
        if (SafeRead(() => entity.destroyed, false))
            return false;
        return true;
    }

    private Entity? FindNearestExitTarget(Hero hero, Level? level, out bool insideCircle)
    {
        insideCircle = false;
        if (hero == null || level == null)
            return null;

        EnsureExitTargetCandidates(level);
        if (TryGetCachedNearestExitTarget(hero, level, out var cachedTarget, out insideCircle))
            return cachedTarget;

        var heroX = GetEntityX(hero);
        var heroY = GetEntityY(hero);

        Entity? best = null;
        var bestDistSq = double.MaxValue;
        var removedCandidates = 0;
        for (int i = _exitTargetCandidates.Count - 1; i >= 0; i--)
        {
            var target = _exitTargetCandidates[i];
            if (!IsTrackedExitTargetCandidate(level, target))
            {
                UnindexExitTargetCandidate(target);
                _exitTargetCandidates.RemoveAt(i);
                removedCandidates++;
                continue;
            }

            if (!IsAvailableExitTarget(target))
                continue;

            var dx = GetEntityX(target!) - heroX;
            var dy = GetEntityY(target!) - heroY;
            var distSq = dx * dx + dy * dy;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = target;
            }
        }

        if (best == null)
        {
            if (removedCandidates > 0)
                HandleRemovedExitTargetCandidates();
            CacheNearestExitTarget(level, hero, null, false);
            return null;
        }

        if (removedCandidates > 0)
            HandleRemovedExitTargetCandidates();
        insideCircle = bestDistSq <= ExitCircleRadiusPx * ExitCircleRadiusPx;
        CacheNearestExitTarget(level, hero, best, insideCircle);
        return best;
    }

    private static bool IsEntityInsideExitCircle(Entity entity, Entity target)
    {
        if (entity == null || target == null)
            return false;

        var dx = GetEntityX(entity) - GetEntityX(target);
        var dy = GetEntityY(entity) - GetEntityY(target);
        var distSq = dx * dx + dy * dy;
        return distSq <= ExitCircleRadiusPx * ExitCircleRadiusPx;
    }

    private void UpdateExitPointer(NetNode net)
    {
        if (_exitPointer != null && SafeRead(() => _exitPointer.destroyed, true))
        {
            _exitPointer = null;
            _exitPointerDoorKey = string.Empty;
            _exitPointerDirty = true;
        }

        if (!_exitPointerDirty && _exitPointer != null)
            return;

        var watchedDoor = ResolveWatchedDoorKey(net);
        if (string.IsNullOrWhiteSpace(watchedDoor))
        {
            ClearExitPointer();
            _exitPointerDirty = false;
            return;
        }

        if (net == null || !net.IsAlive || AreAllPlayersReadyForDoor(watchedDoor, net))
        {
            ClearExitPointer();
            _exitPointerDirty = false;
            return;
        }

        if (_exitPointer != null &&
            !SafeRead(() => _exitPointer.destroyed, true) &&
            string.Equals(_exitPointerDoorKey, watchedDoor, StringComparison.Ordinal))
        {
            _exitPointerDirty = false;
            return;
        }

        var level = ModEntry.me?._level ?? _lastLevel;
        var door = FindExitTargetByDoorKey(level, watchedDoor);
        if (door == null)
        {
            ClearExitPointer();
            _exitPointerDirty = false;
            return;
        }

        var watchedDest = ResolveExitDestinationLevelId(door);
        if (!DlcEntitlementManager.IsLevelAccessibleLocally(watchedDest, out _, out _))
        {
            ClearExitPointer();
            _exitPointerDirty = false;
            return;
        }

        if (_exitPointer != null && !SafeRead(() => _exitPointer.destroyed, true))
        {
            _exitPointer.e = door;
            _exitPointerDoorKey = watchedDoor;
            _exitPointerDirty = false;
            return;
        }

        try
        {
            _exitPointer = new Pointer(door, "".AsHaxeString(), 99999.0, MarkerColor);
            _exitPointerDoorKey = watchedDoor;
            PointerFxHelper.SuppressPointerFx(_exitPointer, PointerFxSuppressionKey);
        }
        catch
        {
            _exitPointer = null;
            _exitPointerDoorKey = string.Empty;
        }
        finally
        {
            _exitPointerDirty = false;
        }
    }

    private string ResolveWatchedDoorKey(NetNode? net)
    {
        if (!_watchedDoorCacheDirty)
            return _cachedWatchedDoorKey;

        if (net != null && net.IsAlive)
            EnsureReadyStateCache(net);

        var watchedDoor = string.Empty;
        if (_localPressed && _localInsideCircle && !string.IsNullOrWhiteSpace(_localDoorKey))
        {
            watchedDoor = _localDoorKey;
        }
        else
        {
            var localId = net?.id ?? 0;
            foreach (var pair in _playerStates)
            {
                var state = pair.Value;
                if (!state.Pressed || !state.InsideCircle)
                    continue;
                if (string.IsNullOrWhiteSpace(state.DoorKey))
                    continue;
                if (state.UserId > 0 && IsPlayerDownedForExit(state.UserId, localId))
                    continue;

                watchedDoor = state.DoorKey;
                break;
            }
        }

        _cachedWatchedDoorKey = watchedDoor;
        _watchedDoorCacheDirty = false;
        return watchedDoor;
    }

    private void ClearExitPointer()
    {
        if (_exitPointer == null)
            return;

        try
        {
            _exitPointer.destroy();
        }
        catch
        {
        }
        finally
        {
            _exitPointer = null;
            _exitPointerDoorKey = string.Empty;
        }
    }

    private void PruneDoorVisuals(Level? currentLevel)
    {
        if (_doorVisuals.Count == 0)
            return;

        _staleDoorVisualKeys.Clear();
        foreach (var pair in _doorVisuals)
        {
            var door = pair.Value.Door;
            var remove = door == null || currentLevel == null;
            if (!remove && door != null)
            {
                remove = !ReferenceEquals(door._level, currentLevel) || SafeRead(() => door.destroyed, true);
            }

            if (!remove)
                continue;

            _staleDoorVisualKeys.Add(pair.Key);
        }

        if (_staleDoorVisualKeys.Count == 0)
            return;

        for (int i = 0; i < _staleDoorVisualKeys.Count; i++)
            RemoveDoorVisual(_staleDoorVisualKeys[i]);

        _doorVisualRefreshDirty = true;
        _exitPointerDirty = true;
    }

    private void RemoveDoorVisual(string key)
    {
        if (!_doorVisuals.TryGetValue(key, out var visual))
            return;

        try { visual.Circle?.remove(); } catch { }
        try { visual.Counter?.remove(); } catch { }
        _doorVisuals.Remove(key);
        _doorVisualRefreshDirty = true;
    }

    private void ResetLevelState(Level? newLevel)
    {
        _lastLevel = newLevel;
        _exitCandidatesLevel = null;
        _exitTargetCandidates.Clear();
        _exitTargetsByDoorKey.Clear();
        _exitTargetCandidatesVersion = 0;
        InvalidateNearestExitCache();
        _localDoorKey = string.Empty;
        _localDoorCx = 0;
        _localDoorCy = 0;
        _localPressed = false;
        _localInsideCircle = false;
        _localDoorOutOfGame = true;
        _localDoorOnScreen = false;
        _lastLocalStateSendTick = 0;
        _hasLastSentState = false;
        _lastSentDoorCx = 0;
        _lastSentDoorCy = 0;
        _lastSentStateFlags = 0;
        _transitionDoorKey = string.Empty;
        // Must reset per level. A stale non-zero value carried into the next biome would already be
        // older than the fallback window, so the client's very first rendezvous there would skip
        // waiting for the host commit entirely and transition on its own.
        _clientAwaitingCommitSinceTicks = 0;
        _hostAwaitingPeersSinceTicks = 0;
        _exitStallDoorKey = string.Empty;
        _exitStallStartedTicks = 0;
        _nextExitStallLogTicks = 0;
        // NOTE: _nextExitTransitionSequence / _lastAppliedExitTransitionSequence are deliberately
        // NOT reset here. They are session-monotonic: resetting them per level would let a stale
        // commit from the previous level pass the "newer than last applied" test.
        _downedExitFollowDoorKey = string.Empty;
        _downedExitFollowStartedTicks = 0;
        _lastBossFightCheckTicks = 0;
        _lastBossFightResult = false;
        _readyStateCacheDirty = true;
        _watchedDoorCacheDirty = true;
        _doorVisualRefreshDirty = true;
        _exitPointerDirty = true;
        _cachedExpectedPlayerCount = 1;
        _cachedWatchedDoorKey = string.Empty;
        _hasCachedDownedSignature = false;
        _cachedDownedSignature = 0;
        _exitPointerDoorKey = string.Empty;

        _staleDoorVisualKeys.Clear();
        foreach (var key in _doorVisuals.Keys)
            _staleDoorVisualKeys.Add(key);
        for (int i = 0; i < _staleDoorVisualKeys.Count; i++)
            RemoveDoorVisual(_staleDoorVisualKeys[i]);

        _playerStates.Clear();
        _activePlayerIds.Clear();
        _activePlayerScratchIds.Clear();
        _readyPlayerCounts.Clear();
        ClearExitPointer();
        ClearBossRushGate();
        ApplyLocalTimerPause(false);
    }

    private void InvalidateNearestExitCache()
    {
        _nearestExitCacheLevel = null;
        _nearestExitCacheHasValue = false;
        _nearestExitCacheHeroX = double.NaN;
        _nearestExitCacheHeroY = double.NaN;
        _nearestExitCacheInsideCircle = false;
        _nearestExitCacheCandidatesVersion = -1;
        _nearestExitCacheTarget = null;
    }

    private bool TryGetCachedNearestExitTarget(Hero hero, Level level, out Entity? target, out bool insideCircle)
    {
        target = null;
        insideCircle = false;
        if (!_nearestExitCacheHasValue ||
            !ReferenceEquals(_nearestExitCacheLevel, level) ||
            _nearestExitCacheCandidatesVersion != _exitTargetCandidatesVersion)
        {
            return false;
        }

        var heroX = GetEntityX(hero);
        var heroY = GetEntityY(hero);
        var dx = heroX - _nearestExitCacheHeroX;
        var dy = heroY - _nearestExitCacheHeroY;
        if (dx * dx + dy * dy > NearestExitCacheReuseDistanceSq)
            return false;

        target = _nearestExitCacheTarget;
        if (target == null)
        {
            insideCircle = _nearestExitCacheInsideCircle;
            return true;
        }
        if (!IsTrackedExitTargetCandidate(level, target) || !IsAvailableExitTarget(target))
        {
            InvalidateNearestExitCache();
            target = null;
            return false;
        }

        insideCircle = IsEntityInsideExitCircle(hero, target);
        _nearestExitCacheInsideCircle = insideCircle;
        return true;
    }

    private void CacheNearestExitTarget(Level level, Hero hero, Entity? target, bool insideCircle)
    {
        _nearestExitCacheLevel = level;
        _nearestExitCacheHasValue = true;
        _nearestExitCacheHeroX = GetEntityX(hero);
        _nearestExitCacheHeroY = GetEntityY(hero);
        _nearestExitCacheInsideCircle = insideCircle;
        _nearestExitCacheCandidatesVersion = _exitTargetCandidatesVersion;
        _nearestExitCacheTarget = target;
    }

    private void ApplyLocalTimerPause(bool paused)
    {
        if (_timerPausedByExit == paused)
            return;

        var game = ModEntry.Instance?.game;
        if (game?.data == null)
            return;

        try
        {
            game.data.stopGameTime = paused;
            _timerPausedByExit = paused;
        }
        catch
        {
        }
    }

    private static double GetEntityX(Entity e)
    {
        if (e == null)
            return 0.0;
        if (e.spr != null)
            return e.spr.x;
        return e.cx * 24.0;
    }

    private static double GetEntityY(Entity e)
    {
        if (e == null)
            return 0.0;
        if (e.spr != null)
            return e.spr.y;
        return e.cy * 24.0;
    }

    private static T SafeRead<T>(Func<T> getter, T fallback)
    {
        try { return getter(); } catch { return fallback; }
    }

    private void MarkExitUiStateDirty()
    {
        _readyStateCacheDirty = true;
        _watchedDoorCacheDirty = true;
        _doorVisualRefreshDirty = true;
        _exitPointerDirty = true;
    }

    private bool RefreshDownedSignature(int localId)
    {
        unchecked
        {
            var signature = 17;
            foreach (var userId in _activePlayerIds)
            {
                var downed = IsPlayerDownedForExit(userId, localId) ? 1 : 0;
                var combined = (userId * 397) ^ downed;
                signature += combined;
                signature ^= combined;
            }

            if (_hasCachedDownedSignature && signature == _cachedDownedSignature)
                return false;

            _cachedDownedSignature = signature;
            _hasCachedDownedSignature = true;
            return true;
        }
    }

    private void EnsureReadyStateCache(NetNode net)
    {
        if (!_readyStateCacheDirty)
            return;

        _readyPlayerCounts.Clear();
        var now = Stopwatch.GetTimestamp();
        var staleTimeoutTicks = (long)(Stopwatch.Frequency * 5.0);

        foreach (var state in _playerStates.Values)
        {
            if (state.UserId <= 0)
                continue;
            if (!_activePlayerIds.Contains(state.UserId))
                continue;
            if (IsPlayerDownedForExit(state.UserId, net.id))
                continue;
            if (!state.Pressed || !state.InsideCircle)
                continue;
            if (string.IsNullOrWhiteSpace(state.DoorKey))
                continue;

            // Packet loss guard: ignore readiness if remote hasn't sent an update in > 5.0s
            if (state.UserId != net.id && state.LastTick > 0 && (now - state.LastTick) > staleTimeoutTicks)
                continue;

            if (_readyPlayerCounts.TryGetValue(state.DoorKey, out var count))
                _readyPlayerCounts[state.DoorKey] = count + 1;
            else
                _readyPlayerCounts[state.DoorKey] = 1;
        }

        _cachedExpectedPlayerCount = ComputeExpectedPlayerCount(net);
        _readyStateCacheDirty = false;
    }

    private bool ApplyNearestExitSelection(Entity? nearestTarget, bool insideCircle)
    {
        var newDoorCx = nearestTarget?.cx ?? 0;
        var newDoorCy = nearestTarget?.cy ?? 0;
        var newDoorOutOfGame = nearestTarget == null || SafeRead(() => nearestTarget.isOutOfGame, false);
        var newDoorOnScreen = nearestTarget != null && SafeRead(() => nearestTarget.isOnScreen, false);
        var newInsideCircle = nearestTarget != null && insideCircle;

        var hasTarget = nearestTarget != null;
        var hadTarget = !string.IsNullOrEmpty(_localDoorKey);

        if (hasTarget == hadTarget &&
            _localDoorCx == newDoorCx &&
            _localDoorCy == newDoorCy &&
            _localDoorOutOfGame == newDoorOutOfGame &&
            _localDoorOnScreen == newDoorOnScreen &&
            _localInsideCircle == newInsideCircle)
        {
            return false;
        }

        var newDoorKey = hasTarget ? BuildDoorKey(newDoorCx, newDoorCy) : string.Empty;
        _localDoorKey = newDoorKey;
        _localDoorCx = newDoorCx;
        _localDoorCy = newDoorCy;
        _localDoorOutOfGame = newDoorOutOfGame;
        _localDoorOnScreen = newDoorOnScreen;
        _localInsideCircle = newInsideCircle;
        return true;
    }

    private static bool ApplyPlayerState(
        PlayerExitState trackedState,
        string? doorKey,
        int doorCx,
        int doorCy,
        bool pressed,
        bool insideCircle,
        bool isOutOfGame,
        bool isOnScreen)
    {
        if (trackedState == null)
            return false;

        var coordsChanged = trackedState.DoorCx != doorCx || trackedState.DoorCy != doorCy;
        var changed =
            coordsChanged ||
            trackedState.Pressed != pressed ||
            trackedState.InsideCircle != insideCircle ||
            trackedState.IsOutOfGame != isOutOfGame ||
            trackedState.IsOnScreen != isOnScreen ||
            string.IsNullOrEmpty(trackedState.DoorKey);

        if (coordsChanged || string.IsNullOrEmpty(trackedState.DoorKey))
        {
            if (!string.IsNullOrEmpty(doorKey) && !coordsChanged)
                trackedState.DoorKey = doorKey;
            else if (doorCx == 0 && doorCy == 0)
                trackedState.DoorKey = string.Empty;
            else
                trackedState.DoorKey = doorKey ?? BuildDoorKey(doorCx, doorCy);
        }

        trackedState.DoorCx = doorCx;
        trackedState.DoorCy = doorCy;
        trackedState.Pressed = pressed;
        trackedState.InsideCircle = insideCircle;
        trackedState.IsOutOfGame = isOutOfGame;
        trackedState.IsOnScreen = isOnScreen;
        return changed;
    }

    private static bool ApplyPlayerState(
        PlayerExitState trackedState,
        int doorCx,
        int doorCy,
        bool pressed,
        bool insideCircle,
        bool isOutOfGame,
        bool isOnScreen)
    {
        return ApplyPlayerState(trackedState, null, doorCx, doorCy, pressed, insideCircle, isOutOfGame, isOnScreen);
    }

    private PlayerExitState GetOrCreatePlayerState(int userId)
    {
        if (!_playerStates.TryGetValue(userId, out var state))
        {
            state = new PlayerExitState
            {
                UserId = userId
            };
            _playerStates[userId] = state;
        }

        return state;
    }

    private byte BuildLocalStateFlags()
    {
        byte flags = 0;
        if (_localPressed)
            flags |= 1;
        if (_localInsideCircle)
            flags |= 2;
        if (_localDoorOutOfGame)
            flags |= 4;
        if (_localDoorOnScreen)
            flags |= 8;
        return flags;
    }

    private void IndexExitTargetCandidate(Entity? entity)
    {
        if (!IsSupportedExitTarget(entity))
            return;

        var key = BuildDoorKey(entity!.cx, entity.cy);
        _exitTargetsByDoorKey[key] = entity;
    }

    private void UnindexExitTargetCandidate(Entity? entity)
    {
        if (!IsSupportedExitTarget(entity))
            return;

        var key = BuildDoorKey(entity!.cx, entity.cy);
        if (_exitTargetsByDoorKey.TryGetValue(key, out var indexed) && ReferenceEquals(indexed, entity))
            _exitTargetsByDoorKey.Remove(key);
    }

    private void HandleRemovedExitTargetCandidates()
    {
        _exitTargetCandidatesVersion++;
        InvalidateNearestExitCache();
        _watchedDoorCacheDirty = true;
        _doorVisualRefreshDirty = true;
        _exitPointerDirty = true;
    }

}
