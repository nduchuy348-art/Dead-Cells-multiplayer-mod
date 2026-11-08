using System.Globalization;
using System.Text;
using DeadCellsMultiplayerMod;
using DeadCellsMultiplayerMod.Interaction;
using DeadCellsMultiplayerMod.AdvancedCoop;
using DeadCellsMultiplayerMod.Mobs.MobsSynchronization;
using DeadCellsMultiplayerMod.Network;

public sealed partial class NetNode
{
    // Pending mob traffic can accumulate while the main thread is loading a level. Keep generous
    // bounded queues so a stalled or malformed peer cannot grow memory without limit. The oldest
    // entries are dropped first; generation fencing and newest-per-sync-id consumption make that
    // the correct failure mode for state/move snapshots.
    private const int PendingMobStateLimit = 8192;
    private const int PendingMobMoveLimit = 8192;
    private const int PendingMobHitLimit = 2048;
    private const int PendingMobDieLimit = 2048;
    private const int PendingMobAttackLimit = 2048;
    private const int PendingMobDrawLimit = 4096;
    private const int PendingNetworkLineLimit = 1024;
    private const int PendingAttackLimit = 1024;
    private const int PendingControlStateLimit = 256;
    private const int PendingInteractionLimit = 512;
    private const int PendingBossCineLimit = 64;
    private const int MaxProtocolLineChars = 1_100_000;
    private const int MaxIdentityFieldChars = 128;

    private static void AppendBoundedLocked<T>(List<T> target, IReadOnlyList<T> items, int maxCount)
    {
        if (items == null || items.Count == 0 || maxCount <= 0)
            return;

        var firstIncoming = Math.Max(0, items.Count - maxCount);
        var incomingCount = items.Count - firstIncoming;
        var overflow = target.Count + incomingCount - maxCount;
        if (overflow > 0)
        {
            if (overflow >= target.Count)
                target.Clear();
            else
                target.RemoveRange(0, overflow);
        }

        for (var i = firstIncoming; i < items.Count; i++)
            target.Add(items[i]);
    }

    private static void AppendBoundedLocked<T>(PendingQueue<T> target, IReadOnlyList<T> items, int maxCount)
    {
        target.AppendBounded(items, maxCount);
    }

    private void AppendLatestMobMovesLocked(IReadOnlyList<MobMoveSnapshot> moves)
    {
        if (moves == null || moves.Count == 0)
            return;

        for (var i = 0; i < moves.Count; i++)
        {
            var move = moves[i];
            // Movement is disposable presentation traffic. If the consumer is stalled, keeping a
            // complete history is actively harmful: the game thread would spend time applying
            // positions that are already obsolete. Start a fresh latest-only window at the bound.
            _pendingMobMoves.Upsert((move.Generation, move.Index), move);
        }
    }

    private static void AddBoundedLocked<T>(List<T> target, T item, int maxCount)
    {
        if (maxCount <= 0)
            return;
        if (target.Count >= maxCount)
            target.RemoveRange(0, target.Count - maxCount + 1);
        target.Add(item);
    }

    private static void AddBoundedLocked<T>(PendingQueue<T> target, T item, int maxCount)
    {
        target.AddBounded(item, maxCount);
    }

    /// <summary>
    /// Lines that can be fully handled on the network thread, without hopping to the game thread.
    /// </summary>
    /// <remarks>
    /// Mob traffic (MOBMOVE/MOBSTATE/MOBHIT/...) is by far the highest-volume part of the protocol
    /// and consists only of string parsing plus an append into a <c>_pending*</c> list under
    /// <c>_sync</c>. None of it touches the Haxe runtime. Routing it through the bounded
    /// main-thread action queue therefore bought nothing and cost a great deal: the parse ran on
    /// the render thread, and once that queue filled, back-pressure propagated all the way back
    /// into the socket read loop, so movement snapshots AND the reliable keyframes that repair
    /// them arrived late and bunched. That is invisible with two instances on one machine (the
    /// queue never fills) and severe over real latency and jitter.
    ///
    /// MobSync still consumes the pending lists on the game thread in its frame loop, and every
    /// mob packet carries a generation, so handling these lines earlier than surrounding protocol
    /// lines is safe: a snapshot for a level the peer has not built yet is rejected by the
    /// existing generation fence and re-sent by the host's periodic authoritative resync.
    /// </remarks>
    private bool TryHandleFastPathLine(string line, int? senderId)
    {
        if (IsSupersededNetworkSession())
            return true;
        if (string.IsNullOrEmpty(line))
            return false;

        if (TryHandleMobTrafficFastPath(line, senderId))
            return true;

        if (_role == NetRole.Host)
            return TryHandleHostFastPathLine(line);

        if (_role != NetRole.Client)
            return false;

        return TryHandleClientFastPathLine(line);
    }

    private bool TryHandleMobTrafficFastPath(string line, int? senderId)
    {
        if (line.Length < 4 || !line.StartsWith("MOB", StringComparison.OrdinalIgnoreCase))
            return false;

        // A host relaying another peer's MOBEVENT die must keep its position in the ordered
        // forward stream, so that one case stays on the main-thread path.
        if (_role == NetRole.Host && line.StartsWith("MOBEVENT|", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var forceSenderId = _role == NetRole.Host && senderId.HasValue;
            if (!TryHandleMobTrafficLineCore(line, senderId, forceSenderId, out _))
                return false;

            NetNodeMobTrafficStats.RecordFastPathLine();
            NetTrafficDiagnostics.RecordReceived(line.Length);
            NetTrafficDiagnostics.TryFlush(_log, _role.ToString());
            return true;
        }
        catch (Exception ex)
        {
            // A malformed mob packet must never tear down the receive loop.
            _log.Warning("[NetNode] Mob fast-path line handling failed: {msg}", ex.Message);
            return true;
        }
    }

    /// <summary>
    /// Shared handling for every MOB* protocol line. Pure parse plus a bounded enqueue; safe to run
    /// on either the network thread or the game thread.
    /// </summary>
    private bool TryHandleMobTrafficLineCore(string line, int? senderId, bool forceSenderId, out string? forwardLine)
    {
        forwardLine = null;
        try
        {
            if (line.StartsWith("MOBSTATE2|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["MOBSTATE2|".Length..].TrimEnd('\r', '\n');
                var parsedStates = new List<MobStateSnapshot>();
                if (MobWireBinary.TryParseMobStatesBase64(payload, parsedStates))
                {
                    MobSyncTrace.RecordWireReceive("state", parsedStates.Count, line.Length);
                    lock (_sync)
                    {
                        if (parsedStates.Count > 0)
                            AppendBoundedLocked(_pendingMobStates, parsedStates, PendingMobStateLimit);

                        _hasRemote = true;
                    }
                }
                else
                {
                    _log.Warning("[NetNode] Failed to deserialize MOBSTATE2 payload (length={Length})", payload.Length);
                }

                return true;
            }

            if (line.StartsWith("MOBSTATE|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["MOBSTATE|".Length..];
                var parsedStates = ParseMobStatesPayload(payload);
                if (parsedStates.Count > 0)
                {
                    MobSyncTrace.RecordWireReceive("state", parsedStates.Count, line.Length);
                    lock (_sync)
                    {
                        AppendBoundedLocked(_pendingMobStates, parsedStates, PendingMobStateLimit);
                        _hasRemote = true;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(payload))
                {
                    _log.Warning("[NetNode] Malformed or empty MOBSTATE payload: \"{Payload}\"", ClampProtocolText(payload, 64));
                }
                return true;
            }

            if (line.StartsWith("MOBREG|", StringComparison.OrdinalIgnoreCase))
            {
                if (_role == NetRole.Host)
                    return true;

                var payload = line["MOBREG|".Length..];
                if (TryParseMobRegistryPayload(payload, out var parsedRegistry) && parsedRegistry.Count > 0)
                {
                    lock (_sync)
                    {
                        AppendBoundedLocked(_pendingMobRegistry, parsedRegistry, PendingMobStateLimit);
                        _hasRemote = true;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(payload))
                {
                    _log.Warning("[NetNode] Malformed or empty MOBREG payload: \"{Payload}\"", ClampProtocolText(payload, 64));
                }
                return true;
            }

            if (line.StartsWith("MOBMOVE|", StringComparison.OrdinalIgnoreCase))
            {
                if (_role != NetRole.Host)
                {
                    var payload = line["MOBMOVE|".Length..];
                    var parsedMoves = ParseMobMovesPayload(payload);
                    if (parsedMoves.Count > 0)
                    {
                        lock (_sync)
                        {
                            MobSyncTrace.RecordWireReceive("move", parsedMoves.Count, line.Length);
                            AppendLatestMobMovesLocked(parsedMoves);
                            _hasRemote = true;
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(payload))
                    {
                        _log.Warning("[NetNode] Malformed or empty MOBMOVE payload: \"{Payload}\"", ClampProtocolText(payload, 64));
                    }
                }
                return true;
            }

            if (line.StartsWith("MOBHIT|", StringComparison.OrdinalIgnoreCase))
            {
                if (_role != NetRole.Host)
                    return true;

                var payload = line["MOBHIT|".Length..];
                if (TryParseMobHitPayload(payload, senderId, forceSenderId, out var hit))
                {
                    lock (_sync)
                    {
                        AddBoundedLocked(_pendingMobHits, hit, PendingMobHitLimit);
                        _hasRemote = true;
                    }
                }
                else
                {
                    _log.Warning("[NetNode] Malformed MOBHIT payload: \"{Payload}\"", ClampProtocolText(payload, 64));
                }
                return true;
            }

            if (line.StartsWith("MOBDIE|", StringComparison.OrdinalIgnoreCase))
            {
                if (_role == NetRole.Host)
                    return true;

                var payload = line["MOBDIE|".Length..];
                if (TryParseMobDiePayload(payload, senderId, forceSenderId, out var die))
                {
                    lock (_sync)
                    {
                        AddBoundedLocked(_pendingMobDies, die, PendingMobDieLimit);
                        _hasRemote = true;
                    }
                }
                else
                {
                    _log.Warning("[NetNode] Malformed MOBDIE payload: \"{Payload}\"", ClampProtocolText(payload, 64));
                }
                return true;
            }

            if (line.StartsWith("MOBDRAW|", StringComparison.OrdinalIgnoreCase))
            {
                if (_role != NetRole.Host)
                    return true;

                var payload = line["MOBDRAW|".Length..];
                if (TryParseMobDrawPayload(payload, senderId, forceSenderId, out var draws))
                {
                    lock (_sync)
                    {
                        AppendBoundedLocked(_pendingMobDraws, draws, PendingMobDrawLimit);
                        _hasRemote = true;
                    }
                }
                else
                {
                    _log.Warning("[NetNode] Malformed MOBDRAW payload: \"{Payload}\"", ClampProtocolText(payload, 64));
                }
                return true;
            }

            if (line.StartsWith("MOBEVENT|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["MOBEVENT|".Length..];
                var parsed = ParseMobEventsPayload(payload);
                var effectiveUserId = forceSenderId && senderId.HasValue ? senderId.Value : (senderId ?? 0);
                var hasDieToForward = false;
                if (parsed.Count > 0)
                {
                    lock (_sync)
                    {
                        foreach (var u in parsed)
                        {
                            if (u.Events == null)
                                continue;
                            foreach (var ev in u.Events)
                            {
                                if (string.IsNullOrEmpty(ev))
                                    continue;
                                if (ev.StartsWith("attack|", StringComparison.Ordinal) && _role != NetRole.Host)
                                {
                                    if (TryParseMobAttackEvent(ev, u.Index, u.X, u.Y, u.Dir, u.Type, u.Generation, out var attack))
                                        AddBoundedLocked(_pendingMobAttacks, attack, PendingMobAttackLimit);
                                }
                                else if (ev.StartsWith("hit|", StringComparison.Ordinal))
                                {
                                    if (TryParseMobHitEvent(ev, u.Index, u.X, u.Y, effectiveUserId, u.Type, u.Generation, out var hit))
                                    {
                                        AddBoundedLocked(_pendingMobHits, hit, PendingMobHitLimit);
                                    }
                                }
                                else if (ev == "die")
                                {
                                    var die = new MobDie(effectiveUserId, u.Index, u.X, u.Y, u.Generation, u.Type);
                                    AddBoundedLocked(_pendingMobDies, die, PendingMobDieLimit);
                                    if (_role == NetRole.Host && senderId.HasValue)
                                        hasDieToForward = true;
                                }
                            }
                        }
                        _hasRemote = true;
                    }
                    if (hasDieToForward)
                        forwardLine = line;
                }
                else if (!string.IsNullOrWhiteSpace(payload))
                {
                    _log.Warning("[NetNode] Malformed or empty MOBEVENT payload: \"{Payload}\"", ClampProtocolText(payload, 64));
                }
                return true;
            }

            if (line.StartsWith("MOBATK|", StringComparison.OrdinalIgnoreCase))
            {
                if (_role == NetRole.Host)
                    return true;

                var payload = line["MOBATK|".Length..];
                if (TryParseMobAttackPayload(payload, out var attack))
                {
                    lock (_sync)
                    {
                        AddBoundedLocked(_pendingMobAttacks, attack, PendingMobAttackLimit);
                        _hasRemote = true;
                    }
                }
                else
                {
                    _log.Warning("[NetNode] Malformed MOBATK payload: \"{Payload}\"", ClampProtocolText(payload, 64));
                }
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _log.Warning("[NetNode] Mob packet processing failed: {msg}", ex.Message);
            return true;
        }
    }

    private static bool TryReadBufferedLine(StringBuilder buffer, out string line)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] != '\n')
                continue;

            line = buffer.ToString(0, i).Trim();
            buffer.Remove(0, i + 1);
            return true;
        }

        line = string.Empty;
        return false;
    }

    private static string ClampProtocolText(string? value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || maxChars <= 0)
            return string.Empty;
        return value.Length <= maxChars ? value : value[..maxChars];
    }

    internal bool HandleLine(string line, int? senderId, out string? forwardLine)
    {
        forwardLine = null;
        if (string.IsNullOrWhiteSpace(line))
            return true;

        try
        {
            var forceSenderId = _role == NetRole.Host && senderId.HasValue;

            if (line.StartsWith("ID|"))
            {
                if (_role == NetRole.Host)
                    return true;

                var part = line["ID|".Length..];
                if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId))
                {
                    ID = parsedId;
                    lock (_sync)
                    {
                        _hasRemote = true;
                        _connectedClientCount = 1;
                        if (_primaryRemoteId == 0)
                            _primaryRemoteId = 1;
                    }
                    _log.Information("[NetNode] Assigned ID {Id}", ID);
                    SendDlcMask(DlcEntitlementManager.GetLocalDlcMask());
                    if (!_useSteamTransport)
                    {
                        MainThreadPump.EnqueueCriticalMainThreadCoalesced("net:client-connected", () =>
                        {
                            if (!IsCurrentNetworkSession())
                                return;
                            LobbySession.SetRole(_role);
                            LobbySession.NotifyRemoteConnected(_role);
                        });
                    }
                }
                else
                {
                    _log.Warning("[NetNode] Malformed ID line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("WELCOME", StringComparison.OrdinalIgnoreCase))
            {
                if (_role == NetRole.Client && RejectIncompatiblePeer(line, "WELCOME"))
                    return false;
                lock (_sync) _hasRemote = true;
                return true;
            }

            if (line.StartsWith("HELLO", StringComparison.OrdinalIgnoreCase))
            {
                if (_role == NetRole.Client)
                    return true;

                if (RejectIncompatiblePeer(line, "HELLO"))
                    return false;

                if (_role == NetRole.Host && senderId.HasValue)
                    CompleteHostHandshake(senderId.Value);

                lock (_sync) _hasRemote = true;
                if (_role == NetRole.Host && _useSteamTransport && senderId.HasValue)
                {
                    SteamClientConnection? steamConnection = null;
                    lock (_clientsLock)
                    {
                        _steamClients.TryGetValue(senderId.Value, out steamConnection);
                    }

                    if (steamConnection != null)
                        _ = Task.Run(() => SendInitialStateToSteamClient(steamConnection, forceSend: true));
                }
                return true;
            }

            if (line.StartsWith("PING", StringComparison.OrdinalIgnoreCase))
            {
                lock (_sync) _hasRemote = true;
                return true;
            }

            if (line.StartsWith("GTIME|", StringComparison.OrdinalIgnoreCase))
            {
                if (_role != NetRole.Client)
                    return true;

                var payload = line["GTIME|".Length..];
                var parts = payload.Split('|');
                if (parts.Length >= 2 &&
                    double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var hostGameTimeS) &&
                    double.IsFinite(hostGameTimeS) &&
                    hostGameTimeS >= 0)
                {
                    var hostStopGameTime = parts[1] == "1" || parts[1].Equals("true", StringComparison.OrdinalIgnoreCase);
                    ApplyHostGameTime(hostGameTimeS, hostStopGameTime);
                }
                else
                {
                    _log.Warning("[NetNode] Malformed GTIME line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith(RunLaunchWireCodec.CommitTag + "|", StringComparison.Ordinal))
            {
                var payload = line[(RunLaunchWireCodec.CommitTag.Length + 1)..];
                lock (_sync) _hasRemote = true;
                RunLaunchFlow.ReceiveRunLaunchCommitPayload(payload);
                return true;
            }

            if (line.StartsWith(RunLaunchWireCodec.AckTag + "|", StringComparison.Ordinal))
            {
                var payload = line[(RunLaunchWireCodec.AckTag.Length + 1)..];
                lock (_sync) _hasRemote = true;
                RunLaunchFlow.ReceiveRunLaunchAckPayload(payload);
                return true;
            }

            if (line.StartsWith(RunLaunchWireCodec.ExecuteTag + "|", StringComparison.Ordinal))
            {
                var payload = line[(RunLaunchWireCodec.ExecuteTag.Length + 1)..];
                lock (_sync) _hasRemote = true;
                RunLaunchFlow.ReceiveRunLaunchExecutePayload(payload);
                return true;
            }

            if (line.StartsWith(RunLaunchWireCodec.QueuedTag + "|", StringComparison.Ordinal))
            {
                var payload = line[(RunLaunchWireCodec.QueuedTag.Length + 1)..];
                lock (_sync) _hasRemote = true;
                RunLaunchFlow.ReceiveRunLaunchQueuedPayload(payload);
                return true;
            }

            if (line.StartsWith(RunLaunchWireCodec.ReadyTag + "|", StringComparison.Ordinal))
            {
                var payload = line[(RunLaunchWireCodec.ReadyTag.Length + 1)..];
                lock (_sync) _hasRemote = true;
                RunLaunchFlow.ReceiveRunLevelReadyPayload(payload);
                return true;
            }

            if (line.StartsWith(RunLaunchWireCodec.CancelTag + "|", StringComparison.Ordinal))
            {
                var payload = line[(RunLaunchWireCodec.CancelTag.Length + 1)..];
                lock (_sync) _hasRemote = true;
                RunLaunchFlow.ReceiveRunLaunchCancelPayload(payload);
                return true;
            }

            if (line.StartsWith("SEED|"))
            {
                var partsSeed = line.Split('|');
                if (partsSeed.Length >= 4 &&
                    int.TryParse(partsSeed[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sequence) &&
                    int.TryParse(partsSeed[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hostSeed))
                {
                    var launchKind = ClampProtocolText(partsSeed[3], MaxIdentityFieldChars);
                    lock (_sync) _hasRemote = true;
                    RunLaunchFlow.ReceiveHostRunSeed(sequence, hostSeed, launchKind);
                    _log.Information(
                        "[NetNode] Received host run seed seq={Sequence} seed={Seed} launch={LaunchKind}",
                        sequence,
                        hostSeed,
                        launchKind);
                }
                else
                {
                    _log.Warning("[NetNode] Malformed SEED line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("RESTART|", StringComparison.Ordinal))
            {
                var payload = line["RESTART|".Length..];
                if (int.TryParse(payload, NumberStyles.Integer, CultureInfo.InvariantCulture, out var restartSeed))
                {
                    lock (_sync) _hasRemote = true;
                    RunLaunchFlow.ReceiveHostRunRestart(restartSeed);
                }
                else
                {
                    _log.Warning("[NetNode] Malformed RESTART line: \"{Line}\"", ClampProtocolText(line, 64));
                }

                return true;
            }

            if (line.StartsWith("HXSYNC|", StringComparison.Ordinal))
            {
                var payload = line["HXSYNC|".Length..];
                lock (_sync) _hasRemote = true;
                GameDataSync.ReceiveSerializerSync(payload);
                return true;
            }

            if (line.StartsWith("BOSSRUNE|"))
            {
                if (_role == NetRole.Host)
                {
                    _log.Warning("[NetNode] Host received unexpected BOSSRUNE from client; ignoring.");
                    return true;
                }

                var payload = line["BOSSRUNE|".Length..];
                lock (_sync) _hasRemote = true;
                GameDataSync.ReceiveBossRune(payload);
                return true;
            }

            if (line.StartsWith("BOSSRUNE_UPDATE_CELLS|", StringComparison.OrdinalIgnoreCase))
            {
                if (_role == NetRole.Host)
                {
                    _log.Warning("[NetNode] Host received unexpected BOSSRUNE_UPDATE_CELLS from client; ignoring.");
                    return true;
                }

                var payload = line["BOSSRUNE_UPDATE_CELLS|".Length..].Trim();
                if (TryParseInterBossRuneUpdateCellsPayload(payload, out var ev))
                {
                    lock (_sync)
                    {
                        AddBoundedLocked(_pendingBossRuneUpdateCells, ev, PendingInteractionLimit);
                        _hasRemote = true;
                    }
                }
                else
                {
                    _log.Warning("[NetNode] Malformed BOSSRUNE_UPDATE_CELLS line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("USER|"))
            {
                var payload = line["USER|".Length..];
                var effectiveId = ResolvePayloadId(payload, senderId, out var username);
                username = ClampProtocolText(username, MaxIdentityFieldChars);
                if (forceSenderId)
                    effectiveId = senderId;
                if (effectiveId.HasValue && !string.IsNullOrWhiteSpace(username))
                {
                    int primaryId;
                    lock (_sync)
                    {
                        var state = GetOrCreateRemoteLocked(effectiveId.Value);
                        state.Username = username;
                        state.HasRemote = true;
                        _hasRemote = true;
                        if (_primaryRemoteId == 0)
                            _primaryRemoteId = effectiveId.Value;
                        primaryId = _primaryRemoteId;
                    }

                    if (effectiveId.Value == primaryId)
                        LobbySession.ReceiveRemoteUsername(username);

                    if (_role == NetRole.Host && senderId.HasValue)
                        forwardLine = BuildTaggedLine("USER", effectiveId.Value, username);
                }
                else
                {
                    _log.Warning("[NetNode] Malformed USER line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("READY|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["READY|".Length..];
                var parts = payload.Split('|');
                if (parts.Length >= 2 &&
                    int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedReadyId))
                {
                    var effectiveId = forceSenderId ? senderId : parsedReadyId;
                    if (effectiveId.HasValue)
                    {
                        var ready = string.Equals(parts[1], "1", StringComparison.Ordinal);
                        lock (_sync)
                        {
                            var state = GetOrCreateRemoteLocked(effectiveId.Value);
                            state.Ready = ready;
                            state.HasRemote = true;
                            _hasRemote = true;
                            if (_primaryRemoteId == 0)
                                _primaryRemoteId = effectiveId.Value;
                        }

                        ReadySync.ReceiveRemoteReady(effectiveId.Value, ready);

                        if (_role == NetRole.Host && senderId.HasValue)
                            forwardLine = BuildReadyLine(effectiveId.Value, ready);
                    }
                    else
                    {
                        _log.Warning("[NetNode] Malformed READY sender id: \"{Line}\"", ClampProtocolText(line, 64));
                    }
                }
                else
                {
                    _log.Warning("[NetNode] Malformed READY line: \"{Line}\"", ClampProtocolText(line, 64));
                }

                return true;
            }

            if (line.StartsWith("COOPID|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["COOPID|".Length..];
                var effectiveId = ResolvePayloadId(payload, senderId, out var coopPayload);
                if (forceSenderId)
                    effectiveId = senderId;

                if (effectiveId.HasValue)
                {
                    ParseCoopStatePayload(coopPayload, out var coopId, out var hasContinueSave);
                    lock (_sync)
                    {
                        var state = GetOrCreateRemoteLocked(effectiveId.Value);
                        state.CoopId = coopId;
                        state.HasContinueSave = hasContinueSave;
                        state.HasRemote = true;
                        _hasRemote = true;
                        if (_primaryRemoteId == 0)
                            _primaryRemoteId = effectiveId.Value;
                    }

                    CoopIdentity.ReceiveRemoteCoopState(effectiveId.Value, coopId, hasContinueSave);

                    if (_role == NetRole.Host && senderId.HasValue)
                        forwardLine = BuildCoopStateLine(effectiveId.Value, coopId, hasContinueSave);
                }
                else
                {
                    _log.Warning("[NetNode] Malformed COOPID line: \"{Line}\"", ClampProtocolText(line, 64));
                }

                return true;
            }

            if (line.StartsWith("LAUNCHMODE|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["LAUNCHMODE|".Length..];
                var parts = payload.Split('|');
                if (parts.Length >= 6 &&
                    int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var actionValue))
                {
                    var custom = string.Equals(parts[1], "1", StringComparison.Ordinal);
                    var streamEnabled = string.Equals(parts[2], "1", StringComparison.Ordinal);
                    var newCoopWorldPrepared = string.Equals(parts[3], "1", StringComparison.Ordinal);
                    var coopId = SanitizeProtocolToken(parts[4], 128);
                    var hostHasContinueSave = string.Equals(parts[5], "1", StringComparison.Ordinal);

                    lock (_sync)
                        _hasRemote = true;

                    RunLaunchFlow.ReceiveLaunchMode(
                        actionValue,
                        custom,
                        streamEnabled,
                        newCoopWorldPrepared,
                        coopId,
                        hostHasContinueSave);
                }
                else
                {
                    _log.Warning("[NetNode] Malformed LAUNCHMODE line: \"{Line}\"", ClampProtocolText(line, 64));
                }

                return true;
            }

            if (line.StartsWith("LOBBYSTATE|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["LOBBYSTATE|".Length..];
                lock (_sync) _hasRemote = true;
                CoopAdvancedHardening.ReceiveLobbyState(payload);
                if (_role == NetRole.Host && senderId.HasValue)
                    forwardLine = line.EndsWith("\n", StringComparison.Ordinal) ? line : line + "\n";
                return true;
            }

            if (line.StartsWith("RUNEPROG|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["RUNEPROG|".Length..];
                lock (_sync) _hasRemote = true;
                // Permanent progression belongs to the host's selected save. Clients receive that
                // state for the session, but their own save never imports unlocks into the host.
                if (_role != NetRole.Host)
                    CoopAdvancedHardening.ReceiveRuneProgress(payload);
                return true;
            }

            if (line.StartsWith("HPMULT|"))
            {
                var payload = line["HPMULT|".Length..];
                lock (_sync) _hasRemote = true;
                GameDataSync.ReceiveHpMultipliers(payload);
                return true;
            }

            if (line.StartsWith("LDESC|"))
            {
                var payload = line["LDESC|".Length..];
                lock (_sync) _hasRemote = true;
                LobbySession.ReceiveLevelDesc(payload);
                return true;
            }

            if (line.StartsWith("LSEED|"))
            {
                var payload = line["LSEED|".Length..];
                lock (_sync) _hasRemote = true;
                GameDataSync.ReceiveLevelSeed(payload);
                return true;
            }

            if (line.StartsWith("LGRAPH|"))
            {
                var payload = line["LGRAPH|".Length..];
                lock (_sync) _hasRemote = true;
                GameDataSync.ReceiveLevelGraph(payload);
                return true;
            }

            if (line.StartsWith("SKIN|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["SKIN|".Length..];
                var effectiveId = ResolvePayloadId(payload, senderId, out var skin);
                skin = ClampProtocolText(skin, MaxIdentityFieldChars);
                if (forceSenderId)
                    effectiveId = senderId;
                if (effectiveId.HasValue && !string.IsNullOrWhiteSpace(skin))
                {
                    int primaryId;
                    lock (_sync)
                    {
                        var state = GetOrCreateRemoteLocked(effectiveId.Value);
                        state.Skin = skin;
                        state.HasRemote = true;
                        _hasRemote = true;
                        if (_primaryRemoteId == 0)
                            _primaryRemoteId = effectiveId.Value;
                        primaryId = _primaryRemoteId;
                    }

                    var skinId = effectiveId.Value;
                    var skinValue = skin;
                    MainThreadPump.EnqueueMainThreadCoalesced(string.Create(CultureInfo.InvariantCulture, $"net:skin:{skinId}"), () =>
                    {
                        if (!IsCurrentNetworkSession())
                            return;
                        try
                        {
                            ModEntry.SetClientSkin(skinId, skinValue);
                            if (skinId == primaryId)
                                GameDataSync.ReceiveHeroSkin(skinValue);
                        }
                        catch (Exception ex)
                        {
                            _log.Warning("[NetNode] Failed to handle hero skin: {msg}", ex.Message);
                        }
                    });

                    if (_role == NetRole.Host && senderId.HasValue)
                        forwardLine = BuildTaggedLine("SKIN", effectiveId.Value, skin);
                }
                else
                {
                    _log.Warning("[NetNode] Malformed SKIN line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("HEAD|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["HEAD|".Length..];
                var effectiveId = ResolvePayloadId(payload, senderId, out var skinHead);
                skinHead = ClampProtocolText(skinHead, MaxIdentityFieldChars);
                if (forceSenderId)
                    effectiveId = senderId;
                if (effectiveId.HasValue && !string.IsNullOrWhiteSpace(skinHead))
                {
                    int primaryId;
                    lock (_sync)
                    {
                        var state = GetOrCreateRemoteLocked(effectiveId.Value);
                        state.Head = skinHead;
                        state.HasRemote = true;
                        _hasRemote = true;
                        if (_primaryRemoteId == 0)
                            _primaryRemoteId = effectiveId.Value;
                        primaryId = _primaryRemoteId;
                    }

                    var headId = effectiveId.Value;
                    var headSkinValue = skinHead;
                    MainThreadPump.EnqueueMainThreadCoalesced(string.Create(CultureInfo.InvariantCulture, $"net:head:{headId}"), () =>
                    {
                        if (!IsCurrentNetworkSession())
                            return;
                        try
                        {
                            ModEntry.SetClientHeadSkin(headId, headSkinValue);
                            if (headId == primaryId)
                                GameDataSync.ReceiveHeroHeadSkin(headSkinValue);
                        }
                        catch (Exception ex)
                        {
                            _log.Warning("[NetNode] Failed to handle hero head skin: {msg}", ex.Message);
                        }
                    });

                    if (_role == NetRole.Host && senderId.HasValue)
                        forwardLine = BuildTaggedLine("HEAD", effectiveId.Value, skinHead);
                }
                else
                {
                    _log.Warning("[NetNode] Malformed HEAD line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("GEN|"))
            {
                var payload = line["GEN|".Length..];
                lock (_sync) _hasRemote = true;
                LobbySession.ReceiveGeneratePayload(payload);
                return true;
            }

            if (line.StartsWith("CGDATA|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["CGDATA|".Length..];
                lock (_sync) _hasRemote = true;
                RunLaunchFlow.ReceiveCustomGameData(payload);
                return true;
            }

            if (line.StartsWith("LEVEL|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["LEVEL|".Length..];
                var partsLevel = payload.Split(new[] { '|' }, 2);
                int? parsedId = null;
                string levelValue = payload;
                if (partsLevel.Length >= 2)
                {
                    levelValue = ClampProtocolText(partsLevel[1], MaxIdentityFieldChars);
                    if (int.TryParse(partsLevel[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLevelRemoteId) && parsedLevelRemoteId > 0)
                    {
                        parsedId = parsedLevelRemoteId;
                    }
                }
                levelValue = ClampProtocolText(levelValue, MaxIdentityFieldChars);
                var effectiveId = parsedId ?? senderId;
                if (forceSenderId)
                    effectiveId = senderId;
                if (effectiveId.HasValue && !string.IsNullOrWhiteSpace(levelValue))
                {
                    lock (_sync)
                    {
                        var state = GetOrCreateRemoteLocked(effectiveId.Value);
                        state.LevelId = levelValue;
                        state.HasRemote = true;
                        _hasRemote = true;
                        if (_primaryRemoteId == 0)
                            _primaryRemoteId = effectiveId.Value;
                    }
                }
                else
                {
                    _log.Warning("[NetNode] Malformed LEVEL line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("ZROOM|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["ZROOM|".Length..];
                ParseRoomPayload(payload, out var parsedId, out var roomLevelValue, out var roomIdValue);
                var effectiveId = parsedId ?? senderId;
                if (forceSenderId)
                    effectiveId = senderId;

                if (effectiveId.HasValue && roomIdValue >= 0)
                {
                    lock (_sync)
                    {
                        var state = GetOrCreateRemoteLocked(effectiveId.Value);
                        state.RoomLevelId = roomLevelValue;
                        state.RoomId = roomIdValue;
                        state.HasRoom = true;
                        state.HasRemote = true;
                        _hasRemote = true;
                        if (_primaryRemoteId == 0)
                            _primaryRemoteId = effectiveId.Value;
                    }

                    if (_role == NetRole.Host && senderId.HasValue)
                        forwardLine = BuildRoomLine(effectiveId.Value, roomLevelValue, roomIdValue);
                }
                else
                {
                    _log.Warning("[NetNode] Malformed ZROOM line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("ANIM|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line[(line.IndexOf('|') + 1)..];
                ParseAnimPayload(payload, out var parsedId, out var animName, out var q, out var gFlag);
                var effectiveId = parsedId ?? senderId;
                if (forceSenderId)
                    effectiveId = senderId;
                if (effectiveId.HasValue && !string.IsNullOrWhiteSpace(animName))
                {
                    lock (_sync)
                    {
                        var state = GetOrCreateRemoteLocked(effectiveId.Value);
                        state.Anim = animName;
                        state.AnimQueue = q;
                        state.AnimG = gFlag;
                        state.HasAnim = true;
                        state.HasRemote = true;
                        _hasRemote = true;
                        if (_primaryRemoteId == 0)
                            _primaryRemoteId = effectiveId.Value;
                    }

                    if (_role == NetRole.Host && senderId.HasValue)
                        forwardLine = BuildAnimLine(effectiveId.Value, animName, q, gFlag);
                }
                else
                {
                    _log.Warning("[NetNode] Malformed ANIM line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("HEADANIM|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line[(line.IndexOf('|') + 1)..];
                ParseHeadAnimPayload(payload, out var parsedId, out var animName);
                var effectiveId = parsedId ?? senderId;
                if (forceSenderId)
                    effectiveId = senderId;
                if (effectiveId.HasValue && !string.IsNullOrWhiteSpace(animName))
                {
                    lock (_sync)
                    {
                        var state = GetOrCreateRemoteLocked(effectiveId.Value);
                        state.HeadAnim = animName;
                        state.HasHeadAnim = true;
                        state.HasRemote = true;
                        _hasRemote = true;
                        if (_primaryRemoteId == 0)
                            _primaryRemoteId = effectiveId.Value;
                    }

                    if (_role == NetRole.Host && senderId.HasValue)
                        forwardLine = BuildHeadAnimLine(effectiveId.Value, animName);
                }
                else
                {
                    _log.Warning("[NetNode] Malformed HEADANIM line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("INV|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line[(line.IndexOf('|') + 1)..];
                ParseWeaponPayload(payload, out var parsedId, out var kind, out var slot, out var permanentId, out var ammo);
                var effectiveId = parsedId ?? senderId;
                if (forceSenderId)
                    effectiveId = senderId;
                if (effectiveId.HasValue && !string.IsNullOrWhiteSpace(kind))
                {
                    lock (_sync)
                    {
                        var state = GetOrCreateRemoteLocked(effectiveId.Value);
                        state.WeaponKind = kind;
                        state.WeaponSlot = slot;
                        state.WeaponPermanentId = permanentId;
                        state.WeaponAmmo = ammo ?? int.MinValue;
                        state.HasWeaponUpdate = true;
                        state.HasRemote = true;
                        _hasRemote = true;
                        if (_primaryRemoteId == 0)
                            _primaryRemoteId = effectiveId.Value;
                    }

                    if (_role == NetRole.Host && senderId.HasValue)
                        forwardLine = BuildWeaponLine("INV", effectiveId.Value, kind, slot, permanentId, ammo);
                }
                else
                {
                    _log.Warning("[NetNode] Malformed INV line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("ATK|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line[(line.IndexOf('|') + 1)..];
                ParseAttackPayload(payload, out var parsedId, out var kind, out var slot, out var permanentId, out var ammo, out var action);
                var effectiveId = parsedId ?? senderId;
                if (forceSenderId)
                    effectiveId = senderId;
                if (effectiveId.HasValue && !string.IsNullOrWhiteSpace(kind))
                {
                    lock (_sync)
                    {
                        var state = GetOrCreateRemoteLocked(effectiveId.Value);
                        AddBoundedLocked(_pendingAttacks, new RemoteAttack(effectiveId.Value, kind, slot, permanentId, ammo, action), PendingAttackLimit);
                        state.HasRemote = true;
                        _hasRemote = true;
                        if (_primaryRemoteId == 0)
                            _primaryRemoteId = effectiveId.Value;
                    }

                    if (_role == NetRole.Host && senderId.HasValue)
                        forwardLine = BuildAttackLine(effectiveId.Value, kind, slot, permanentId, ammo, action);
                }
                else
                {
                    _log.Warning("[NetNode] Malformed ATK line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("HEROAFFECT|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line[(line.IndexOf('|') + 1)..];
                var parts = payload.Split('|');
                int? parsedId = null;
                int affectId = 0;
                double sec = 0.0;
                if (parts.Length >= 3 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var idVal) && idVal > 0)
                {
                    parsedId = idVal;
                    int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out affectId);
                    double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out sec);
                }
                else if (parts.Length >= 2)
                {
                    int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out affectId);
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out sec);
                }

                if (!double.IsFinite(sec) || sec < 0.0)
                    sec = 0.0;

                var effectiveId = parsedId ?? senderId;
                if (forceSenderId)
                    effectiveId = senderId;
                if (effectiveId.HasValue && affectId > 0)
                {
                    lock (_sync)
                    {
                        var state = GetOrCreateRemoteLocked(effectiveId.Value);
                        AddBoundedLocked(_pendingHeroAffects, new RemoteAffectEvent(effectiveId.Value, affectId, sec, isRemove: false), 128);
                        state.HasRemote = true;
                        _hasRemote = true;
                    }

                    if (_role == NetRole.Host && senderId.HasValue)
                        forwardLine = $"HEROAFFECT|{effectiveId.Value}|{affectId}|{sec.ToString("F2", CultureInfo.InvariantCulture)}\n";
                }
                else
                {
                    _log.Warning("[NetNode] Malformed HEROAFFECT line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("HEROREMAFFECT|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line[(line.IndexOf('|') + 1)..];
                var parts = payload.Split('|');
                int? parsedId = null;
                int affectId = 0;
                if (parts.Length >= 2 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var idVal) && idVal > 0)
                {
                    parsedId = idVal;
                    int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out affectId);
                }
                else if (parts.Length >= 1)
                {
                    int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out affectId);
                }

                var effectiveId = parsedId ?? senderId;
                if (forceSenderId)
                    effectiveId = senderId;
                if (effectiveId.HasValue && affectId > 0)
                {
                    lock (_sync)
                    {
                        var state = GetOrCreateRemoteLocked(effectiveId.Value);
                        AddBoundedLocked(_pendingHeroAffects, new RemoteAffectEvent(effectiveId.Value, affectId, 0.0, isRemove: true), 128);
                        state.HasRemote = true;
                        _hasRemote = true;
                    }

                    if (_role == NetRole.Host && senderId.HasValue)
                        forwardLine = $"HEROREMAFFECT|{effectiveId.Value}|{affectId}\n";
                }
                else
                {
                    _log.Warning("[NetNode] Malformed HEROREMAFFECT line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("HP|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line[(line.IndexOf('|') + 1)..];
                ParseHpPayload(payload, out var parsedId, out var life, out var maxLife, out var lif, out var bonusLife, out var recover);
                var effectiveId = parsedId ?? senderId;
                if (forceSenderId)
                    effectiveId = senderId;
                if (effectiveId.HasValue)
                {
                    lock (_sync)
                    {
                        var state = GetOrCreateRemoteLocked(effectiveId.Value);
                        state.Life = life;
                        state.MaxLife = maxLife;
                        state.Lif = lif;
                        state.BonusLife = bonusLife;
                        state.Recover = recover;
                        state.HasRemote = true;
                        _hasRemote = true;
                        if (_primaryRemoteId == 0)
                            _primaryRemoteId = effectiveId.Value;
                    }

                    if (_role == NetRole.Host && senderId.HasValue)
                        forwardLine = BuildHpLine(effectiveId.Value, life, maxLife, lif, bonusLife, recover);
                }
                else
                {
                    _log.Warning("[NetNode] Malformed HP line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            // Mob traffic is pure parse + enqueue and never touches the Haxe runtime, so it is handled
            // by one shared core that both this ordered main-thread path and the network-thread fast
            // path can call. Whichever path reaches a given line first is the only one that runs it.
            if (TryHandleMobTrafficLineCore(line, senderId, forceSenderId, out var mobForwardLine))
            {
                forwardLine = mobForwardLine;
                return true;
            }

            if (line.StartsWith("SPAWNANCHOR|", StringComparison.OrdinalIgnoreCase))
            {
                if (_role == NetRole.Host)
                    return true;

                var parts = line["SPAWNANCHOR|".Length..].Split('|');
                if (parts.Length >= 3 &&
                    int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var anchorCx) &&
                    int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var anchorCy))
                {
                    var anchorLevel = ClampProtocolText(parts[2], MaxIdentityFieldChars);
                    var inCombat = false;
                    if (parts.Length >= 4)
                    {
                        var trimmed = parts[3].Trim();
                        inCombat = trimmed == "1" || string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase);
                    }
                    lock (_sync)
                    {
                        _latestHostSpawnAnchor = new HostSpawnAnchor(anchorCx, anchorCy, anchorLevel, inCombat);
                        _hasRemote = true;
                    }
                }
                else
                {
                    _log.Warning("[NetNode] Malformed SPAWNANCHOR line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("EXITCOMMIT|", StringComparison.OrdinalIgnoreCase))
            {
                // Host-authored transition decision. A client must never originate one, and the host
                // must never adopt one: accepting a client-origin commit would let a peer drag the
                // whole party through a door it alone decided on.
                if (_role == NetRole.Host)
                    return true;

                var payload = line["EXITCOMMIT|".Length..];
                if (TryParseExitCommitPayload(payload, out var commit))
                {
                    lock (_sync)
                    {
                        AddBoundedLocked(_pendingExitTransitionCommits, commit, PendingControlStateLimit);
                        _hasRemote = true;
                    }
                }
                else
                {
                    _log.Warning("[NetNode] Malformed EXITCOMMIT line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("EXITREADY|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["EXITREADY|".Length..];
                if (TryParseExitReadyPayload(payload, senderId, forceSenderId, out var state))
                {
                    lock (_sync)
                    {
                        AddBoundedLocked(_pendingExitReadyStates, state, PendingControlStateLimit);
                        _hasRemote = true;
                    }

                    if (_role == NetRole.Host && senderId.HasValue)
                        forwardLine = BuildExitReadyLine(state);
                }
                else
                {
                    _log.Warning("[NetNode] Malformed EXITREADY line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("DLCMASK|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["DLCMASK|".Length..];
                var parts = payload.Split('|');
                if (parts.Length >= 2 &&
                    int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var uid) &&
                    int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mask))
                {
                    var resolvedId = (forceSenderId && senderId.HasValue) ? senderId.Value : uid;
                    if (resolvedId <= 0 && _role == NetRole.Client)
                        resolvedId = 1;
                    if (resolvedId > 0)
                        DlcEntitlementManager.SetPeerDlcMask(resolvedId, mask);
                    if (_role == NetRole.Host && senderId.HasValue)
                        forwardLine = BuildDlcMaskLine(resolvedId, mask);
                }
                return true;
            }

            if (line.StartsWith("PDOWN|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["PDOWN|".Length..];
                if (TryParsePlayerDownPayload(payload, senderId, forceSenderId, out var state))
                {
                    lock (_sync)
                    {
                        AddBoundedLocked(_pendingPlayerDownStates, state, PendingControlStateLimit);
                        _hasRemote = true;
                    }

                    if (_role == NetRole.Host && senderId.HasValue)
                        forwardLine = BuildPlayerDownLine(state);
                }
                else
                {
                    _log.Warning("[NetNode] Malformed PDOWN line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("PREVIVE|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["PREVIVE|".Length..];
                if (TryParsePlayerRevivePayload(payload, senderId, forceSenderId, out var request))
                {
                    lock (_sync)
                    {
                        AddBoundedLocked(_pendingPlayerReviveRequests, request, PendingControlStateLimit);
                        _hasRemote = true;
                    }

                    if (_role == NetRole.Host && senderId.HasValue)
                        forwardLine = BuildPlayerReviveLine(request);
                }
                else
                {
                    _log.Warning("[NetNode] Malformed PREVIVE line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("BOSSCINE|", StringComparison.OrdinalIgnoreCase))
            {
                // Boss-room presentation is host-authoritative. Never enqueue or relay a client-origin
                // intro back to the listen server/other peers; doing so can restart a live arena and
                // freeze both the host boss and every player behind a second cinematic lock.
                if (_role == NetRole.Host)
                    return true;

                var payload = line["BOSSCINE|".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(payload))
                {
                    var levelId = payload.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(levelId))
                    {
                        lock (_sync)
                        {
                            AddBoundedLocked(_pendingBossCineLevelIds, levelId, PendingBossCineLimit);
                            _hasRemote = true;
                        }
                    }
                }
                else
                {
                    _log.Warning("[NetNode] Malformed BOSSCINE line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("INTERDOOR|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["INTERDOOR|".Length..];
                if (TryParseInterDoorPayload(payload, senderId, forceSenderId, out var ev))
                {
                    lock (_sync)
                    {
                        AddBoundedLocked(_pendingInterDoorEvents, ev, PendingInteractionLimit);
                        _hasRemote = true;
                    }

                    if (_role == NetRole.Host && senderId.HasValue)
                        forwardLine = $"INTERDOOR|{ev.UserId}|{ev.X.ToString(CultureInfo.InvariantCulture)}|{ev.Y.ToString(CultureInfo.InvariantCulture)}|{ev.Action}|{(ev.Broken ? 1 : 0)}|{ev.LevelId}\n";
                }
                else
                {
                    _log.Warning("[NetNode] Malformed INTERDOOR line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("INTERELEV|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["INTERELEV|".Length..];
                if (TryParseInterElevatorPayload(payload, senderId, forceSenderId, out var ev))
                {
                    lock (_sync)
                    {
                        AddBoundedLocked(_pendingInterElevatorEvents, ev, PendingInteractionLimit);
                        _hasRemote = true;
                    }

                    if (_role == NetRole.Host && senderId.HasValue)
                    {
                        forwardLine =
                            $"INTERELEV|{ev.UserId.ToString(CultureInfo.InvariantCulture)}|{ev.X.ToString(CultureInfo.InvariantCulture)}|{ev.Y.ToString(CultureInfo.InvariantCulture)}|{ev.Sequence.ToString(CultureInfo.InvariantCulture)}|{ev.LevelId}\n";
                    }
                }
                else
                {
                    _log.Warning("[NetNode] Malformed INTERELEV line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("INTERELEVSTATE|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["INTERELEVSTATE|".Length..];
                if (TryParseInterElevatorStatePayload(payload, senderId, forceSenderId, out var ev))
                {
                    lock (_sync)
                    {
                        AddBoundedLocked(_pendingInterElevatorStateEvents, ev, PendingInteractionLimit);
                        _hasRemote = true;
                    }

                    if (_role == NetRole.Host && senderId.HasValue)
                    {
                        forwardLine =
                            $"INTERELEVSTATE|{ev.UserId.ToString(CultureInfo.InvariantCulture)}|{ev.AnchorX.ToString(CultureInfo.InvariantCulture)}|{ev.AnchorY.ToString(CultureInfo.InvariantCulture)}|{ev.Sequence.ToString(CultureInfo.InvariantCulture)}|{ev.PlatformX.ToString(CultureInfo.InvariantCulture)}|{ev.PlatformY.ToString(CultureInfo.InvariantCulture)}|{(ev.Moving ? 1 : 0)}|{ev.LevelId}\n";
                    }
                }
                else
                {
                    _log.Warning("[NetNode] Malformed INTERELEVSTATE line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("INTERPLATE|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["INTERPLATE|".Length..];
                if (TryParseInterPressurePlatePayload(payload, senderId, forceSenderId, out var ev))
                {
                    lock (_sync)
                    {
                        AddBoundedLocked(_pendingInterPressurePlateEvents, ev, PendingInteractionLimit);
                        _hasRemote = true;
                    }

                    if (_role == NetRole.Host && senderId.HasValue)
                        forwardLine = $"INTERPLATE|{ev.UserId.ToString(CultureInfo.InvariantCulture)}|{ev.X.ToString(CultureInfo.InvariantCulture)}|{ev.Y.ToString(CultureInfo.InvariantCulture)}|{ev.Sequence.ToString(CultureInfo.InvariantCulture)}|{ev.LevelId}\n";
                }
                else
                {
                    _log.Warning("[NetNode] Malformed INTERPLATE line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("INTERCHEST|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["INTERCHEST|".Length..];
                if (TryParseInterTreasureChestPayload(payload, out var ev))
                {
                    lock (_sync)
                    {
                        AddBoundedLocked(_pendingInterTreasureChestEvents, ev, PendingInteractionLimit);
                        _hasRemote = true;
                    }

                    if (_role == NetRole.Host && senderId.HasValue)
                        forwardLine = $"INTERCHEST|{ev.X.ToString(CultureInfo.InvariantCulture)}|{ev.Y.ToString(CultureInfo.InvariantCulture)}|{ev.LevelId}\n";
                }
                else
                {
                    _log.Warning("[NetNode] Malformed INTERCHEST line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("INTERVINELADDER|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["INTERVINELADDER|".Length..];
                if (TryParseInterVineLadderPayload(payload, out var ev))
                {
                    lock (_sync)
                    {
                        AddBoundedLocked(_pendingInterVineLadderEvents, ev, PendingInteractionLimit);
                        _hasRemote = true;
                    }

                    if (_role == NetRole.Host && senderId.HasValue)
                        forwardLine = $"INTERVINELADDER|{ev.X.ToString(CultureInfo.InvariantCulture)}|{ev.Y.ToString(CultureInfo.InvariantCulture)}|{ev.LevelId}\n";
                }
                else
                {
                    _log.Warning("[NetNode] Malformed INTERVINELADDER line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("INTERTELEPORT|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["INTERTELEPORT|".Length..];
                if (TryParseInterTeleportPayload(payload, out var ev))
                {
                    lock (_sync)
                    {
                        AddBoundedLocked(_pendingInterTeleportEvents, ev, PendingInteractionLimit);
                        _hasRemote = true;
                    }

                    if (_role == NetRole.Host && senderId.HasValue)
                        forwardLine = $"INTERTELEPORT|{ev.X.ToString(CultureInfo.InvariantCulture)}|{ev.Y.ToString(CultureInfo.InvariantCulture)}|{ev.LevelId}\n";
                }
                else
                {
                    _log.Warning("[NetNode] Malformed INTERTELEPORT line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("BOSSHEROTELE|", StringComparison.OrdinalIgnoreCase))
            {
                if (_role == NetRole.Host)
                    return true;

                var payload = line["BOSSHEROTELE|".Length..];
                if (TryParseBossHeroTeleportPayload(payload, senderId, forceSenderId, out var ev))
                {
                    lock (_sync)
                    {
                        AddBoundedLocked(_pendingBossHeroTeleports, ev, PendingInteractionLimit);
                        _hasRemote = true;
                    }
                }
                else
                {
                    _log.Warning("[NetNode] Malformed BOSSHEROTELE line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("INTERBREAK|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["INTERBREAK|".Length..];
                if (TryParseInterBreakableGroundPayload(payload, out var ev))
                {
                    lock (_sync)
                    {
                        AddBoundedLocked(_pendingInterBreakableGroundEvents, ev, PendingInteractionLimit);
                        _hasRemote = true;
                    }

                    if (_role == NetRole.Host && senderId.HasValue)
                        forwardLine = $"INTERBREAK|{ev.X.ToString(CultureInfo.InvariantCulture)}|{ev.Y.ToString(CultureInfo.InvariantCulture)}|{ev.LevelId}\n";
                }
                else
                {
                    _log.Warning("[NetNode] Malformed INTERBREAK line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("INTERPORTAL|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["INTERPORTAL|".Length..];
                if (TryParseInterPortalPayload(payload, out var ev))
                {
                    lock (_sync)
                    {
                        AddBoundedLocked(_pendingInterPortalEvents, ev, PendingInteractionLimit);
                        _hasRemote = true;
                    }

                    if (_role == NetRole.Host && senderId.HasValue)
                        forwardLine = $"INTERPORTAL|{ev.Action}|{ev.X.ToString(CultureInfo.InvariantCulture)}|{ev.Y.ToString(CultureInfo.InvariantCulture)}|{ev.LevelId}\n";
                }
                else
                {
                    _log.Warning("[NetNode] Malformed INTERPORTAL line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("INTERSHRINE|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["INTERSHRINE|".Length..];
                if (TryParseInterEliteShrinePayload(payload, senderId, forceSenderId, out var ev))
                {
                    lock (_sync)
                    {
                        AddBoundedLocked(_pendingInterEliteShrineEvents, ev, PendingInteractionLimit);
                        _hasRemote = true;
                    }

                    if (_role == NetRole.Host && senderId.HasValue)
                        forwardLine = $"INTERSHRINE|{ev.UserId.ToString(CultureInfo.InvariantCulture)}|{ev.X.ToString(CultureInfo.InvariantCulture)}|{ev.Y.ToString(CultureInfo.InvariantCulture)}|{ev.LevelId}\n";
                }
                else
                {
                    _log.Warning("[NetNode] Malformed INTERSHRINE line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("INTERCOLLECTOR_DEPOSIT|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["INTERCOLLECTOR_DEPOSIT|".Length..];
                if (TryParseInterCollectorDepositPayload(payload, senderId, forceSenderId, out var ev))
                {
                    lock (_sync)
                    {
                        AddBoundedLocked(_pendingInterCollectorDeposits, ev, PendingInteractionLimit);
                        _hasRemote = true;
                    }

                    if (_role == NetRole.Host && senderId.HasValue)
                        forwardLine = $"INTERCOLLECTOR_DEPOSIT|{ev.UserId.ToString(CultureInfo.InvariantCulture)}|{ev.ItemId}|{ev.CellsDeposited.ToString(CultureInfo.InvariantCulture)}|{ev.TotalInvested.ToString(CultureInfo.InvariantCulture)}|{ev.Sequence.ToString(CultureInfo.InvariantCulture)}|{ev.LevelId}\n";
                }
                else
                {
                    _log.Warning("[NetNode] Malformed INTERCOLLECTOR_DEPOSIT line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("INTERBLUEPRINT_UNLOCK|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["INTERBLUEPRINT_UNLOCK|".Length..];
                if (TryParseInterBlueprintUnlockPayload(payload, senderId, forceSenderId, out var ev))
                {
                    lock (_sync)
                    {
                        AddBoundedLocked(_pendingInterBlueprintUnlocks, ev, PendingInteractionLimit);
                        _hasRemote = true;
                    }

                    if (_role == NetRole.Host && senderId.HasValue)
                        forwardLine = $"INTERBLUEPRINT_UNLOCK|{ev.UserId.ToString(CultureInfo.InvariantCulture)}|{ev.ItemId}|{ev.Sequence.ToString(CultureInfo.InvariantCulture)}|{ev.LevelId}\n";
                }
                else
                {
                    _log.Warning("[NetNode] Malformed INTERBLUEPRINT_UNLOCK line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("INTERFORGE_UPGRADE|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["INTERFORGE_UPGRADE|".Length..];
                if (TryParseInterForgeUpgradePayload(payload, senderId, forceSenderId, out var ev))
                {
                    lock (_sync)
                    {
                        AddBoundedLocked(_pendingInterForgeUpgrades, ev, PendingInteractionLimit);
                        _hasRemote = true;
                    }

                    if (_role == NetRole.Host && senderId.HasValue)
                        forwardLine = $"INTERFORGE_UPGRADE|{ev.UserId.ToString(CultureInfo.InvariantCulture)}|{ev.UpgradeLevel.ToString(CultureInfo.InvariantCulture)}|{ev.CellsDeposited.ToString(CultureInfo.InvariantCulture)}|{ev.TotalInvested.ToString(CultureInfo.InvariantCulture)}|{ev.Sequence.ToString(CultureInfo.InvariantCulture)}|{ev.LevelId}\n";
                }
                else
                {
                    _log.Warning("[NetNode] Malformed INTERFORGE_UPGRADE line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("INTERCOLLECTOR_LOCK|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["INTERCOLLECTOR_LOCK|".Length..];
                if (TryParseInterCollectorLockPayload(payload, senderId, forceSenderId, out var ev))
                {
                    lock (_sync)
                    {
                        AddBoundedLocked(_pendingInterCollectorLocks, ev, PendingInteractionLimit);
                        _hasRemote = true;
                    }

                    if (_role == NetRole.Host && senderId.HasValue)
                        forwardLine = $"INTERCOLLECTOR_LOCK|{ev.UserId.ToString(CultureInfo.InvariantCulture)}|{(ev.InUse ? 1 : 0)}|{ev.Kind}|{ev.Sequence.ToString(CultureInfo.InvariantCulture)}|{ev.LevelId}\n";
                }
                else
                {
                    _log.Warning("[NetNode] Malformed INTERCOLLECTOR_LOCK line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("INTERSHOP_BUY|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["INTERSHOP_BUY|".Length..];
                if (TryParseInterShopPurchasePayload(payload, senderId, forceSenderId, out var ev))
                {
                    lock (_sync)
                    {
                        AddBoundedLocked(_pendingInterShopPurchaseEvents, ev, PendingInteractionLimit);
                        _hasRemote = true;
                    }
                }
                else
                {
                    _log.Warning("[NetNode] Malformed INTERSHOP_BUY line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("INTERSHOP_REJECT|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["INTERSHOP_REJECT|".Length..];
                if (TryParseInterShopRejectPayload(payload, senderId, forceSenderId, out var ev))
                {
                    lock (_sync)
                    {
                        AddBoundedLocked(_pendingInterShopRejectEvents, ev, PendingInteractionLimit);
                        _hasRemote = true;
                    }
                }
                else
                {
                    _log.Warning("[NetNode] Malformed INTERSHOP_REJECT line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("INTERREROLL|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["INTERREROLL|".Length..];
                if (TryParseInterShopRerollPayload(payload, senderId, forceSenderId, out var ev))
                {
                    lock (_sync)
                    {
                        AddBoundedLocked(_pendingInterShopRerollEvents, ev, PendingInteractionLimit);
                        _hasRemote = true;
                    }
                }
                else
                {
                    _log.Warning("[NetNode] Malformed INTERREROLL line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("INTERPEDESTAL|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["INTERPEDESTAL|".Length..];
                if (TryParseInterPedestalPickupPayload(payload, senderId, forceSenderId, out var ev))
                {
                    lock (_sync)
                    {
                        AddBoundedLocked(_pendingInterPedestalPickupEvents, ev, PendingInteractionLimit);
                        _hasRemote = true;
                    }
                }
                else
                {
                    _log.Warning("[NetNode] Malformed INTERPEDESTAL line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("INTERPEDESTAL_REJECT|", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["INTERPEDESTAL_REJECT|".Length..];
                if (TryParseInterPedestalRejectPayload(payload, senderId, forceSenderId, out var ev))
                {
                    lock (_sync)
                    {
                        AddBoundedLocked(_pendingInterPedestalRejectEvents, ev, PendingInteractionLimit);
                        _hasRemote = true;
                    }
                }
                else
                {
                    _log.Warning("[NetNode] Malformed INTERPEDESTAL_REJECT line: \"{Line}\"", ClampProtocolText(line, 64));
                }
                return true;
            }

            if (line.StartsWith("DIED", StringComparison.OrdinalIgnoreCase))
            {
                if (_role == NetRole.Host)
                {
                    var _remoteId = senderId ?? 0;
                    _log.Information("[NetNode] Remote hero died (id {Id})", _remoteId);
                    forwardLine = "DIED\n";
                }
                GameDataSync.TriggerRemoteDeath();
                return true;
            }

            if (line.StartsWith("KICK"))
            {
                return false;
            }

            if (line.StartsWith("BYE", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (TryParsePositionLine(line, senderId, out var remoteId, out var cx, out var cy, out var dir, out var hasDir, out var positionLevelId, out var positionSequence))
            {
                if (forceSenderId && senderId.HasValue)
                    remoteId = senderId.Value;
                int forwardDir = dir;
                lock (_sync)
                {
                    var state = GetOrCreateRemoteLocked(remoteId);

                    // Steam movement uses the intentionally-unreliable realtime channel, which may
                    // deliver an older packet after the player has already crossed a passage. Without
                    // an ordering fence that old coordinate could win and briefly place the remote
                    // shell inside geometry from the new world. Protocol 19 position packets carry a
                    // monotonic sender sequence; ignore any packet older than the last accepted one.
                    if (positionSequence > 0 && state.LastPositionSequence > 0 && positionSequence <= state.LastPositionSequence)
                        return true;
                    if (positionSequence > 0)
                        state.LastPositionSequence = positionSequence;

                    var prevX = state.X;
                    var hadRemote = state.HasRemote;
                    state.X = cx;
                    state.Y = cy;
                    state.HasPosition = true;
                    if (!string.IsNullOrWhiteSpace(positionLevelId))
                        state.LevelId = ClampProtocolText(positionLevelId, MaxIdentityFieldChars);
                    if (hasDir)
                    {
                        state.Dir = dir;
                    }
                    else if (hadRemote && cx != prevX)
                    {
                        state.Dir = cx < prevX ? -1 : 1;
                    }
                    state.HasRemote = true;
                    _hasRemote = true;
                    if (_primaryRemoteId == 0)
                        _primaryRemoteId = remoteId;
                    forwardDir = state.Dir;
                }
                if (_role == NetRole.Host && senderId.HasValue)
                    forwardLine = BuildPosLine(remoteId, cx, cy, forwardDir, positionLevelId, positionSequence);
                return true;
            }

            _log.Warning("[NetNode] Unrecognized or unhandled protocol line: \"{Line}\"", ClampProtocolText(line, 64));
            return true;
        }
        catch (Exception ex)
        {
            _log.Warning("[NetNode] Protocol line processing failed: {msg}", ex.Message);
            return true;
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void ApplyHostGameTime(double hostGameTimeS, bool hostStopGameTime)
    {
        try
        {
            var gameData = (ModEntry.Instance?.game ?? dc.pr.Game.Class.ME)?.data;
            if (gameData != null)
            {
                if (hostStopGameTime || System.Math.Abs(gameData.gameTimeS - hostGameTimeS) > 0.25)
                {
                    gameData.gameTimeS = hostGameTimeS;
                }
                gameData.stopGameTime = hostStopGameTime;
            }
        }
        catch (Exception ex)
        {
            _log.Warning("[NetNode] Failed to apply GTIME: {msg}", ex.Message);
        }
    }
}
