from __future__ import annotations

from pathlib import Path


def patch(path: str, old: str, new: str, count: int = 1) -> None:
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    found = text.count(old)
    if found < count:
        raise RuntimeError(f"{path}: expected at least {count} occurrence(s), found {found}: {old[:100]!r}")
    text = text.replace(old, new, count)
    p.write_text(text, encoding="utf-8")


def replace_all(path: str, old: str, new: str) -> None:
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    if old not in text:
        raise RuntimeError(f"{path}: missing {old!r}")
    p.write_text(text.replace(old, new), encoding="utf-8")


# ---------------------------------------------------------------------------
# Android: BackupRelayActivity compile fixes
# ---------------------------------------------------------------------------
patch(
    "VoiceCraft.Server.Android/BackupRelayActivity.cs",
    "using Android.Graphics;\nusing Android.OS;",
    "using Android.Graphics;\nusing Android.Graphics.Drawables;\nusing Android.OS;",
)
patch(
    "VoiceCraft.Server.Android/BackupRelayActivity.cs",
    "            InputType = InputTypes.ClassText | InputTypes.TextVariationUri,\n            SingleLine = true,\n            TextSize = 13,",
    "            InputType = InputTypes.ClassText | InputTypes.TextVariationUri,\n            TextSize = 13,",
)
patch(
    "VoiceCraft.Server.Android/BackupRelayActivity.cs",
    "        input.SetTextColor(Ink);\n        input.SetHintTextColor(Muted);",
    "        input.SetSingleLine(true);\n        input.SetTextColor(Ink);\n        input.SetHintTextColor(Muted);",
)

# ---------------------------------------------------------------------------
# Android: EndstoneBridgeController multi-relay state machine
# ---------------------------------------------------------------------------
patch(
    "VoiceCraft.Server.Android/EndstoneBridgeController.cs",
    "    private readonly Uri _relayUri;\n    private readonly string _serverId;",
    "    private const int MaxAttemptsPerRelay = 5;\n    private const int PeerTimeoutSeconds = 30;\n    private readonly IReadOnlyList<Uri> _relayUris;\n    private readonly string _serverId;",
)
patch(
    "VoiceCraft.Server.Android/EndstoneBridgeController.cs",
    "    private int _queuedMessages;\n\n    public bool RelayConnected",
    "    private int _queuedMessages;\n    private int _activeRelayIndex;\n    private int _relayFailures;\n    private DateTimeOffset? _peerMissingSince;\n\n    public bool RelayConnected",
)
patch(
    "VoiceCraft.Server.Android/EndstoneBridgeController.cs",
    "    public string Status => !RelayConnected\n        ? \"relay-disconnected\"\n        : EndstoneConnected ? \"endstone-connected\" : \"relay-only\";\n\n    public EndstoneBridgeController(string relayUrl, string serverId, string secret)\n    {\n        _relayUri = new Uri(relayUrl, UriKind.Absolute);\n        _serverId = serverId;\n        _secret = secret;\n    }",
    "    public string Status => !RelayConnected\n        ? \"relay-disconnected\"\n        : EndstoneConnected ? \"endstone-connected\" : \"relay-only\";\n\n    public int RelayCount => _relayUris.Count;\n    public int ActiveRelayIndex => _activeRelayIndex;\n    public string ActiveRelayName => _activeRelayIndex == 0 ? \"Primary\" : $\"Backup #{_activeRelayIndex}\";\n    public string ActiveRelayEndpoint => SafeEndpoint(ActiveRelayUri);\n    private Uri ActiveRelayUri => _relayUris[_activeRelayIndex];\n\n    public EndstoneBridgeController(IEnumerable<string> relayUrls, string serverId, string secret)\n    {\n        var urls = new List<Uri>();\n        foreach (var raw in relayUrls)\n        {\n            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))\n                continue;\n            if (urls.Any(existing => string.Equals(existing.AbsoluteUri, uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase)))\n                continue;\n            urls.Add(uri);\n        }\n        if (urls.Count == 0)\n            throw new ArgumentException(\"At least one relay URL is required.\", nameof(relayUrls));\n        _relayUris = urls;\n        _serverId = serverId;\n        _secret = secret;\n    }",
)
patch(
    "VoiceCraft.Server.Android/EndstoneBridgeController.cs",
    "        AndroidRuntimeLog.Append(\"BRIDGE\", $\"Phase 2 UI4.2 starting relay={SafeEndpoint(_relayUri)} server_id={_serverId}; secret hidden\");",
    "        AndroidRuntimeLog.Append(\"BRIDGE\", $\"Phase 2 UI4.3 starting relays={_relayUris.Count} active={ActiveRelayName} relay={ActiveRelayEndpoint} server_id={_serverId}; secret hidden\");",
)
old_run = '''    private async Task RunRelayAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                await ws.ConnectAsync(_relayUri, token);

                await SendDirectAsync(ws, new
                {
                    type = "hello",
                    role = "android",
                    serverId = _serverId,
                    secret = _secret,
                    protocol = 1,
                    appVersion = "phase2"
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
                LastError = string.Empty;
                AndroidRuntimeLog.Append("BRIDGE", $"Relay connected {SafeEndpoint(_relayUri)} server_id={_serverId}");
                QueueOutgoing(new
                {
                    type = "server_status",
                    status = "running",
                    voiceClients = VcServerApp.ConnectedClients,
                    bridgeVersion = "0.2.0"
                });

                using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                var sender = SenderLoopAsync(ws, connectionCts.Token);
                var receiver = ReceiverLoopAsync(ws, connectionCts.Token);
                await Task.WhenAny(sender, receiver);
                connectionCts.Cancel();
                await Task.WhenAll(IgnoreCancellation(sender), IgnoreCancellation(receiver));
            }
            catch (System.OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LastError = $"{ex.GetType().Name}: {ex.Message}";
                AndroidRuntimeLog.Append("BRIDGE", $"Relay disconnected: {LastError}; retrying in 5s");
            }
            finally
            {
                RelayConnected = false;
                EndstoneConnected = false;
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
'''
new_run = '''    private async Task RunRelayAsync(CancellationToken token)
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
                    appVersion = "1.7.1-android-phase2-ui4.3"
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
                    bridgeVersion = "0.2.5",
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
'''
patch("VoiceCraft.Server.Android/EndstoneBridgeController.cs", old_run, new_run)
patch(
    "VoiceCraft.Server.Android/EndstoneBridgeController.cs",
    "                    EndstoneConnected = connected;\n                    AndroidRuntimeLog.Append(\"BRIDGE\", $\"Endstone peer {(connected ? \"connected\" : \"disconnected\")}\");",
    "                    EndstoneConnected = connected;\n                    if (connected)\n                    {\n                        _relayFailures = 0;\n                        _peerMissingSince = null;\n                    }\n                    else\n                    {\n                        _peerMissingSince ??= DateTimeOffset.UtcNow;\n                    }\n                    AndroidRuntimeLog.Append(\"BRIDGE\", $\"Endstone peer {(connected ? \"connected\" : \"disconnected\")} via {ActiveRelayName}\");",
)
patch(
    "VoiceCraft.Server.Android/EndstoneBridgeController.cs",
    "        AndroidRuntimeLog.Append(\"BRIDGE\", \"Phase 2 controller stopped\");",
    "        AndroidRuntimeLog.Append(\"BRIDGE\", \"Phase 2 UI4.3 multi-relay controller stopped\");",
)

# ---------------------------------------------------------------------------
# Android foreground service: load list once, keep VoiceCraft runtime alive
# ---------------------------------------------------------------------------
patch(
    "VoiceCraft.Server.Android/VoiceCraftServerService.cs",
    "    public static string BridgeLastError { get; private set; } = string.Empty;\n    internal static BridgeDashboardSnapshot",
    "    public static string BridgeLastError { get; private set; } = string.Empty;\n    public static string BridgeActiveRelay { get; private set; } = \"Primary\";\n    public static int BridgeActiveRelayIndex { get; private set; }\n    public static int BridgeRelayCount { get; private set; } = 1;\n    internal static BridgeDashboardSnapshot",
)
patch(
    "VoiceCraft.Server.Android/VoiceCraftServerService.cs",
    "        var bridgeUrl = ServerPreferences.GetBridgeUrl(this);\n        var bridgeServerId = ServerPreferences.GetBridgeServerId(this);\n        var bridgeSecret = ServerPreferences.GetBridgeSecret(this);",
    "        var bridgeUrl = ServerPreferences.GetBridgeUrl(this);\n        var bridgeServerId = ServerPreferences.GetBridgeServerId(this);\n        var bridgeSecret = ServerPreferences.GetBridgeSecret(this);\n        var bridgeRelayUrls = new List<string> { bridgeUrl };\n        foreach (var backup in ServerPreferences.GetBridgeBackupUrls(this))\n        {\n            if (!IsBridgeUrlValid(backup))\n            {\n                AndroidRuntimeLog.Append(\"BRIDGE\", $\"Ignoring invalid optional Backup Relay: {SafeBridgeUrl(backup)}\");\n                continue;\n            }\n            if (!bridgeRelayUrls.Contains(backup, StringComparer.OrdinalIgnoreCase))\n                bridgeRelayUrls.Add(backup);\n        }",
)
patch(
    "VoiceCraft.Server.Android/VoiceCraftServerService.cs",
    "        BridgeStatus = \"starting\";\n        BridgeDashboard = BridgeDashboardSnapshot.Empty;",
    "        BridgeStatus = \"starting\";\n        BridgeActiveRelay = \"Primary\";\n        BridgeActiveRelayIndex = 0;\n        BridgeRelayCount = bridgeRelayUrls.Count;\n        BridgeDashboard = BridgeDashboardSnapshot.Empty;",
)
patch(
    "VoiceCraft.Server.Android/VoiceCraftServerService.cs",
    "            $\"Configured required outbound relay={SafeBridgeUrl(bridgeUrl)} server_id={bridgeServerId}; secret hidden\");",
    "            $\"Configured Primary relay={SafeBridgeUrl(bridgeUrl)} backups={bridgeRelayUrls.Count - 1} server_id={bridgeServerId}; shared secret hidden\");",
)
patch(
    "VoiceCraft.Server.Android/VoiceCraftServerService.cs",
    "        _serverTask = Task.Run(() => RunServerAsync(port, key, bridgeUrl, bridgeServerId, bridgeSecret));",
    "        _serverTask = Task.Run(() => RunServerAsync(port, key, bridgeRelayUrls, bridgeServerId, bridgeSecret));",
)
patch(
    "VoiceCraft.Server.Android/VoiceCraftServerService.cs",
    "        string bridgeUrl,\n        string bridgeServerId,\n        string bridgeSecret)\n    {\n        try\n        {\n            if (!IsValidBridgeConfig(bridgeUrl, bridgeServerId, bridgeSecret))",
    "        IReadOnlyList<string> bridgeRelayUrls,\n        string bridgeServerId,\n        string bridgeSecret)\n    {\n        try\n        {\n            var bridgeUrl = bridgeRelayUrls.Count > 0 ? bridgeRelayUrls[0] : string.Empty;\n            if (!IsValidBridgeConfig(bridgeUrl, bridgeServerId, bridgeSecret))",
)
patch(
    "VoiceCraft.Server.Android/VoiceCraftServerService.cs",
    "            _bridgeController = new EndstoneBridgeController(bridgeUrl, bridgeServerId, bridgeSecret);",
    "            _bridgeController = new EndstoneBridgeController(bridgeRelayUrls, bridgeServerId, bridgeSecret);",
)
patch(
    "VoiceCraft.Server.Android/VoiceCraftServerService.cs",
    "                    BridgeStatus = _bridgeController.Status;\n                    BridgeLastError = _bridgeController.LastError;\n                    BridgeDashboard = _bridgeController.DashboardSnapshot;",
    "                    BridgeStatus = _bridgeController.Status;\n                    BridgeLastError = _bridgeController.LastError;\n                    BridgeActiveRelay = _bridgeController.ActiveRelayName;\n                    BridgeActiveRelayIndex = _bridgeController.ActiveRelayIndex;\n                    BridgeRelayCount = _bridgeController.RelayCount;\n                    BridgeDashboard = _bridgeController.DashboardSnapshot;",
)
patch(
    "VoiceCraft.Server.Android/VoiceCraftServerService.cs",
    "                        ? $\"UDP/TCP {port} • Clients {VcServerApp.ConnectedClients} • Bridge {BridgeStatus}\"",
    "                        ? $\"UDP/TCP {port} • Clients {VcServerApp.ConnectedClients} • {BridgeActiveRelay} • Bridge {BridgeStatus}\"",
)
patch(
    "VoiceCraft.Server.Android/VoiceCraftServerService.cs",
    "            BridgeStatus = \"disabled\";\n            BridgeLastError = string.Empty;",
    "            BridgeStatus = \"disabled\";\n            BridgeLastError = string.Empty;\n            BridgeActiveRelay = \"Primary\";\n            BridgeActiveRelayIndex = 0;\n            BridgeRelayCount = 1;",
)

# ---------------------------------------------------------------------------
# Android UI4.3: optional manager button, config export, active relay status
# ---------------------------------------------------------------------------
patch(
    "VoiceCraft.Server.Android/ModernMainActivity.cs",
    "        _renderUrl.TextChanged += (_, _) => UpdateWebSocketFromRenderUrl();\n        setup.AddView(_renderUrl);",
    "        _renderUrl.TextChanged += (_, _) => UpdateWebSocketFromRenderUrl();\n        setup.AddView(_renderUrl);",
)
patch(
    "VoiceCraft.Server.Android/ModernMainActivity.cs",
    "        AddButton(renderButtons, T(\"เปิด Render\", \"OPEN RENDER\"), OpenRender, primary: true);\n        AddButton(renderButtons, T(\"คัดลอก WSS\", \"COPY WSS\"), CopyWebSocket);\n        setup.AddView(renderButtons);",
    "        AddButton(renderButtons, T(\"เปิด Render\", \"OPEN RENDER\"), OpenRender, primary: true);\n        AddButton(renderButtons, T(\"คัดลอก WSS\", \"COPY WSS\"), CopyWebSocket);\n        setup.AddView(renderButtons);\n\n        var backupButtons = ButtonRow();\n        AddButton(backupButtons, T(\"+ เพิ่ม/จัดการลิงก์สำรอง\", \"+ MANAGE BACKUP RELAYS\"), OpenBackupRelays, primary: true);\n        setup.AddView(backupButtons);\n        setup.AddView(Label(T(\"Backup เป็น Optional — ไม่เพิ่มก็เปิด Server ด้วย Primary ได้ตามปกติ\", \"Backups are optional — Primary-only startup works normally.\"), 11, Muted));",
)
patch(
    "VoiceCraft.Server.Android/ModernMainActivity.cs",
    "        T(\"ตั้งค่า Bridge\", \"Bridge Setup\"),\n            T(\"วาง Render URL ครั้งเดียว แล้วแอปจะเตรียมค่าที่เหลือให้\", \"Paste the Render URL once and the app prepares the rest\"));",
    "        T(\"ตั้งค่า Bridge\", \"Bridge Setup\"),\n            T(\"Primary จำเป็น ส่วน Backup เพิ่มได้ตามต้องการและใช้ Secret เดียวกัน\", \"Primary is required; optional backups can be added and share the same secret\"));",
)
old_plugin = '''    private string PluginConfig(bool maskSecret)
    {
        var secret = CurrentSecret();
        if (maskSecret && secret.Length > 0)
            secret = "••••••••••••••••";
        return
            "[tracking]\\n" +
            "interval_ticks = 2\\n" +
            "position_epsilon = 0.05\\n" +
            "rotation_epsilon = 1.0\\n" +
            "log_position_changes = false\\n" +
            "heartbeat_seconds = 30\\n\\n" +
            "[binding]\\n" +
            "min_key_length = 4\\n" +
            "max_key_length = 128\\n\\n" +
            "[bridge]\\n" +
            "enabled = true\\n" +
            $"url = \\"{CurrentWebSocket()}\\"\\n" +
            $"server_id = \\"{CurrentServerId()}\\"\\n" +
            $"secret = \\"{secret}\\"\\n" +
            "reconnect_seconds = 5\\n";
    }
'''
new_plugin = '''    private string PluginConfig(bool maskSecret)
    {
        var secret = CurrentSecret();
        if (maskSecret && secret.Length > 0)
            secret = "••••••••••••••••";
        var backups = ServerPreferences.GetBridgeBackupUrls(this);
        var backupArray = "[" + string.Join(", ", backups.Select(url => $"\\\"{url.Replace("\\\"", "\\\\\\\"")}\\\"")) + "]";
        return
            "[tracking]\\n" +
            "interval_ticks = 2\\n" +
            "position_epsilon = 0.05\\n" +
            "rotation_epsilon = 1.0\\n" +
            "log_position_changes = false\\n" +
            "heartbeat_seconds = 30\\n\\n" +
            "[binding]\\n" +
            "min_key_length = 4\\n" +
            "max_key_length = 128\\n\\n" +
            "[bridge]\\n" +
            "enabled = true\\n" +
            $"url = \\"{CurrentWebSocket()}\\"\\n" +
            $"backup_urls = {backupArray}\\n" +
            $"server_id = \\"{CurrentServerId()}\\"\\n" +
            $"secret = \\"{secret}\\"\\n" +
            "reconnect_seconds = 5\\n" +
            "max_attempts = 5\\n" +
            "peer_timeout_seconds = 30\\n";
    }
'''
patch("VoiceCraft.Server.Android/ModernMainActivity.cs", old_plugin, new_plugin)
patch(
    "VoiceCraft.Server.Android/ModernMainActivity.cs",
    "            var wss = CurrentWebSocket();\n            _relaySummary.Text = string.IsNullOrEmpty(wss) ? T(\"ยังไม่ได้ตั้งค่า Relay\", \"Relay not configured\") : wss;\n            _relaySummary.SetTextColor(string.IsNullOrEmpty(wss) ? Red : Ink);",
    "            var wss = CurrentWebSocket();\n            var backups = ServerPreferences.GetBridgeBackupUrls(this);\n            if (string.IsNullOrEmpty(wss))\n            {\n                _relaySummary.Text = T(\"ยังไม่ได้ตั้งค่า Relay\", \"Relay not configured\");\n                _relaySummary.SetTextColor(Red);\n            }\n            else if (VcServerApp.IsRunning || VoiceCraftServerService.IsServiceRunning)\n            {\n                _relaySummary.Text = $\"{VoiceCraftServerService.BridgeActiveRelay} • Relay {VoiceCraftServerService.BridgeActiveRelayIndex + 1}/{VoiceCraftServerService.BridgeRelayCount}\\n{wss}\\n{T(\"Backup\", \"Backups\")}: {backups.Count}\";\n                _relaySummary.SetTextColor(Ink);\n            }\n            else\n            {\n                _relaySummary.Text = $\"Primary: {wss}\\n{T(\"Backup (Optional)\", \"Backups (Optional)\")}: {backups.Count}\";\n                _relaySummary.SetTextColor(Ink);\n            }",
)
patch(
    "VoiceCraft.Server.Android/ModernMainActivity.cs",
    "    private void OpenRender()\n    {",
    "    private void OpenBackupRelays()\n    {\n        SaveCurrentConfiguration();\n        StartActivity(new Intent(this, typeof(BackupRelayActivity)));\n    }\n\n    private void OpenRender()\n    {",
)
patch(
    "VoiceCraft.Server.Android/ModernMainActivity.cs",
    "    protected override void OnPause()\n    {",
    "    protected override void OnResume()\n    {\n        base.OnResume();\n        RefreshBridgePreview();\n        RefreshUi();\n    }\n\n    protected override void OnPause()\n    {",
)
replace_all(
    "VoiceCraft.Server.Android/ModernMainActivity.cs",
    "endstone_voicecraft-0.2.0-py3-none-any.whl",
    "endstone_voicecraft-0.2.5-py3-none-any.whl",
)
replace_all(
    "VoiceCraft.Server.Android/ModernMainActivity.cs",
    "VoiceCraft Client → /vcbind",
    "VoiceCraft Client → /vc",
)
replace_all(
    "VoiceCraft.Server.Android/ModernMainActivity.cs",
    "• ใช้ /vcbind ABC12 โดยแทน ABC12 ด้วย Binding Key ของคุณ",
    "• ใช้ /vc แล้วเลือก Bind Microphone จากนั้นกรอก Binding Key ของคุณ",
)
replace_all(
    "VoiceCraft.Server.Android/ModernMainActivity.cs",
    "• Run /vcbind ABC12 using your actual 5-character binding key.",
    "• Run /vc, choose Bind Microphone, and enter your actual 5-character binding key.",
)

# ---------------------------------------------------------------------------
# Endstone 0.2.5: final exported metadata + multi-relay loader
# ---------------------------------------------------------------------------
patch(
    "VoiceCraft.Endstone/src/endstone_voicecraft/compat.py",
    "from .bridge import EndstoneRelayClient\nfrom .plugin import VoiceCraftEndstone as VoiceCraftEndstoneBase",
    "from .bridge import EndstoneRelayClient\nfrom .failover import MultiRelayEndstoneClient\nfrom .plugin import VoiceCraftEndstone as VoiceCraftEndstoneBase",
)
old_compat_bridge = '''        enabled = bool(bridge.get("enabled", False))
        url = str(bridge.get("url", "")).strip()
        server_id = str(bridge.get("server_id", "mcsv-main")).strip()
        secret = str(bridge.get("secret", "")).strip()
        reconnect_seconds = self._bounded_float(
            bridge.get("reconnect_seconds", 5), 1.0, 60.0, 5.0
        )

        usable, validation_error = self._validate_bridge_config(
            enabled, url, server_id, secret
        )
        self._bridge_enabled = usable
        self._bridge_server_id = server_id or "mcsv-main"
        self._bridge = (
            CompatibleEndstoneRelayClient(
                self.logger,
                url,
                self._bridge_server_id,
                secret,
                reconnect_seconds,
                plugin_version=self.version,
            )
            if usable
            else None
        )
'''
new_compat_bridge = '''        enabled = bool(bridge.get("enabled", False))
        url = str(bridge.get("url", "")).strip()
        server_id = str(bridge.get("server_id", "mcsv-main")).strip()
        secret = str(bridge.get("secret", "")).strip()
        reconnect_seconds = self._bounded_float(
            bridge.get("reconnect_seconds", 5), 1.0, 60.0, 5.0
        )
        max_attempts = self._bounded_int(bridge.get("max_attempts", 5), 1, 20, 5)
        peer_timeout_seconds = self._bounded_float(
            bridge.get("peer_timeout_seconds", 30), 5.0, 300.0, 30.0
        )

        usable, validation_error = self._validate_bridge_config(
            enabled, url, server_id, secret
        )
        relay_urls: list[str] = [url] if usable else []
        raw_backups = bridge.get("backup_urls", [])
        if isinstance(raw_backups, str):
            raw_backups = [raw_backups]
        if not isinstance(raw_backups, (list, tuple)):
            raw_backups = []
        for raw_backup in raw_backups:
            backup = str(raw_backup or "").strip()
            if not backup or backup in relay_urls:
                continue
            backup_ok, backup_error = self._validate_bridge_config(True, backup, server_id, secret)
            if backup_ok:
                relay_urls.append(backup)
            else:
                self.logger.warning(
                    f"BRIDGE optional Backup Relay ignored: {backup_error}; relay={self._safe_bridge_endpoint(backup)}"
                )

        self._bridge_enabled = usable
        self._bridge_server_id = server_id or "mcsv-main"
        self._bridge = (
            MultiRelayEndstoneClient(
                self.logger,
                relay_urls,
                self._bridge_server_id,
                secret,
                reconnect_seconds,
                plugin_version=self.version,
                max_attempts=max_attempts,
                peer_timeout_seconds=peer_timeout_seconds,
            )
            if usable
            else None
        )
'''
patch("VoiceCraft.Endstone/src/endstone_voicecraft/compat.py", old_compat_bridge, new_compat_bridge)
patch(
    "VoiceCraft.Endstone/src/endstone_voicecraft/compat.py",
    "    def _command_unbind(self, sender: CommandSender) -> bool:",
    "    @staticmethod\n    def _safe_bridge_endpoint(url: str) -> str:\n        return str(url or \"\").split(\"?\", 1)[0]\n\n    def _command_unbind(self, sender: CommandSender) -> bool:",
)
replace_all("VoiceCraft.Endstone/src/endstone_voicecraft/compat.py", "VoiceCraft Server Mobile UI4.1.", "VoiceCraft Server Mobile UI4.3.")
replace_all("VoiceCraft.Endstone/src/endstone_voicecraft/compat.py", "Use Copy Plugin Config in VoiceCraft Server Mobile UI4.1.", "Use Copy Plugin Config in VoiceCraft Server Mobile UI4.3.")

patch(
    "VoiceCraft.Endstone/src/endstone_voicecraft/menu.py",
    "from .auto_rebind import VoiceCraftEndstone as VoiceCraftEndstone023",
    "from .auto_rebind import VoiceCraftEndstone as VoiceCraftEndstone024",
)
patch(
    "VoiceCraft.Endstone/src/endstone_voicecraft/menu.py",
    "class VoiceCraftEndstone(VoiceCraftEndstone023):",
    "class VoiceCraftEndstone(VoiceCraftEndstone024):",
)
replace_all("VoiceCraft.Endstone/src/endstone_voicecraft/menu.py", "Endstone 0.2.4", "Endstone 0.2.5")
patch(
    "VoiceCraft.Endstone/src/endstone_voicecraft/menu.py",
    '    version = "0.2.4"',
    '    version = "0.2.5"',
)
patch(
    "VoiceCraft.Endstone/src/endstone_voicecraft/menu.py",
    '        self.logger.info("VoiceCraft UI ready: /vc")',
    '        self.logger.info("VoiceCraft UI + multi-relay failover ready: /vc")',
)

# ---------------------------------------------------------------------------
# CI version guards and package content
# ---------------------------------------------------------------------------
replace_all(".github/workflows/build-endstone.yml", "0.2.4", "0.2.5")
patch(
    ".github/workflows/build-endstone.yml",
    "          from endstone_voicecraft.compat import CompatibleEndstoneRelayClient\n          from endstone_voicecraft.listener import VoiceCraftListener",
    "          from endstone_voicecraft.compat import CompatibleEndstoneRelayClient\n          from endstone_voicecraft.failover import MultiRelayEndstoneClient\n          from endstone_voicecraft.listener import VoiceCraftListener",
    count=1,
)
patch(
    ".github/workflows/build-endstone.yml",
    "          assert CompatibleEndstoneRelayClient is not None\n          assert ActionForm is not None and ModalForm is not None and TextInput is not None",
    "          assert CompatibleEndstoneRelayClient is not None\n          assert MultiRelayEndstoneClient is not None\n          assert ActionForm is not None and ModalForm is not None and TextInput is not None",
    count=1,
)
patch(
    ".github/workflows/build-endstone.yml",
    '              assert "endstone_voicecraft/compat.py" in names, names\n              assert "endstone_voicecraft/auto_bind.py" in names, names',
    '              assert "endstone_voicecraft/compat.py" in names, names\n              assert "endstone_voicecraft/failover.py" in names, names\n              assert "endstone_voicecraft/auto_bind.py" in names, names',
)
# Add failover import to the installed-wheel validation block too.
patch(
    ".github/workflows/build-endstone.yml",
    "          from endstone_voicecraft.compat import CompatibleEndstoneRelayClient\n          from endstone_voicecraft.listener import VoiceCraftListener",
    "          from endstone_voicecraft.compat import CompatibleEndstoneRelayClient\n          from endstone_voicecraft.failover import MultiRelayEndstoneClient\n          from endstone_voicecraft.listener import VoiceCraftListener",
    count=1,
)
patch(
    ".github/workflows/build-endstone.yml",
    "          assert CompatibleEndstoneRelayClient is not None\n          assert ActionForm is not None and ModalForm is not None and TextInput is not None",
    "          assert CompatibleEndstoneRelayClient is not None\n          assert MultiRelayEndstoneClient is not None\n          assert ActionForm is not None and ModalForm is not None and TextInput is not None",
    count=1,
)
replace_all(".github/workflows/build-android.yml", "VoiceCraft-Server-Android-arm64-UI4.2", "VoiceCraft-Server-Android-arm64-UI4.3")
patch(
    ".github/workflows/build-android.yml",
    "      - name: Publish Android ARM64 APK\n        run: >-",
    "      - name: Validate multi-relay source guards\n        shell: bash\n        run: |\n          grep -q 'MaxAttemptsPerRelay = 5' VoiceCraft.Server.Android/EndstoneBridgeController.cs\n          grep -q 'BackupRelayActivity' VoiceCraft.Server.Android/ModernMainActivity.cs\n          grep -q 'ExtraBridgeBackupUrls' VoiceCraft.Server.Android/ServerPreferences.cs\n\n      - name: Publish Android ARM64 APK\n        run: >-",
)

# Keep this branch patch helper out of the final source tree after it runs.
Path("tools/apply_multi_relay_branch.py").unlink(missing_ok=True)
print("Multi-relay auto-failover branch patch applied successfully")
