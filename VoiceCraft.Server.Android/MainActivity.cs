using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Text;
using Android.Widget;
using Java.Lang;
using VcServerApp = VoiceCraft.Server.App;

namespace VoiceCraft.Server.Android;

[Activity(
    Name = "chat.voicecraft.server.MainActivity",
    Label = "VoiceCraft Server",
    MainLauncher = true,
    Exported = true)]
public sealed class MainActivity : Activity
{
    private TextView? _status;
    private TextView? _connectionInfo;
    private TextView? _logView;
    private EditText? _port;
    private EditText? _serverKey;
    private CheckBox? _bridgeEnabled;
    private EditText? _bridgeUrl;
    private EditText? _bridgeServerId;
    private EditText? _bridgeSecret;
    private Handler? _handler;
    private IRunnable? _refreshRunnable;
    private long _renderedLogVersion = -1;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        BuildUi();
        RequestNotificationPermission();
        StartRefreshLoop();
        AndroidRuntimeLog.Append("UI", "MainActivity opened");
    }

    private void BuildUi()
    {
        var density = Resources?.DisplayMetrics?.Density ?? 1f;
        int Dp(int value) => (int)(value * density + 0.5f);

        var scroll = new ScrollView(this);
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetPadding(Dp(20), Dp(24), Dp(20), Dp(24));
        scroll.AddView(root);

        root.AddView(new TextView(this)
        {
            Text = "VoiceCraft Server • Android Phase 2",
            TextSize = 22
        });

        _status = new TextView(this) { TextSize = 18 };
        _status.SetPadding(0, Dp(18), 0, Dp(8));
        root.AddView(_status);

        _connectionInfo = new TextView(this) { TextSize = 15 };
        _connectionInfo.SetTextIsSelectable(true);
        root.AddView(_connectionInfo);

        root.AddView(new TextView(this) { Text = "Voice / McHttp port", TextSize = 14 });
        _port = new EditText(this)
        {
            InputType = InputTypes.ClassNumber,
            Text = ServerPreferences.GetVoicePort(this).ToString()
        };
        root.AddView(_port);

        root.AddView(new TextView(this) { Text = "Server key (legacy McHttp token)", TextSize = 14 });
        _serverKey = new EditText(this)
        {
            InputType = InputTypes.ClassText,
            Text = ServerPreferences.GetServerKey(this)
        };
        _serverKey.SetSingleLine(true);
        root.AddView(_serverKey);

        var bridgeHeader = new TextView(this)
        {
            Text = "Endstone Phase 2 Bridge (Render WSS)",
            TextSize = 16
        };
        bridgeHeader.SetPadding(0, Dp(18), 0, Dp(6));
        root.AddView(bridgeHeader);

        _bridgeEnabled = new CheckBox(this)
        {
            Text = "Enable Endstone bridge",
            Checked = ServerPreferences.GetBridgeEnabled(this)
        };
        root.AddView(_bridgeEnabled);

        root.AddView(new TextView(this) { Text = "Relay WebSocket URL", TextSize = 14 });
        _bridgeUrl = new EditText(this)
        {
            InputType = InputTypes.ClassText | InputTypes.TextVariationUri,
            Text = ServerPreferences.GetBridgeUrl(this)
        };
        _bridgeUrl.SetSingleLine(true);
        root.AddView(_bridgeUrl);

        root.AddView(new TextView(this) { Text = "Bridge server ID", TextSize = 14 });
        _bridgeServerId = new EditText(this)
        {
            InputType = InputTypes.ClassText,
            Text = ServerPreferences.GetBridgeServerId(this)
        };
        _bridgeServerId.SetSingleLine(true);
        root.AddView(_bridgeServerId);

        root.AddView(new TextView(this) { Text = "Bridge secret (same value on Render + Endstone)", TextSize = 14 });
        _bridgeSecret = new EditText(this)
        {
            InputType = InputTypes.ClassText | InputTypes.TextVariationPassword,
            Text = ServerPreferences.GetBridgeSecret(this)
        };
        _bridgeSecret.SetSingleLine(true);
        root.AddView(_bridgeSecret);

        var copyBridge = new Button(this) { Text = "COPY BRIDGE SETUP" };
        copyBridge.Click += (_, _) => CopyBridgeSetup();
        root.AddView(copyBridge);

        var start = new Button(this) { Text = "START SERVER" };
        start.Click += (_, _) => StartServer();
        root.AddView(start);

        var stop = new Button(this) { Text = "STOP SERVER" };
        stop.Click += (_, _) => StopServer();
        root.AddView(stop);

        var copy = new Button(this) { Text = "COPY CONNECTION INFO" };
        copy.Click += (_, _) => CopyConnectionInfo();
        root.AddView(copy);

        var logHeader = new TextView(this)
        {
            Text = "Runtime / McHttp / Endstone Bridge Log",
            TextSize = 16
        };
        logHeader.SetPadding(0, Dp(18), 0, Dp(6));
        root.AddView(logHeader);

        _logView = new TextView(this)
        {
            TextSize = 11,
            Typeface = Typeface.Monospace
        };
        _logView.SetMinHeight(Dp(260));
        _logView.SetTextIsSelectable(true);
        _logView.SetPadding(Dp(10), Dp(10), Dp(10), Dp(10));
        root.AddView(_logView);

        var copyLog = new Button(this) { Text = "COPY LOG" };
        copyLog.Click += (_, _) => CopyLog();
        root.AddView(copyLog);

        var clearLog = new Button(this) { Text = "CLEAR LOG" };
        clearLog.Click += (_, _) =>
        {
            AndroidRuntimeLog.Clear();
            AndroidRuntimeLog.Append("UI", "Log cleared");
            RefreshLog(true);
        };
        root.AddView(clearLog);

        var note = new TextView(this)
        {
            Text = "Bridge credentials and binding keys are intentionally hidden from runtime logs. COPY BRIDGE SETUP copies the secret only when you explicitly press it. Render WSS handles Minecraft state/binding; VoiceCraft audio still uses UDP.",
            TextSize = 13
        };
        note.SetPadding(0, Dp(16), 0, 0);
        root.AddView(note);

        SetContentView(scroll);
        RefreshUi();
        RefreshLog(true);
    }

    private void StartServer()
    {
        if (!int.TryParse(_port?.Text, out var port) || port is < 1 or > 65535)
        {
            Toast.MakeText(this, "Port must be 1-65535", ToastLength.Short)?.Show();
            return;
        }

        var key = _serverKey?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            key = Guid.NewGuid().ToString("N");
            if (_serverKey != null)
                _serverKey.Text = key;
        }

        var bridgeEnabled = _bridgeEnabled?.Checked == true;
        var bridgeUrl = _bridgeUrl?.Text?.Trim() ?? string.Empty;
        var bridgeServerId = _bridgeServerId?.Text?.Trim() ?? string.Empty;
        var bridgeSecret = _bridgeSecret?.Text?.Trim() ?? string.Empty;

        if (bridgeEnabled && !ValidateBridge(bridgeUrl, bridgeServerId, bridgeSecret, out var bridgeError))
        {
            Toast.MakeText(this, bridgeError, ToastLength.Long)?.Show();
            return;
        }

        AndroidRuntimeLog.Append("UI", $"START SERVER pressed; port={port}; bridge={(bridgeEnabled ? "enabled" : "disabled")}");
        ServerPreferences.Save(this, port, key);
        ServerPreferences.SaveBridge(this, bridgeEnabled, bridgeUrl, bridgeServerId, bridgeSecret);
        var intent = new Intent(this, typeof(VoiceCraftServerService));
        intent.PutExtra(ServerPreferences.ExtraVoicePort, port);
        intent.PutExtra(ServerPreferences.ExtraServerKey, key);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
#pragma warning disable CA1416
            StartForegroundService(intent);
#pragma warning restore CA1416
        else
            StartService(intent);

        RefreshUi();
        RefreshLog(true);
    }

    private void StopServer()
    {
        AndroidRuntimeLog.Append("UI", "STOP SERVER pressed");
        VoiceCraft.Server.App.Shutdown();
        StopService(new Intent(this, typeof(VoiceCraftServerService)));
        RefreshUi();
    }

    private void RefreshUi()
    {
        var ip = GetLanIpv4() ?? "(no LAN IPv4 detected)";
        var port = ServerPreferences.GetVoicePort(this);
        var running = VcServerApp.IsRunning;
        var service = VoiceCraftServerService.IsServiceRunning;
        var bridgeEnabled = ServerPreferences.GetBridgeEnabled(this);
        var bridgeUrl = ServerPreferences.GetBridgeUrl(this);
        var bridgeServerId = ServerPreferences.GetBridgeServerId(this);

        if (_status != null)
        {
            _status.Text = VoiceCraftServerService.LastError != null
                ? $"Status: ERROR\n{VoiceCraftServerService.LastError}"
                : running
                    ? $"Status: RUNNING • Voice clients {VcServerApp.ConnectedClients}\nEndstone bridge: {VoiceCraftServerService.BridgeStatus}"
                    : service ? "Status: STARTING…" : "Status: STOPPED";
        }

        if (_connectionInfo != null)
        {
            _connectionInfo.Text =
                $"LAN IP: {ip}\n" +
                $"Voice client: {ip}:{port} (UDP / LAN)\n" +
                $"McHttp legacy test: http://{ip}:{port} (TCP)\n" +
                $"Endstone bridge: {(bridgeEnabled ? "enabled" : "disabled")}\n" +
                $"Relay: {SafeBridgeUrl(bridgeUrl)}\n" +
                $"Server ID: {bridgeServerId}\n";
        }

        RefreshLog();
    }

    private void RefreshLog(bool force = false)
    {
        if (_logView == null)
            return;

        var version = AndroidRuntimeLog.Version;
        if (!force && version == _renderedLogVersion)
            return;

        _renderedLogVersion = version;
        var text = AndroidRuntimeLog.Snapshot();
        _logView.Text = string.IsNullOrWhiteSpace(text) ? "(no log entries yet)" : text;
    }

    private void CopyConnectionInfo()
    {
        var ip = GetLanIpv4() ?? "0.0.0.0";
        var port = ServerPreferences.GetVoicePort(this);
        var bridgeUrl = ServerPreferences.GetBridgeUrl(this);
        var bridgeServerId = ServerPreferences.GetBridgeServerId(this);
        var text =
            $"VoiceCraft LAN UDP: {ip}:{port}\n" +
            $"Endstone relay: {SafeBridgeUrl(bridgeUrl)}\n" +
            $"Bridge server ID: {bridgeServerId}\n" +
            $"Bridge status: {VoiceCraftServerService.BridgeStatus}";

        if (GetSystemService(ClipboardService) is global::Android.Content.ClipboardManager clipboard)
        {
            clipboard.PrimaryClip = ClipData.NewPlainText("VoiceCraft connection", text);
            Toast.MakeText(this, "Connection info copied (secret excluded)", ToastLength.Short)?.Show();
        }
    }

    private void CopyBridgeSetup()
    {
        var enabled = _bridgeEnabled?.Checked == true;
        var url = _bridgeUrl?.Text?.Trim() ?? ServerPreferences.GetBridgeUrl(this);
        var serverId = _bridgeServerId?.Text?.Trim() ?? ServerPreferences.GetBridgeServerId(this);
        var secret = _bridgeSecret?.Text?.Trim() ?? ServerPreferences.GetBridgeSecret(this);
        var text =
            $"Render env:\nBRIDGE_SECRET={secret}\n\n" +
            $"Endstone config.toml:\n[bridge]\nenabled = {enabled.ToString().ToLowerInvariant()}\n" +
            $"url = \"{url}\"\nserver_id = \"{serverId}\"\nsecret = \"{secret}\"\nreconnect_seconds = 5\n";

        if (GetSystemService(ClipboardService) is global::Android.Content.ClipboardManager clipboard)
        {
            clipboard.PrimaryClip = ClipData.NewPlainText("VoiceCraft bridge setup", text);
            Toast.MakeText(this, "Bridge setup copied — contains the bridge secret", ToastLength.Long)?.Show();
        }
    }

    private void CopyLog()
    {
        var text = AndroidRuntimeLog.Snapshot();
        if (string.IsNullOrWhiteSpace(text))
        {
            Toast.MakeText(this, "Log is empty", ToastLength.Short)?.Show();
            return;
        }

        if (GetSystemService(ClipboardService) is global::Android.Content.ClipboardManager clipboard)
        {
            clipboard.PrimaryClip = ClipData.NewPlainText("VoiceCraft Server log", text);
            Toast.MakeText(this, "Log copied", ToastLength.Short)?.Show();
        }
    }

    private static bool ValidateBridge(string url, string serverId, string secret, out string error)
    {
        if (string.IsNullOrWhiteSpace(serverId))
        {
            error = "Bridge server ID is required";
            return false;
        }
        if (secret.Length < 16)
        {
            error = "Bridge secret must be at least 16 characters";
            return false;
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("ws" or "wss"))
        {
            error = "Relay URL must start with ws:// or wss://";
            return false;
        }
        if (url.Contains("YOUR-RELAY", StringComparison.OrdinalIgnoreCase))
        {
            error = "Replace YOUR-RELAY with your Render relay URL";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static string SafeBridgeUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return "(not configured)";
        return uri.GetLeftPart(UriPartial.Path);
    }

    private void StartRefreshLoop()
    {
        _handler = new Handler(Looper.MainLooper!);
        _refreshRunnable = new Runnable(() =>
        {
            RefreshUi();
            if (_handler != null && _refreshRunnable != null)
                _handler.PostDelayed(_refreshRunnable, 750);
        });
        _handler.Post(_refreshRunnable);
    }

    protected override void OnDestroy()
    {
        if (_handler != null && _refreshRunnable != null)
            _handler.RemoveCallbacks(_refreshRunnable);
        _refreshRunnable?.Dispose();
        _refreshRunnable = null;
        _handler?.Dispose();
        _handler = null;
        base.OnDestroy();
    }

    private void RequestNotificationPermission()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu)
            return;

#pragma warning disable CA1416
        if (CheckSelfPermission(Manifest.Permission.PostNotifications) != Permission.Granted)
            RequestPermissions([Manifest.Permission.PostNotifications], 1701);
#pragma warning restore CA1416
    }

    private static string? GetLanIpv4()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(x => x.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork &&
                                     !IPAddress.IsLoopback(a))
                ?.ToString();
        }
        catch
        {
            return null;
        }
    }
}
