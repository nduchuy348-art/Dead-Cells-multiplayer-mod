using System.Buffers;
using System.Net.Sockets;
using Steamworks;
using DeadCellsMultiplayerMod.Network;

public sealed partial class NetNode
{
    private ClientConnection[] _cachedTcpClientsSnapshot = Array.Empty<ClientConnection>();
    private SteamClientConnection[] _cachedSteamClientsSnapshot = Array.Empty<SteamClientConnection>();
    private void CloseClientConnection()
    {
        try { _stream?.Close(); } catch { }
        try { _client?.Close(); } catch { }
        _stream = null;
        _client = null;
    }

    private static bool IsRealtimeSteamLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var trimmed = line.TrimStart();
        if (trimmed.Length == 0)
            return false;

        if (IsPositionLine(trimmed))
            return true;

        return trimmed.StartsWith("ANIM|", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("HEADANIM|", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("HP|", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("MOBSTATE|", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("MOBSTATE2|", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("MOBMOVE|", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("MOBDRAW|", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("INTERELEVSTATE|", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDroppableTcpRealtimeLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var trimmed = line.TrimStart();
        if (trimmed.Length == 0)
            return false;

        if (IsPositionLine(trimmed))
            return true;

        // A keep-alive whose predecessor is still in flight carries no information: the write that
        // is already queued proves the same thing. Queuing another behind a wedged socket would
        // just accumulate tasks on the send semaphore.
        if (trimmed.StartsWith("PING", StringComparison.OrdinalIgnoreCase))
            return true;

        // Only packets that are superseded by a newer frame may be dropped when TCP is
        // congested. Full MOBSTATE bootstrap/resync chunks, HP, hits, deaths and control
        // messages must remain reliable and ordered.
        return trimmed.StartsWith("ANIM|", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("HEADANIM|", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("MOBMOVE|", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("MOBDRAW|", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("INTERELEVSTATE|", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPositionLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var separatorIndex = line.IndexOf('|');
        if (separatorIndex <= 0)
            return false;

        for (var i = 0; i < separatorIndex; i++)
        {
            if (!char.IsDigit(line[i]))
                return false;
        }

        return true;
    }

    private static EP2PSend ResolveSteamSendType(string line)
    {
        // Full mob tables are split into multiple chunks and form the authoritative bootstrap for
        // a level. Sending those chunks unreliably can leave the client permanently missing a
        // subset of enemies even though ordinary movement packets continue to arrive.
        if (line.StartsWith("MOBEVENT|", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("MOBSTATE|", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("MOBSTATE2|", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("MOBREG|", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("MOBDIE|", StringComparison.OrdinalIgnoreCase))
        {
            return EP2PSend.k_EP2PSendReliable;
        }

        return IsRealtimeSteamLine(line)
            ? EP2PSend.k_EP2PSendUnreliable
            : EP2PSend.k_EP2PSendReliable;
    }

    private int GetSteamOutgoingChannel()
    {
        return _role == NetRole.Host
            ? SteamP2PChannelHostToClient
            : SteamP2PChannelClientToHost;
    }

    private bool HasAnyConnection()
    {
        if (_role == NetRole.Host)
        {
            lock (_clientsLock)
            {
                return _useSteamTransport ? _steamClients.Count > 0 : _clients.Count > 0;
            }
        }
        if (_useSteamTransport)
        {
            lock (_sync) return _hasRemote;
        }

        return _stream != null && _client != null && _client.Connected;
    }

    /// <summary>Sends a pre-encoded mob protocol line. Line must start with MOB.</summary>
    public Task SendMobWireLine(string line)
    {
        if (string.IsNullOrEmpty(line))
            return Task.CompletedTask;

        if (!line.StartsWith("MOB", StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;

        if (_role != NetRole.Host && _role != NetRole.Client)
            return Task.CompletedTask;

        if (!HasAnyConnection())
            return Task.CompletedTask;

        return SendLineSafe(line);
    }

    private Task SendLineSafe(string line)
    {
        var transport = ReadTransportSnapshot();
        if (!transport.HasConnection)
            return Task.CompletedTask;

        if (IsDroppableTcpRealtimeLine(line) && !_realtimePacketBudget.TryConsume(line.Length))
        {
            NetTrafficDiagnostics.RecordBudgetDrop();
            return Task.CompletedTask;
        }

        NetTrafficDiagnostics.TryFlush(_log, _role.ToString());

        if (transport.Role == NetRole.Host)
            return BroadcastLineSafe(line);

        if (transport.Kind == DeadCellsMultiplayerMod.Network.NetTransportKind.Steam && _steamBridge != null)
            return SendLineToSteamBridgeSafe(_steamHostId.m_SteamID, line, ResolveSteamSendType(line), GetSteamOutgoingChannel());

        return SendLineToStreamSafe(_stream, _sendLock, line);
    }

    private ClientConnection[] GetTcpClientsSnapshotLocked()
    {
        var count = _clients.Count;
        if (count == 0)
        {
            if (_cachedTcpClientsSnapshot.Length != 0)
                _cachedTcpClientsSnapshot = Array.Empty<ClientConnection>();
            return _cachedTcpClientsSnapshot;
        }

        if (_cachedTcpClientsSnapshot.Length == count)
        {
            var match = true;
            for (var i = 0; i < count; i++)
            {
                var item = _cachedTcpClientsSnapshot[i];
                if (!_clients.TryGetValue(item.AssignedId, out var current) || !ReferenceEquals(item, current))
                {
                    match = false;
                    break;
                }
            }
            if (match)
                return _cachedTcpClientsSnapshot;
        }

        var snapshot = new ClientConnection[count];
        var index = 0;
        foreach (var client in _clients.Values)
        {
            snapshot[index++] = client;
        }
        _cachedTcpClientsSnapshot = snapshot;
        return snapshot;
    }

    private SteamClientConnection[] GetSteamClientsSnapshotLocked()
    {
        var count = _steamClients.Count;
        if (count == 0)
        {
            if (_cachedSteamClientsSnapshot.Length != 0)
                _cachedSteamClientsSnapshot = Array.Empty<SteamClientConnection>();
            return _cachedSteamClientsSnapshot;
        }

        if (_cachedSteamClientsSnapshot.Length == count)
        {
            var match = true;
            for (var i = 0; i < count; i++)
            {
                var item = _cachedSteamClientsSnapshot[i];
                if (!_steamClients.TryGetValue(item.AssignedId, out var current) || !ReferenceEquals(item, current))
                {
                    match = false;
                    break;
                }
            }
            if (match)
                return _cachedSteamClientsSnapshot;
        }

        var snapshot = new SteamClientConnection[count];
        var index = 0;
        foreach (var client in _steamClients.Values)
        {
            snapshot[index++] = client;
        }
        _cachedSteamClientsSnapshot = snapshot;
        return snapshot;
    }

    private Task BroadcastLineSafe(string line)
    {
        if (string.IsNullOrEmpty(line))
            return Task.CompletedTask;

        if (_useSteamTransport && _steamBridge != null)
        {
            SteamClientConnection[] steamSnapshot;
            lock (_clientsLock)
            {
                steamSnapshot = GetSteamClientsSnapshotLocked();
            }
            if (steamSnapshot.Length == 0) return Task.CompletedTask;
            var sendType = ResolveSteamSendType(line);
            var channel = SteamP2PChannelHostToClient;
            var encoded = ProtocolWire.Encode(line, MaxProtocolLineChars);
            if (!encoded.Success)
            {
                _log.Warning("[NetNode] Rejected Steam broadcast: {Error}", encoded.Error);
                return Task.CompletedTask;
            }
            var bytes = encoded.Bytes;
            NetTrafficDiagnostics.RecordSent(bytes.Length);
            for (var i = 0; i < steamSnapshot.Length; i++)
            {
                _steamBridge.TrySend(steamSnapshot[i].SteamId.m_SteamID, sendType, channel, bytes, out _);
            }
            return Task.CompletedTask;
        }

        ClientConnection[] snapshot;
        lock (_clientsLock)
        {
            snapshot = GetTcpClientsSnapshotLocked();
        }

        var count = snapshot.Length;
        if (count == 0)
            return Task.CompletedTask;

        var encodedTcp = ProtocolWire.Encode(line, MaxProtocolLineChars);
        if (!encodedTcp.Success)
        {
            _log.Warning("[NetNode] Rejected TCP protocol line: {Error}", encodedTcp.Error);
            return Task.CompletedTask;
        }

        var tcpBytes = encodedTcp.Bytes;
        var realtime = IsDroppableTcpRealtimeLine(line);

        if (count == 1)
            return SendBytesToClientSafe(snapshot[0], tcpBytes, realtime);

        return BroadcastTcpConcurrent(snapshot, tcpBytes, realtime);
    }

    private Task BroadcastTcpConcurrent(ClientConnection[] snapshot, ReadOnlyMemory<byte> bytes, bool realtime)
    {
        var count = snapshot.Length;
        if (count == 2)
        {
            var t0 = SendBytesToClientSafe(snapshot[0], bytes, realtime);
            var t1 = SendBytesToClientSafe(snapshot[1], bytes, realtime);
            var c0 = t0.IsCompletedSuccessfully;
            var c1 = t1.IsCompletedSuccessfully;
            if (c0 && c1) return Task.CompletedTask;
            if (c0) return t1;
            if (c1) return t0;
            return AwaitTwoAsync(t0, t1);
        }

        if (count == 3)
        {
            var t0 = SendBytesToClientSafe(snapshot[0], bytes, realtime);
            var t1 = SendBytesToClientSafe(snapshot[1], bytes, realtime);
            var t2 = SendBytesToClientSafe(snapshot[2], bytes, realtime);
            var c0 = t0.IsCompletedSuccessfully;
            var c1 = t1.IsCompletedSuccessfully;
            var c2 = t2.IsCompletedSuccessfully;

            if (c0 && c1 && c2) return Task.CompletedTask;
            if (c0 && c1) return t2;
            if (c0 && c2) return t1;
            if (c1 && c2) return t0;
            if (c0) return AwaitTwoAsync(t1, t2);
            if (c1) return AwaitTwoAsync(t0, t2);
            if (c2) return AwaitTwoAsync(t0, t1);
            return AwaitThreeAsync(t0, t1, t2);
        }

        if (count == 4)
        {
            var t0 = SendBytesToClientSafe(snapshot[0], bytes, realtime);
            var t1 = SendBytesToClientSafe(snapshot[1], bytes, realtime);
            var t2 = SendBytesToClientSafe(snapshot[2], bytes, realtime);
            var t3 = SendBytesToClientSafe(snapshot[3], bytes, realtime);
            var c0 = t0.IsCompletedSuccessfully;
            var c1 = t1.IsCompletedSuccessfully;
            var c2 = t2.IsCompletedSuccessfully;
            var c3 = t3.IsCompletedSuccessfully;

            var pendingCount = (c0 ? 0 : 1) + (c1 ? 0 : 1) + (c2 ? 0 : 1) + (c3 ? 0 : 1);
            if (pendingCount == 0) return Task.CompletedTask;
            if (pendingCount == 1)
            {
                if (!c0) return t0;
                if (!c1) return t1;
                if (!c2) return t2;
                return t3;
            }
            if (pendingCount == 2)
            {
                Task? p0 = null, p1 = null;
                if (!c0) p0 = t0;
                if (!c1) { if (p0 == null) p0 = t1; else p1 = t1; }
                if (!c2) { if (p0 == null) p0 = t2; else p1 = t2; }
                if (!c3) { p1 = t3; }
                return AwaitTwoAsync(p0!, p1!);
            }
            if (pendingCount == 3)
            {
                Task? p0 = null, p1 = null, p2 = null;
                if (!c0) p0 = t0;
                if (!c1) { if (p0 == null) p0 = t1; else p1 = t1; }
                if (!c2) { if (p0 == null) p0 = t2; else if (p1 == null) p1 = t2; else p2 = t2; }
                if (!c3) { if (p1 == null) p1 = t3; else p2 = t3; }
                return AwaitThreeAsync(p0!, p1!, p2!);
            }
            return AwaitFourAsync(t0, t1, t2, t3);
        }

        var tasks = ArrayPool<Task>.Shared.Rent(count);
        var pending = 0;
        Task? singlePending = null;
        for (var i = 0; i < count; i++)
        {
            var t = SendBytesToClientSafe(snapshot[i], bytes, realtime);
            tasks[i] = t;
            if (!t.IsCompletedSuccessfully)
            {
                pending++;
                singlePending = t;
            }
        }

        if (pending == 0)
        {
            Array.Clear(tasks, 0, count);
            ArrayPool<Task>.Shared.Return(tasks);
            return Task.CompletedTask;
        }

        if (pending == 1)
        {
            Array.Clear(tasks, 0, count);
            ArrayPool<Task>.Shared.Return(tasks);
            return singlePending!;
        }

        return AwaitManyAsync(tasks, count);
    }

    private static async Task AwaitTwoAsync(Task t0, Task t1)
    {
        try
        {
            await t0.ConfigureAwait(false);
        }
        finally
        {
            await t1.ConfigureAwait(false);
        }
    }

    private static async Task AwaitThreeAsync(Task t0, Task t1, Task t2)
    {
        try
        {
            await t0.ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await t1.ConfigureAwait(false);
            }
            finally
            {
                await t2.ConfigureAwait(false);
            }
        }
    }

    private static async Task AwaitFourAsync(Task t0, Task t1, Task t2, Task t3)
    {
        try
        {
            await t0.ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await t1.ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    await t2.ConfigureAwait(false);
                }
                finally
                {
                    await t3.ConfigureAwait(false);
                }
            }
        }
    }

    private static async Task AwaitManyAsync(Task[] tasks, int count)
    {
        try
        {
            for (var i = 0; i < count; i++)
            {
                var t = tasks[i];
                if (!t.IsCompletedSuccessfully)
                {
                    try
                    {
                        await t.ConfigureAwait(false);
                    }
                    catch
                    {
                        // Defensive: ensure all remaining tasks in the rented array are awaited
                        // before the array is returned to ArrayPool in finally block.
                    }
                }
            }
        }
        finally
        {
            Array.Clear(tasks, 0, count);
            ArrayPool<Task>.Shared.Return(tasks);
        }
    }

    private async Task SendKnownUsersToSteamClientSafe(SteamClientConnection connection)
    {
        List<RemotePlayerState> snapshot;
        lock (_sync)
        {
            if (_remotes.Count == 0)
                return;
            snapshot = new List<RemotePlayerState>(_remotes.Values);
        }

        foreach (var state in snapshot)
        {
            var username = state.Username;
            if (string.IsNullOrWhiteSpace(username))
                continue;
            var line = BuildTaggedLine("USER", state.Id, username);
            await SendLineToSteamClientSafe(connection, line).ConfigureAwait(false);
            await SendLineToSteamClientSafe(connection, BuildReadyLine(state.Id, state.Ready)).ConfigureAwait(false);
            await SendLineToSteamClientSafe(connection, BuildCoopStateLine(state.Id, state.CoopId, state.HasContinueSave)).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(state.Skin))
            {
                var skinLine = BuildTaggedLine("SKIN", state.Id, state.Skin);
                await SendLineToSteamClientSafe(connection, skinLine).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(state.Head))
            {
                var headLine = BuildTaggedLine("HEAD", state.Id, state.Head);
                await SendLineToSteamClientSafe(connection, headLine).ConfigureAwait(false);
            }
        }
    }

    private Task SendLineToStreamSafe(NetworkStream? stream, SemaphoreSlim? sendLock, string line)
    {
        if (stream == null || sendLock == null || _disposed || string.IsNullOrEmpty(line))
            return Task.CompletedTask;

        var encoded = ProtocolWire.Encode(line, MaxProtocolLineChars);
        if (!encoded.Success)
        {
            _log.Warning("[NetNode] Rejected TCP protocol line: {Error}", encoded.Error);
            return Task.CompletedTask;
        }

        return SendBytesToStreamSafe(stream, sendLock, encoded.Bytes, IsDroppableTcpRealtimeLine(line));
    }

    private Task SendBytesToClientSafe(ClientConnection client, ReadOnlyMemory<byte> bytes, bool realtime)
    {
        return SendBytesToStreamSafe(client.Stream, client.SendLock, bytes, realtime);
    }

    private async Task SendBytesToStreamSafe(NetworkStream? stream, SemaphoreSlim? sendLock, ReadOnlyMemory<byte> bytes, bool realtime)
    {
        if (stream == null || sendLock == null || _disposed || bytes.IsEmpty)
            return;

        NetTrafficDiagnostics.RecordSent(bytes.Length);

        var token = _cts?.Token ?? CancellationToken.None;
        var locked = false;
        try
        {
            // Position and visual animation packets become obsolete immediately. If a previous TCP
            // write is still in flight, drop only those stale visual packets instead of creating an
            // unbounded chain of Tasks waiting on the send semaphore. Full mob snapshots, HP,
            // control, death and hit lines remain reliable and ordered.
            if (realtime)
            {
                locked = await sendLock.WaitAsync(0).ConfigureAwait(false);
                if (!locked)
                    return;
            }
            else
            {
                await sendLock.WaitAsync(token).ConfigureAwait(false);
                locked = true;
            }

            await stream.WriteAsync(bytes, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            NetTrafficDiagnostics.RecordSendError();
            _log.Warning("[NetNode] send error: {msg}", ex.Message);
        }
        finally
        {
            if (locked)
            {
                try { sendLock.Release(); } catch (ObjectDisposedException) { }
            }
        }
    }

    private Task SendLineToSteamClientSafe(SteamClientConnection client, string line, EP2PSend? sendType = null)
    {
        if (_steamBridge == null)
            return Task.CompletedTask;
        var encoded = ProtocolWire.Encode(line, MaxProtocolLineChars);
        if (!encoded.Success)
        {
            _log.Warning("[NetNode] Rejected Steam payload for {SteamId}: {Error}", client.SteamId.m_SteamID, encoded.Error);
            return Task.CompletedTask;
        }
        var bytes = encoded.Bytes;
        NetTrafficDiagnostics.RecordSent(bytes.Length);
        var st = sendType ?? ResolveSteamSendType(line);
        if (!_steamBridge.TrySend(client.SteamId.m_SteamID, st, SteamP2PChannelHostToClient, bytes, out var err))
            _log.Warning("[NetNode] Steam send failed to {SteamId}: {Error}", client.SteamId.m_SteamID, err);
        return Task.CompletedTask;
    }

    private Task SendLineToSteamBridgeSafe(ulong steamId, string line, EP2PSend sendType, int channel)
    {
        if (_steamBridge == null || steamId == 0UL)
            return Task.CompletedTask;

        if (!ProtocolWire.TryEncode(line, (int)Math.Min((uint)MaxProtocolLineChars, SteamMaxPacketSizeBytes), out var bytes))
        {
            _log.Warning(
                "[NetNode] Steam payload too large for {SteamId} (protocol limit {Limit} bytes)",
                steamId,
                MaxProtocolLineChars);
            return Task.CompletedTask;
        }
        NetTrafficDiagnostics.RecordSent(bytes.Length);

        if (!_steamBridge.TrySend(steamId, sendType, channel, bytes, out var err))
        {
            var ctx = line.StartsWith("HELLO", StringComparison.Ordinal) ? " HELLO" : string.Empty;
            _log.Warning("[NetNode] Steam send failed to {SteamId}: {Error}{Context}", steamId, err, ctx);
        }
        return Task.CompletedTask;
    }
}
