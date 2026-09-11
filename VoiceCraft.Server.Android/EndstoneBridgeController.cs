using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using VoiceCraft.Core.World;
using VoiceCraft.Network;
using VoiceCraft.Network.World;
using VcServerApp = VoiceCraft.Server.App;

namespace VoiceCraft.Server.Android;

internal sealed record BridgeDashboardPlayer(
    string Name,
    string Xuid,
    string Uuid,
    string Dimension,
    float X,
    float Y,
    float Z,
    bool Bound,
    int? EntityId);

internal sealed record BridgeDashboardSnapshot(
    int MinecraftPlayers,
    int BoundPlayers,
    int UnboundPlayers,
    IReadOnlyList<BridgeDashboardPlayer> Players)
{
    public static BridgeDashboardSnapshot Empty { get; } =
        new(0, 0, 0, Array.Empty<BridgeDashboardPlayer>());
}

internal sealed class EndstoneBridgeController : IAsyncDisposable
{
    private const string BindingAlphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const int MaxAttemptsPerRelay = 5;
    private const int PeerTimeoutSeconds = 30;
    private readonly IReadOnlyList<Uri> _relayUris;
    private readonly string _serverId;
    private readonly string _secret;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentQueue<string> _outgoing = new();
    private readonly ConcurrentDictionary<string, BridgePlayerState> _latestStates = new();
    private readonly Dictionary<string, int> _unboundByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _keyByEntity = new();
    private readonly Dictionary<string, int> _boundByPlayer = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _playerByEntity = new();
    private readonly Dictionary<int, BridgeUnbindRequest> _manualUnbindByEntity = new();
    private volatile BridgeDashboardSnapshot _dashboardSnapshot = BridgeDashboardSnapshot.Empty;
    private Task? _runTask;
    private Task? _attachTask;
    private VoiceCraftWorld? _world;
    private int _queuedMessages;
    private int _activeRelayIndex;
    private int _relayFailures;
    private DateTimeOffset? _peerMissingSince;

    public bool RelayConnected { get; private set; }
    public bool EndstoneConnected { get; private set; }
    public string LastError { get; private set; } = string.Empty;
    public BridgeDashboardSnapshot DashboardSnapshot => _dashboardSnapshot;

    public string Status => !RelayConnected
        ? "relay-disconnected"
        : EndstoneConnected ? "endstone-connected" : "relay-only";

    public int RelayCount => _relayUris.Count;
    public int ActiveRelayIndex => _activeRelayIndex;
    public string ActiveRelayName => _activeRelayIndex == 0 ? "Primary" : $"Backup #{_activeRelayIndex}";
    public string ActiveRelayEndpoint => SafeEndpoint(ActiveRelayUri);
    private Uri ActiveRelayUri => _relayUris[_activeRelayIndex];

    public EndstoneBridgeController(IEnumerable<string> relayUrls, string serverId, string secret)
    {
        var urls = new List<Uri>();
        foreach (var raw in relayUrls)
        {
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
                continue;
            if (urls.Any(existing => string.Equals(existing.AbsoluteUri, uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase)))
                continue;
            urls.Add(uri);
        }
        if (urls.Count == 0)
            throw new ArgumentException("At least one relay URL is required.", nameof(relayUrls));
        _relayUris = urls;
        _serverId = serverId;
        _secret = secret;
    }

    public void Start()
    {
        if (_runTask is not null)
            return;
        AndroidRuntimeLog.Append("BRIDGE", $"Phase 2 UI4.4 starting relays={_relayUris.Count} active={ActiveRelayName} relay={ActiveRelayEndpoint} server_id={_serverId}; secret hidden");
        _attachTask = Task.Run(() => AttachRuntimeAsync(_cts.Token));
        _runTask = Task.Run(() => RunRelayAsync(_cts.Token));
    }

    public void RequestSnapshot()
    {
        QueueOutgoing(new
        {
            type = "request_snapshot",
            reason = "android-ui4.4"
        });
        AndroidRuntimeLog.Append("BRIDGE", "Requested fresh Endstone player snapshot");
    }

    private async Task AttachRuntimeAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var provider = VcServerApp.ActiveServiceProvider;
            if (provider is null || !VcServerApp.IsRunning)
            {
                await Task.Delay(100, token);
                continue;
            }

            var attached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            VoiceCraft.Server.RuntimeDispatcher.Post(() =>
            {
                try
                {
                    if (_world is not null)
                    {
                        attached.TrySetResult();
                        return;
                    }

                    var world = provider.GetRequiredService<VoiceCraftWorld>();
                    _world = world;
                    world.OnEntityCreated += OnEntityCreated;
                    world.OnEntityDestroyed += OnEntityDestroyed;
                    foreach (var entity in world.Entities)
                        OnEntityCreated(entity);
                    AndroidRuntimeLog.Append("BRIDGE", $"Attached to VoiceCraft world; entities={world.Entities.Count}");
                    attached.TrySetResult();
                }
                catch (Exception ex)
                {
                    attached.TrySetException(ex);
                }
            });

            try
            {
                await attached.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                return;
            }
            catch (TimeoutException)
            {
                // Runtime may still be starting. Retry without failing the service.
            }
        }
    }

    private void OnEntityCreated(VoiceCraftEntity entity)
    {
        if (entity is not VoiceCraftNetworkEntity networkEntity)
            return;

        if (networkEntity.PositioningType != PositioningType.Server)
        {
            AndroidRuntimeLog.Append(
                "BRIDGE",
                $"Voice client entity={networkEntity.Id} uses {networkEntity.PositioningType} positioning; Endstone server positioning skipped");
            return;
        }

        if (_keyByEntity.ContainsKey(networkEntity.Id) || _playerByEntity.ContainsKey(networkEntity.Id))
            return;

        AssignBindingKey(networkEntity);
    }

    private void OnEntityDestroyed(VoiceCraftEntity entity)
    {
        if (entity is not VoiceCraftNetworkEntity)
            return;

        if (_keyByEntity.Remove(entity.Id, out var key))
            _unboundByKey.Remove(key);

        BridgePlayerState? disconnectedPlayer = null;
        if (_playerByEntity.Remove(entity.Id, out var playerKey))
        {
            _boundByPlayer.Remove(playerKey);
            _latestStates.TryGetValue(playerKey, out disconnectedPlayer);
        }

        if (_manualUnbindByEntity.Remove(entity.Id, out var manualRequest))
        {
            SendUnbindResult(manualRequest, true, string.Empty, entity.Id);
            AndroidRuntimeLog.Append(
                "BRIDGE",
                $"UNBIND completed player={manualRequest.Name} entity={entity.Id} request={ShortId(manualRequest.RequestId)}; auto-rebind event suppressed");
        }
        else if (disconnectedPlayer is not null)
        {
            QueueOutgoing(new
            {
                type = "voice_client_disconnected",
                xuid = disconnectedPlayer.Xuid,
                uuid = disconnectedPlayer.Uuid,
                name = disconnectedPlayer.Name,
                entityId = entity.Id
            });
            AndroidRuntimeLog.Append(
                "BRIDGE",
                $"Voice client disconnected while Minecraft player={disconnectedPlayer.Name} remains online; rebind requested entity={entity.Id}");
        }
        else
        {
            AndroidRuntimeLog.Append("BRIDGE", $"Voice client entity={entity.Id} removed from Endstone binding table");
        }

        RefreshDashboardSnapshot();
    }

    private void AssignBindingKey(VoiceCraftNetworkEntity entity)
    {
        string key;
        do
        {
            key = GenerateBindingKey(5);
        } while (_unboundByKey.ContainsKey(key));

        _unboundByKey[key] = entity.Id;
        _keyByEntity[entity.Id] = key;
        entity.Name = "New Client";
        entity.WorldId = string.Empty;
        entity.Position = Vector3.Zero;
        entity.Rotation = Vector2.Zero;
        entity.SetDescription($"Welcome! Your binding key is {key}");
        AndroidRuntimeLog.Append(
            "BRIDGE",
            $"Voice client entity={entity.Id} user={entity.UserGuid} awaiting /vcbind; key hidden from log");
    }

    private void ApplyPlayerState(BridgePlayerState state)
    {
        _latestStates[state.PlayerKey] = state;
        if (!_boundByPlayer.TryGetValue(state.PlayerKey, out var entityId))
        {
            RefreshDashboardSnapshot();
            return;
        }

        if (_world?.GetEntity(entityId) is not VoiceCraftNetworkEntity entity || entity.Destroyed)
        {
            _boundByPlayer.Remove(state.PlayerKey);
            _playerByEntity.Remove(entityId);
            RefreshDashboardSnapshot();
            return;
        }

        ApplyStateToEntity(entity, state);
        RefreshDashboardSnapshot();
    }

    private void ApplyStateToEntity(VoiceCraftNetworkEntity entity, BridgePlayerState state)
    {
        if (entity.PositioningType != PositioningType.Server)
            return;

        entity.Name = state.Name;
        entity.WorldId = NormalizeDimension(state.Dimension);
        entity.Position = new Vector3(state.X, state.Y, state.Z);
        // Minecraft getRotation() uses X=pitch and Y=yaw. VoiceCraft's Vector2
        // follows that ordering, matching the stock Basic addon.
        entity.Rotation = new Vector2(state.Pitch, state.Yaw);
    }

    private void HandleBind(BridgeBindRequest request)
    {
        var state = request.State;
        _latestStates[state.PlayerKey] = state;

        if (_boundByPlayer.ContainsKey(state.PlayerKey))
        {
            SendBindResult(request, false, "player already bound");
            return;
        }

        if (!_unboundByKey.TryGetValue(request.BindingKey, out var entityId))
        {
            SendBindResult(request, false, "binding key not found or already used");
            return;
        }

        if (_world?.GetEntity(entityId) is not VoiceCraftNetworkEntity entity || entity.Destroyed)
        {
            _unboundByKey.Remove(request.BindingKey);
            _keyByEntity.Remove(entityId);
            SendBindResult(request, false, "voice client entity no longer exists");
            return;
        }

        if (entity.PositioningType != PositioningType.Server)
        {
            SendBindResult(request, false, "voice client must use Server positioning mode");
            return;
        }

        _unboundByKey.Remove(request.BindingKey);
        _keyByEntity.Remove(entityId);
        _boundByPlayer[state.PlayerKey] = entityId;
        _playerByEntity[entityId] = state.PlayerKey;
        entity.SetDescription($"Bound to player {state.Name}");
        ApplyStateToEntity(entity, state);

        AndroidRuntimeLog.Append(
            "BRIDGE",
            $"BIND success player={state.Name} xuid={state.Xuid} entity={entityId} request={ShortId(request.RequestId)}");
        RefreshDashboardSnapshot();
        SendBindResult(request, true, string.Empty, entityId);
    }

    private void HandleManualUnbind(BridgeUnbindRequest request)
    {
        if (!_boundByPlayer.TryGetValue(request.PlayerKey, out var currentEntityId))
        {
            SendUnbindResult(request, false, "player is not currently bound");
            return;
        }
        if (currentEntityId != request.EntityId)
        {
            SendUnbindResult(request, false, $"stale entity id (current={currentEntityId})", currentEntityId);
            AndroidRuntimeLog.Append(
                "BRIDGE",
                $"UNBIND stale request rejected player={request.Name} requested_entity={request.EntityId} current_entity={currentEntityId} request={ShortId(request.RequestId)}");
            return;
        }
        if (_world?.GetEntity(currentEntityId) is not VoiceCraftNetworkEntity entity || entity.Destroyed)
        {
            SendUnbindResult(request, false, "voice client entity no longer exists", currentEntityId);
            return;
        }
        var server = entity.NetPeer.Server;
        if (server is null)
        {
            SendUnbindResult(request, false, "voice client server is unavailable", currentEntityId);
            return;
        }

        _manualUnbindByEntity[currentEntityId] = request;
        AndroidRuntimeLog.Append(
            "BRIDGE",
            $"UNBIND accepted player={request.Name} entity={currentEntityId} request={ShortId(request.RequestId)}; disconnecting VoiceCraft peer");
        try
        {
            server.Disconnect(entity.NetPeer, "VoiceCraft.DisconnectReason.Kicked");
        }
        catch (Exception ex)
        {
            _manualUnbindByEntity.Remove(currentEntityId);
            SendUnbindResult(request, false, $"{ex.GetType().Name}: {ex.Message}", currentEntityId);
        }
    }

    private void SendUnbindResult(BridgeUnbindRequest request, bool success, string reason, int? entityId = null)
    {
        QueueOutgoing(new
        {
            type = "unbind_result",
            requestId = request.RequestId,
            xuid = request.Xuid,
            uuid = request.Uuid,
            name = request.Name,
            success,
            reason,
            entityId
        });
    }

    private void HandlePlayerLeave(string playerKey, string name)
    {
        _latestStates.TryRemove(playerKey, out _);
        if (!_boundByPlayer.Remove(playerKey, out var entityId))
        {
            RefreshDashboardSnapshot();
            return;
        }

        _playerByEntity.Remove(entityId);
        if (_world?.GetEntity(entityId) is not VoiceCraftNetworkEntity entity || entity.Destroyed)
        {
            RefreshDashboardSnapshot();
            return;
        }

        entity.Name = "New Client";
        entity.WorldId = string.Empty;
        entity.Position = Vector3.Zero;
        entity.Rotation = Vector2.Zero;
        AssignBindingKey(entity);
        AndroidRuntimeLog.Append("BRIDGE", $"UNBIND player={name} entity={entityId}; new key assigned");
        RefreshDashboardSnapshot();
    }

    private void SendBindResult(BridgeBindRequest request, bool success, string reason, int? entityId = null)
    {
        QueueOutgoing(new
        {
            type = "bind_result",
            requestId = request.RequestId,
            xuid = request.State.Xuid,
            uuid = request.State.Uuid,
            success,
            reason,
            entityId
        });
        if (!success)
        {
            AndroidRuntimeLog.Append(
                "BRIDGE",
                $"BIND rejected player={request.State.Name} xuid={request.State.Xuid} request={ShortId(request.RequestId)} reason={reason}");
        }
    }

    private async Task RunRelayAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var relayUri = ActiveRelayUri;
            var relayName = ActiveRelayName;
            try
            {
                if (_relayFailures > 0)
                    AndroidRuntimeLog.Append("BRIDGE", $"{relayName} attempt {_relayFailures + 1}/{MaxAttemptsPerRelay} relay={SafeEndpoint(relayUri)}");

                using var ws = new ClientWebSocket();
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                await ws.ConnectAsync(relayUri, token);

                await SendDirectAsync(ws, new
                {
                    type = "hello",
                    role = "android",
                    serverId = _serverId,
                    secret = _secret,
                    protocol = 1,
                    appVersion = "1.7.1-android-phase2-ui4.5"
                }, token);

                var helloText = await ReceiveTextAsync(ws, token);
                if (helloText is null)
                    throw new IOException("relay closed before hello_ok");
                using (var hello = JsonDocument.Parse(helloText))
                {
                    if (GetString(hello.RootElement, "type") != "hello_ok")
                        throw new IOException("relay rejected hello");
                }

                RelayConnected = true;
                EndstoneConnected = false;
                _peerMissingSince = DateTimeOffset.UtcNow;
                LastError = string.Empty;
                AndroidRuntimeLog.Append("BRIDGE", $"Relay connected via {relayName} relay={SafeEndpoint(relayUri)} server_id={_serverId}");
                QueueOutgoing(new
                {
                    type = "server_status",
                    status = "running",
                    voiceClients = VcServerApp.ConnectedClients,
                    bridgeVersion = "0.2.6",
                    activeRelay = relayName,
                    relayIndex = _activeRelayIndex,
                    relayCount = _relayUris.Count
                });

                using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                var sender = SenderLoopAsync(ws, connectionCts.Token);
                var receiver = ReceiverLoopAsync(ws, connectionCts.Token);
                var watchdog = PeerWatchdogAsync(connectionCts.Token);
                var completed = await Task.WhenAny(sender, receiver, watchdog);
                if (completed == watchdog)
                    await watchdog;
                connectionCts.Cancel();
                await Task.WhenAll(IgnoreCancellation(sender), IgnoreCancellation(receiver), IgnoreCancellation(watchdog));

                if (!token.IsCancellationRequested)
                    throw new IOException("relay websocket closed");
            }
            catch (System.OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                RegisterRelayFailure(relayName, relayUri, ex);
            }
            finally
            {
                RelayConnected = false;
                EndstoneConnected = false;
                _peerMissingSince = null;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);
            }
            catch (System.OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PeerWatchdogAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), token);
            if (EndstoneConnected)
            {
                _relayFailures = 0;
                _peerMissingSince = null;
                continue;
            }

            _peerMissingSince ??= DateTimeOffset.UtcNow;
            if (DateTimeOffset.UtcNow - _peerMissingSince >= TimeSpan.FromSeconds(PeerTimeoutSeconds))
                throw new IOException($"Endstone peer not visible on {ActiveRelayName} for {PeerTimeoutSeconds}s");
        }
    }

    private void RegisterRelayFailure(string relayName, Uri relayUri, Exception ex)
    {
        _relayFailures++;
        LastError = $"{ex.GetType().Name}: {ex.Message}";
        AndroidRuntimeLog.Append(
            "BRIDGE",
            $"{relayName} failed {_relayFailures}/{MaxAttemptsPerRelay}: {LastError}; retrying in 5s");

        if (_relayFailures < MaxAttemptsPerRelay)
            return;

        if (_relayUris.Count == 1)
        {
            AndroidRuntimeLog.Append("BRIDGE", "Primary retry cycle exhausted; no Backup Relay configured, continuing Primary");
            _relayFailures = 0;
            return;
        }

        var previous = relayName;
        _activeRelayIndex = (_activeRelayIndex + 1) % _relayUris.Count;
        _relayFailures = 0;
        AndroidRuntimeLog.Append(
            "BRIDGE",
            $"FAILOVER {previous} -> {ActiveRelayName} relay={ActiveRelayEndpoint}; VoiceCraft UDP runtime remains active");
    }

    private async Task SenderLoopAsync(ClientWebSocket ws, CancellationToken token)
    {
        while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            var sent = false;
            for (var i = 0; i < 64 && _outgoing.TryDequeue(out var text); i++)
            {
                Interlocked.Decrement(ref _queuedMessages);
                var bytes = Encoding.UTF8.GetBytes(text);
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
                sent = true;
            }

            if (!sent)
                await Task.Delay(25, token);
        }
    }

    private async Task ReceiverLoopAsync(ClientWebSocket ws, CancellationToken token)
    {
        while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            var text = await ReceiveTextAsync(ws, token);
            if (text is null)
                return;

            try
            {
                using var doc = JsonDocument.Parse(text);
                HandleRelayMessage(doc.RootElement);
            }
            catch (JsonException ex)
            {
                AndroidRuntimeLog.Append("BRIDGE", $"Ignored invalid relay JSON: {ex.Message}");
            }
        }
    }

    private void HandleRelayMessage(JsonElement root)
    {
        var type = GetString(root, "type");
        switch (type)
        {
            case "peer_status":
            {
                var connected = GetBool(root, "endstoneConnected");
                if (connected != EndstoneConnected)
                {
                    EndstoneConnected = connected;
                    if (connected)
                    {
                        _relayFailures = 0;
                        _peerMissingSince = null;
                    }
                    else
                    {
                        _peerMissingSince ??= DateTimeOffset.UtcNow;
                    }
                    AndroidRuntimeLog.Append("BRIDGE", $"Endstone peer {(connected ? "connected" : "disconnected")} via {ActiveRelayName}");
                }
                break;
            }
            case "player_state":
            {
                if (!BridgePlayerState.TryParse(root, out var state))
                    return;
                _latestStates[state.PlayerKey] = state;
                VoiceCraft.Server.RuntimeDispatcher.Post(() => ApplyPlayerState(state));
                break;
            }
            case "player_leave":
            {
                var xuid = GetString(root, "xuid");
                var uuid = GetString(root, "uuid");
                var playerKey = !string.IsNullOrWhiteSpace(xuid) ? xuid : uuid;
                var name = GetString(root, "name");
                if (string.IsNullOrWhiteSpace(playerKey))
                    return;
                _latestStates.TryRemove(playerKey, out _);
                VoiceCraft.Server.RuntimeDispatcher.Post(() => HandlePlayerLeave(playerKey, name));
                break;
            }
            case "bind":
            {
                if (!BridgeBindRequest.TryParse(root, out var request))
                    return;
                VoiceCraft.Server.RuntimeDispatcher.Post(() => HandleBind(request));
                break;
            }
            case "unbind":
            {
                if (!BridgeUnbindRequest.TryParse(root, out var request))
                    return;
                VoiceCraft.Server.RuntimeDispatcher.Post(() => HandleManualUnbind(request));
                break;
            }
            case "sync_end":
                AndroidRuntimeLog.Append("BRIDGE", $"Endstone state sync received cached_players={_latestStates.Count}");
                break;
        }
    }

    private void RefreshDashboardSnapshot()
    {
        var players = _latestStates.Values
            .OrderBy(state => state.Name, StringComparer.OrdinalIgnoreCase)
            .Select(state =>
            {
                var bound = _boundByPlayer.TryGetValue(state.PlayerKey, out var entityId);
                return new BridgeDashboardPlayer(
                    state.Name,
                    state.Xuid,
                    state.Uuid,
                    state.Dimension,
                    state.X,
                    state.Y,
                    state.Z,
                    bound,
                    bound ? entityId : null);
            })
            .ToArray();
        var boundPlayers = players.Count(player => player.Bound);
        _dashboardSnapshot = new BridgeDashboardSnapshot(
            players.Length,
            boundPlayers,
            players.Length - boundPlayers,
            players);
    }

    private void QueueOutgoing(object payload)
    {
        var text = JsonSerializer.Serialize(payload);
        _outgoing.Enqueue(text);
        var count = Interlocked.Increment(ref _queuedMessages);
        while (count > 1024 && _outgoing.TryDequeue(out _))
        {
            count = Interlocked.Decrement(ref _queuedMessages);
        }
    }

    private static async Task SendDirectAsync(ClientWebSocket ws, object payload, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
    }

    private static async Task<string?> ReceiveTextAsync(ClientWebSocket ws, CancellationToken token)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            if (result.MessageType != WebSocketMessageType.Text)
                continue;
            stream.Write(buffer, 0, result.Count);
            if (stream.Length > 65536)
                throw new IOException("relay message exceeded 64 KiB");
            if (!result.EndOfMessage)
                continue;
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    private static async Task IgnoreCancellation(Task task)
    {
        try
        {
            await task;
        }
        catch (System.OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    private static string GenerateBindingKey(int length)
    {
        Span<char> chars = stackalloc char[length];
        for (var i = 0; i < length; i++)
            chars[i] = BindingAlphabet[RandomNumberGenerator.GetInt32(BindingAlphabet.Length)];
        return new string(chars);
    }

    private static string NormalizeDimension(string value)
    {
        var key = value.Trim().ToLowerInvariant().Replace(" ", string.Empty).Replace("_", string.Empty);
        return key switch
        {
            "overworld" or "minecraft:overworld" => "minecraft:overworld",
            "nether" or "thenether" or "minecraft:nether" => "minecraft:nether",
            "theend" or "end" or "minecraft:theend" => "minecraft:the_end",
            _ => value.Trim()
        };
    }

    private static string SafeEndpoint(Uri uri) => uri.GetLeftPart(UriPartial.Path);
    private static string ShortId(string value) => value.Length <= 8 ? value : value[..8];

    private static string GetString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static bool GetBool(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            && value.GetBoolean();
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_world is not null)
        {
            _world.OnEntityCreated -= OnEntityCreated;
            _world.OnEntityDestroyed -= OnEntityDestroyed;
            _world = null;
        }

        if (_runTask is not null)
            await IgnoreCancellation(_runTask);
        if (_attachTask is not null)
            await IgnoreCancellation(_attachTask);
        _cts.Dispose();
        AndroidRuntimeLog.Append("BRIDGE", "Phase 2 UI4.4 multi-relay controller stopped");
    }

    private sealed record BridgePlayerState(
        string Name,
        string Xuid,
        string Uuid,
        string Dimension,
        float X,
        float Y,
        float Z,
        float Yaw,
        float Pitch)
    {
        public string PlayerKey => !string.IsNullOrWhiteSpace(Xuid) ? Xuid : Uuid;

        public static bool TryParse(JsonElement root, out BridgePlayerState state)
        {
            state = null!;
            var name = GetString(root, "name");
            var xuid = GetString(root, "xuid");
            var uuid = GetString(root, "uuid");
            var dimension = GetString(root, "dimension");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dimension) ||
                (string.IsNullOrWhiteSpace(xuid) && string.IsNullOrWhiteSpace(uuid)))
                return false;

            if (!TryGetSingle(root, "x", out var x) || !TryGetSingle(root, "y", out var y) ||
                !TryGetSingle(root, "z", out var z) || !TryGetSingle(root, "yaw", out var yaw) ||
                !TryGetSingle(root, "pitch", out var pitch))
                return false;

            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) ||
                !float.IsFinite(yaw) || !float.IsFinite(pitch) || y is < -4096f or > 4096f)
                return false;

            state = new BridgePlayerState(name, xuid, uuid, dimension, x, y, z, yaw, pitch);
            return true;
        }

        private static bool TryGetSingle(JsonElement root, string name, out float value)
        {
            value = 0;
            return root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number
                && element.TryGetSingle(out value);
        }
    }

    private sealed record BridgeUnbindRequest(
        string RequestId,
        string Name,
        string Xuid,
        string Uuid,
        int EntityId)
    {
        public string PlayerKey => !string.IsNullOrWhiteSpace(Xuid) ? Xuid : Uuid;

        public static bool TryParse(JsonElement root, out BridgeUnbindRequest request)
        {
            request = null!;
            var requestId = GetString(root, "requestId");
            var name = GetString(root, "name");
            var xuid = GetString(root, "xuid");
            var uuid = GetString(root, "uuid");
            if (string.IsNullOrWhiteSpace(requestId) ||
                (string.IsNullOrWhiteSpace(xuid) && string.IsNullOrWhiteSpace(uuid)) ||
                !root.TryGetProperty("entityId", out var entityElement) ||
                entityElement.ValueKind != JsonValueKind.Number ||
                !entityElement.TryGetInt32(out var entityId))
                return false;
            request = new BridgeUnbindRequest(requestId, name, xuid, uuid, entityId);
            return true;
        }
    }

    private sealed record BridgeBindRequest(string RequestId, string BindingKey, BridgePlayerState State)
    {
        public static bool TryParse(JsonElement root, out BridgeBindRequest request)
        {
            request = null!;
            var requestId = GetString(root, "requestId");
            var bindingKey = GetString(root, "bindingKey");
            if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(bindingKey) ||
                !BridgePlayerState.TryParse(root, out var state))
                return false;
            request = new BridgeBindRequest(requestId, bindingKey, state);
            return true;
        }
    }
}
