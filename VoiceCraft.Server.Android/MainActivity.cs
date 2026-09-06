using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Text;
using Android.Views;
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
    private static readonly Color Blue = Color.Rgb(47, 128, 237);
    private static readonly Color LightBlue = Color.Rgb(234, 244, 255);
    private static readonly Color Page = Color.Rgb(247, 250, 252);
    private static readonly Color Ink = Color.Rgb(31, 41, 55);
    private static readonly Color Muted = Color.Rgb(107, 114, 128);
    private static readonly Color Border = Color.Rgb(220, 230, 242);
    private static readonly Color Green = Color.Rgb(34, 197, 94);
    private static readonly Color Amber = Color.Rgb(245, 158, 11);
    private static readonly Color Red = Color.Rgb(239, 68, 68);

    private readonly List<View> _pages = new();
    private readonly List<Button> _navButtons = new();
    private float _density = 1f;

    private TextView? _statusTitle;
    private TextView? _statusDetail;
    private TextView? _address;
    private TextView? _clientCount;
    private TextView? _bridgeState;
    private TextView? _relaySummary;
    private Button? _start;
    private Button? _stop;

    private global::Android.Widget.Switch? _bridgeEnabled;
    private EditText? _renderUrl;
    private TextView? _webSocketUrl;
    private EditText? _serverId;
    private EditText? _bridgeSecret;
    private TextView? _readiness;
    private TextView? _configPreview;
    private bool _bridgeSecretVisible;

    private EditText? _port;
    private EditText? _serverKey;
    private bool _serverKeyVisible;

    private TextView? _logView;
    private Handler? _handler;
    private IRunnable? _refreshRunnable;
    private long _renderedLogVersion = -1;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ActionBar?.Hide();
        _density = Resources?.DisplayMetrics?.Density ?? 1f;
        Window?.SetStatusBarColor(Blue);
        Window?.SetNavigationBarColor(Color.White);
        BuildUi();
        RequestNotificationPermission();
        StartRefreshLoop();
        AndroidRuntimeLog.Append("UI", "MainActivity opened");
    }

    private int Dp(int value) => (int)(value * _density + 0.5f);

    private void BuildUi()
    {
        var shell = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Background = Solid(Page)
        };
        shell.AddView(BuildHeader(), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(70)));

        var content = new FrameLayout(this) { Background = Solid(Page) };
        shell.AddView(content, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));

        _pages.Add(BuildHome());
        _pages.Add(BuildBridge());
        _pages.Add(BuildLogs());
        _pages.Add(BuildSettings());
        foreach (var page in _pages)
            content.AddView(page, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        shell.AddView(BuildNav(), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(68)));
        SetContentView(shell);
        ShowPage(0);
        UpdateWebSocketFromRenderUrl();
        RefreshUi();
        RefreshLog(true);
    }

    private View BuildHeader()
    {
        var row = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            Gravity = GravityFlags.CenterVertical,
            Background = Solid(Color.White)
        };
        row.SetPadding(Dp(20), Dp(8), Dp(20), Dp(8));

        var text = new LinearLayout(this) { Orientation = Orientation.Vertical };
        text.AddView(Label("VoiceCraft Server", 21, Ink, true));
        text.AddView(Label("Android • VoiceCraft 1.7.1", 12, Muted));
        row.AddView(text, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        var badge = Label("MODERN", 11, Blue, true);
        badge.Gravity = GravityFlags.Center;
        badge.SetPadding(Dp(12), Dp(7), Dp(12), Dp(7));
        badge.Background = Round(LightBlue, 14, Border);
        row.AddView(badge);
        return row;
    }

    private View BuildNav()
    {
        var nav = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            Gravity = GravityFlags.Center,
            Background = Solid(Color.White)
        };
        nav.SetPadding(Dp(7), Dp(7), Dp(7), Dp(7));
        AddNav(nav, "HOME", 0);
        AddNav(nav, "BRIDGE", 1);
        AddNav(nav, "LOGS", 2);
        AddNav(nav, "SETTINGS", 3);
        return nav;
    }

    private void AddNav(LinearLayout nav, string name, int index)
    {
        var button = MakeButton(name);
        button.Click += (_, _) => ShowPage(index);
        nav.AddView(button, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f)
        {
            LeftMargin = Dp(3),
            RightMargin = Dp(3)
        });
        _navButtons.Add(button);
    }

    private ScrollView BuildHome()
    {
        var (scroll, body) = NewPage("Dashboard", "Server status and quick actions");

        var hero = Card(LightBlue, Border);
        hero.AddView(Label("VOICECRAFT SERVER", 12, Blue, true));
        _statusTitle = Label("STOPPED", 30, Ink, true);
        _statusDetail = Label("Ready to start", 13, Muted);
        hero.AddView(_statusTitle);
        hero.AddView(_statusDetail);

        var metrics = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        metrics.AddView(Metric("0", "Voice clients", out _clientCount), Weight());
        metrics.AddView(Metric("OFF", "Bridge", out _bridgeState), Weight());
        hero.AddView(metrics);
        body.AddView(hero, CardLayout());

        var connection = Card();
        connection.AddView(CardTitle("Connection"));
        _address = Label("Detecting LAN address…", 19, Ink, true);
        _address.SetTextIsSelectable(true);
        connection.AddView(_address);
        connection.AddView(Label("VoiceCraft Client connects to this UDP address.", 12, Muted));
        var connButtons = ButtonRow();
        AddButton(connButtons, "COPY IP", (_, _) => CopyIp());
        AddButton(connButtons, "COPY PORT", (_, _) => CopyPort());
        AddButton(connButtons, "COPY ADDRESS", (_, _) => CopyAddress(), true);
        connection.AddView(connButtons);
        body.AddView(connection, CardLayout());

        var bridge = Card();
        bridge.AddView(CardTitle("Endstone Bridge"));
        _relaySummary = Label("Relay not configured", 14, Ink, true);
        _relaySummary.SetTextIsSelectable(true);
        bridge.AddView(_relaySummary);
        bridge.AddView(Label("Paste the Render URL once. The app generates /bridge and the full plugin config.", 12, Muted));
        var bridgeButtons = ButtonRow();
        AddButton(bridgeButtons, "COPY WSS", (_, _) => CopyWebSocket());
        AddButton(bridgeButtons, "COPY SECRET", (_, _) => CopyBridgeSecret());
        AddButton(bridgeButtons, "PLUGIN CONFIG", (_, _) => CopyPluginConfig(), true);
        bridge.AddView(bridgeButtons);
        body.AddView(bridge, CardLayout());

        var control = Card();
        control.AddView(CardTitle("Server Control"));
        var controls = ButtonRow();
        _start = MakeButton("START SERVER", true);
        _stop = MakeButton("STOP SERVER", false, true);
        _start.Click += (_, _) => StartServer();
        _stop.Click += (_, _) => StopServer();
        controls.AddView(_start, Weight(Dp(48)));
        controls.AddView(_stop, Weight(Dp(48)));
        control.AddView(controls);
        body.AddView(control, CardLayout());
        return scroll;
    }

    private ScrollView BuildBridge()
    {
        var (scroll, body) = NewPage("Bridge Setup", "Paste once, copy everything ready-to-use");

        var setup = Card();
        setup.AddView(CardTitle("Render Relay"));
        _bridgeEnabled = new global::Android.Widget.Switch(this)
        {
            Text = "Enable Endstone bridge",
            Checked = ServerPreferences.GetBridgeEnabled(this)
        };
        _bridgeEnabled.SetTextColor(Ink);
        _bridgeEnabled.CheckedChange += (_, _) => RefreshBridgePreview();
        setup.AddView(_bridgeEnabled);

        setup.AddView(InputLabel("Render Service URL"));
        _renderUrl = Input(ToServiceUrl(ServerPreferences.GetBridgeUrl(this)), InputTypes.ClassText | InputTypes.TextVariationUri);
        _renderUrl.Hint = "https://voicecraft-server-mobile.onrender.com";
        _renderUrl.TextChanged += (_, _) => UpdateWebSocketFromRenderUrl();
        setup.AddView(_renderUrl);

        setup.AddView(InputLabel("WebSocket URL • auto generated"));
        _webSocketUrl = ReadOnly("Enter Render URL above");
        setup.AddView(_webSocketUrl);
        var wss = ButtonRow();
        AddButton(wss, "COPY WEBSOCKET", (_, _) => CopyWebSocket(), true);
        setup.AddView(wss);

        setup.AddView(InputLabel("Server ID"));
        _serverId = Input(ServerPreferences.GetBridgeServerId(this), InputTypes.ClassText);
        _serverId.TextChanged += (_, _) => RefreshBridgePreview();
        setup.AddView(_serverId);

        setup.AddView(InputLabel("Bridge Secret"));
        _bridgeSecret = Input(ServerPreferences.GetBridgeSecret(this), InputTypes.ClassText | InputTypes.TextVariationPassword);
        _bridgeSecret.TextChanged += (_, _) => RefreshBridgePreview();
        setup.AddView(_bridgeSecret);
        var secretButtons = ButtonRow();
        AddButton(secretButtons, "SHOW / HIDE", (_, _) => ToggleBridgeSecret());
        AddButton(secretButtons, "GENERATE", (_, _) => GenerateBridgeSecret());
        AddButton(secretButtons, "COPY SECRET", (_, _) => CopyBridgeSecret(), true);
        setup.AddView(secretButtons);

        _readiness = Label("○ Setup incomplete", 13, Amber, true);
        _readiness.SetPadding(0, Dp(12), 0, 0);
        setup.AddView(_readiness);
        body.AddView(setup, CardLayout());

        var config = Card();
        config.AddView(CardTitle("Endstone Plugin Config"));
        config.AddView(Label("URL and secret are inserted automatically. Everything else stays at the tested defaults.", 12, Muted));
        _configPreview = Label(string.Empty, 11, Ink);
        _configPreview.Typeface = Typeface.Monospace;
        _configPreview.SetTextIsSelectable(true);
        _configPreview.SetPadding(Dp(12), Dp(12), Dp(12), Dp(12));
        _configPreview.Background = Round(Page, 12, Border);
        config.AddView(_configPreview, Top(Dp(12)));
        var configButtons = ButtonRow();
        AddButton(configButtons, "COPY PLUGIN CONFIG", (_, _) => CopyPluginConfig(), true);
        AddButton(configButtons, "COPY ALL SETUP", (_, _) => CopyAllSetup());
        config.AddView(configButtons);
        body.AddView(config, CardLayout());

        var flow = Card(LightBlue, Border);
        flow.AddView(CardTitle("Connection Flow"));
        flow.AddView(Label("Minecraft / MCSV", 14, Ink, true));
        flow.AddView(Label("↓  Endstone v0.2.x", 13, Blue));
        flow.AddView(Label("↓  Render WebSocket Relay", 13, Blue));
        flow.AddView(Label("↓  VoiceCraft Server Android", 13, Blue));
        flow.AddView(Label("Voice audio still uses UDP to Android.", 12, Muted));
        body.AddView(flow, CardLayout());
        return scroll;
    }

    private ScrollView BuildLogs()
    {
        var (scroll, body) = NewPage("Diagnostics", "Runtime, McHttp and bridge activity");
        var card = Card();
        card.AddView(CardTitle("Runtime Log"));
        _logView = Label("(no log entries yet)", 11, Ink);
        _logView.Typeface = Typeface.Monospace;
        _logView.SetMinHeight(Dp(340));
        _logView.SetTextIsSelectable(true);
        _logView.SetPadding(Dp(12), Dp(12), Dp(12), Dp(12));
        _logView.Background = Round(Page, 12, Border);
        card.AddView(_logView);
        var buttons = ButtonRow();
        AddButton(buttons, "COPY LOG", (_, _) => CopyLog(), true);
        AddButton(buttons, "CLEAR LOG", (_, _) =>
        {
            AndroidRuntimeLog.Clear();
            AndroidRuntimeLog.Append("UI", "Log cleared");
            RefreshLog(true);
        });
        card.AddView(buttons);
        body.AddView(card, CardLayout());

        var privacy = Card(LightBlue, Border);
        privacy.AddView(CardTitle("Privacy"));
        privacy.AddView(Label("Bridge secrets, login tokens and binding keys are intentionally hidden from runtime logs.", 12, Muted));
        body.AddView(privacy, CardLayout());
        return scroll;
    }

    private ScrollView BuildSettings()
    {
        var (scroll, body) = NewPage("Settings", "Voice server and legacy McHttp options");

        var server = Card();
        server.AddView(CardTitle("Voice Server"));
        server.AddView(InputLabel("Voice / McHttp Port"));
        _port = Input(ServerPreferences.GetVoicePort(this).ToString(), InputTypes.ClassNumber);
        server.AddView(_port);
        var portButtons = ButtonRow();
        AddButton(portButtons, "COPY PORT", (_, _) => CopyPort());
        AddButton(portButtons, "COPY IP:PORT", (_, _) => CopyAddress(), true);
        server.AddView(portButtons);
        body.AddView(server, CardLayout());

        var security = Card();
        security.AddView(CardTitle("Legacy McHttp Server Key"));
        security.AddView(Label("Separate from Bridge Secret. Endstone Phase 2 normally uses the bridge instead.", 12, Muted));
        _serverKey = Input(ServerPreferences.GetServerKey(this), InputTypes.ClassText | InputTypes.TextVariationPassword);
        security.AddView(_serverKey, Top(Dp(10)));
        var keyButtons = ButtonRow();
        AddButton(keyButtons, "SHOW / HIDE", (_, _) => ToggleServerKey());
        AddButton(keyButtons, "GENERATE", (_, _) => GenerateServerKey());
        AddButton(keyButtons, "COPY KEY", (_, _) => CopyServerKey(), true);
        security.AddView(keyButtons);
        body.AddView(security, CardLayout());

        var save = Card(LightBlue, Border);
        save.AddView(CardTitle("Save Configuration"));
        save.AddView(Label("Settings are also saved when the server starts or this app leaves the foreground.", 12, Muted));
        var saveButton = MakeButton("SAVE SETTINGS", true);
        saveButton.Click += (_, _) => SavePreferences(true);
        save.AddView(saveButton, Top(Dp(12)));
        body.AddView(save, CardLayout());
        return scroll;
    }

    private (ScrollView Scroll, LinearLayout Body) NewPage(string title, string subtitle)
    {
        var scroll = new ScrollView(this) { FillViewport = true, Background = Solid(Page) };
        var body = new LinearLayout(this) { Orientation = Orientation.Vertical };
        body.SetPadding(Dp(18), Dp(18), Dp(18), Dp(28));
        body.AddView(Label(title, 24, Ink, true));
        var sub = Label(subtitle, 13, Muted);
        sub.SetPadding(0, 0, 0, Dp(16));
        body.AddView(sub);
        scroll.AddView(body);
        return (scroll, body);
    }

    private LinearLayout Card(Color? fill = null, Color? stroke = null)
    {
        var card = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Background = Round(fill ?? Color.White, 18, stroke ?? Border)
        };
        card.SetPadding(Dp(16), Dp(16), Dp(16), Dp(16));
        card.Elevation = Dp(1);
        return card;
    }

    private TextView CardTitle(string text)
    {
        var label = Label(text, 16, Ink, true);
        label.SetPadding(0, 0, 0, Dp(10));
        return label;
    }

    private TextView Label(string text, float size, Color color, bool bold = false)
    {
        var view = new TextView(this) { Text = text, TextSize = size };
        view.SetTextColor(color);
        if (bold)
            view.SetTypeface(Typeface.Default, TypefaceStyle.Bold);
        return view;
    }

    private LinearLayout Metric(string value, string caption, out TextView valueView)
    {
        var box = new LinearLayout(this) { Orientation = Orientation.Vertical, Gravity = GravityFlags.Center };
        box.SetPadding(Dp(4), Dp(14), Dp(4), Dp(4));
        valueView = Label(value, 18, Ink, true);
        valueView.Gravity = GravityFlags.Center;
        var captionView = Label(caption, 11, Muted);
        captionView.Gravity = GravityFlags.Center;
        box.AddView(valueView);
        box.AddView(captionView);
        return box;
    }

    private TextView InputLabel(string text)
    {
        var label = Label(text, 12, Muted, true);
        label.SetPadding(0, Dp(12), 0, Dp(6));
        return label;
    }

    private EditText Input(string text, InputTypes type)
    {
        var input = new EditText(this)
        {
            Text = text,
            TextSize = 14,
            InputType = type,
            Background = Round(Color.White, 12, Border)
        };
        input.SetSingleLine(true);
        input.SetTextColor(Ink);
        input.SetHintTextColor(Muted);
        input.SetPadding(Dp(12), Dp(8), Dp(12), Dp(8));
        input.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(48));
        return input;
    }

    private TextView ReadOnly(string text)
    {
        var field = Label(text, 14, Ink);
        field.SetTextIsSelectable(true);
        field.Gravity = GravityFlags.CenterVertical;
        field.SetPadding(Dp(12), Dp(8), Dp(12), Dp(8));
        field.Background = Round(Page, 12, Border);
        field.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(48));
        return field;
    }

    private Button MakeButton(string text, bool primary = false, bool danger = false)
    {
        var fill = primary ? Blue : danger ? Color.Rgb(254, 242, 242) : Color.White;
        var stroke = primary ? Blue : danger ? Red : Border;
        var color = primary ? Color.White : danger ? Red : Blue;
        var button = new Button(this) { Text = text, TextSize = 11, Gravity = GravityFlags.Center };
        button.SetTextColor(color);
        button.Background = Round(fill, 12, stroke);
        return button;
    }

    private LinearLayout ButtonRow()
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal, Gravity = GravityFlags.CenterVertical };
        row.SetPadding(0, Dp(12), 0, 0);
        return row;
    }

    private void AddButton(LinearLayout row, string text, EventHandler click, bool primary = false)
    {
        var button = MakeButton(text, primary);
        button.Click += click;
        row.AddView(button, new LinearLayout.LayoutParams(0, Dp(46), 1f)
        {
            LeftMargin = Dp(3),
            RightMargin = Dp(3)
        });
    }

    private LinearLayout.LayoutParams Weight(int height = -2) => new(0, height, 1f);
    private LinearLayout.LayoutParams Top(int top) => new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = top };
    private LinearLayout.LayoutParams CardLayout() => new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { BottomMargin = Dp(12) };

    private GradientDrawable Round(Color fill, int radius, Color? stroke = null)
    {
        var drawable = new GradientDrawable();
        drawable.SetColor(fill);
        drawable.SetCornerRadius(Dp(radius));
        if (stroke.HasValue)
            drawable.SetStroke(Dp(1), stroke.Value);
        return drawable;
    }

    private static ColorDrawable Solid(Color color) => new(color);

    private void ShowPage(int index)
    {
        for (var i = 0; i < _pages.Count; i++)
            _pages[i].Visibility = i == index ? ViewStates.Visible : ViewStates.Gone;
        for (var i = 0; i < _navButtons.Count; i++)
        {
            var selected = i == index;
            _navButtons[i].SetTextColor(selected ? Blue : Muted);
            _navButtons[i].Background = Round(selected ? LightBlue : Color.White, 14);
        }
        if (index == 2)
            RefreshLog(true);
    }

    private void UpdateWebSocketFromRenderUrl()
    {
        if (_webSocketUrl == null)
            return;
        var generated = MakeWebSocketUrl(_renderUrl?.Text?.Trim() ?? string.Empty);
        _webSocketUrl.Text = string.IsNullOrEmpty(generated) ? "Enter a valid Render service URL" : generated;
        _webSocketUrl.SetTextColor(string.IsNullOrEmpty(generated) ? Muted : Ink);
        RefreshBridgePreview();
    }

    private void RefreshBridgePreview()
    {
        var ready = ValidateBridge(CurrentWebSocket(), CurrentServerId(), CurrentSecret(), out _);
        if (_readiness != null)
        {
            _readiness.Text = ready
                ? "● Configuration ready — WebSocket, secret and plugin config are ready"
                : "○ Setup incomplete — Render URL, Server ID and 16+ character secret are required";
            _readiness.SetTextColor(ready ? Green : Amber);
        }
        if (_configPreview != null)
            _configPreview.Text = PluginConfig(maskSecret: true);
    }

    private static string MakeWebSocketUrl(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;
        input = input.Trim();
        if (!input.Contains("://", StringComparison.Ordinal))
            input = "https://" + input;
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
            return string.Empty;

        var scheme = uri.Scheme.ToLowerInvariant() switch
        {
            "https" => "wss",
            "http" => "ws",
            "wss" => "wss",
            "ws" => "ws",
            _ => string.Empty
        };
        if (string.IsNullOrEmpty(scheme))
            return string.Empty;

        var builder = new UriBuilder(uri)
        {
            Scheme = scheme,
            Path = "/bridge",
            Query = string.Empty,
            Fragment = string.Empty
        };
        if (uri.IsDefaultPort)
            builder.Port = -1;
        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }

    private static string ToServiceUrl(string websocket)
    {
        if (string.IsNullOrWhiteSpace(websocket) || websocket.Contains("YOUR-RELAY", StringComparison.OrdinalIgnoreCase) || !Uri.TryCreate(websocket, UriKind.Absolute, out var uri))
            return string.Empty;
        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.Equals("ws", StringComparison.OrdinalIgnoreCase) ? "http" : "https",
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty
        };
        if (uri.IsDefaultPort)
            builder.Port = -1;
        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }

    private string CurrentWebSocket()
    {
        var value = _webSocketUrl?.Text?.Trim() ?? string.Empty;
        return value.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)
            ? value
            : string.Empty;
    }

    private string CurrentServerId() => _serverId?.Text?.Trim() ?? "mcsv-main";
    private string CurrentSecret() => _bridgeSecret?.Text?.Trim() ?? string.Empty;

    private string PluginConfig(bool maskSecret)
    {
        var secret = CurrentSecret();
        if (maskSecret && secret.Length > 0)
            secret = "••••••••••••••••";
        return
            "[tracking]\n" +
            "interval_ticks = 2\n" +
            "position_epsilon = 0.05\n" +
            "rotation_epsilon = 1.0\n" +
            "log_position_changes = false\n" +
            "heartbeat_seconds = 30\n\n" +
            "[binding]\n" +
            "min_key_length = 4\n" +
            "max_key_length = 128\n\n" +
            "[bridge]\n" +
            "enabled = true\n" +
            $"url = \"{Toml(CurrentWebSocket())}\"\n" +
            $"server_id = \"{Toml(CurrentServerId())}\"\n" +
            $"secret = \"{Toml(secret)}\"\n" +
            "reconnect_seconds = 5\n";
    }

    private static string Toml(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private void StartServer()
    {
        if (!TryCurrentPort(out var port))
        {
            Toast.MakeText(this, "Port must be 1-65535", ToastLength.Short)?.Show();
            ShowPage(3);
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
        if (bridgeEnabled && !ValidateBridge(CurrentWebSocket(), CurrentServerId(), CurrentSecret(), out var error))
        {
            Toast.MakeText(this, error, ToastLength.Long)?.Show();
            ShowPage(1);
            return;
        }

        ServerPreferences.Save(this, port, key);
        ServerPreferences.SaveBridge(this, bridgeEnabled, CurrentWebSocket(), CurrentServerId(), CurrentSecret());
        AndroidRuntimeLog.Append("UI", $"START SERVER pressed; port={port}; bridge={(bridgeEnabled ? "enabled" : "disabled")}");

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
    }

    private void StopServer()
    {
        AndroidRuntimeLog.Append("UI", "STOP SERVER pressed");
        VcServerApp.Shutdown();
        StopService(new Intent(this, typeof(VoiceCraftServerService)));
        RefreshUi();
    }

    private void SavePreferences(bool toast)
    {
        var port = TryCurrentPort(out var current) ? current : ServerPreferences.GetVoicePort(this);
        var key = _serverKey?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            key = Guid.NewGuid().ToString("N");
            if (_serverKey != null)
                _serverKey.Text = key;
        }
        ServerPreferences.Save(this, port, key);
        ServerPreferences.SaveBridge(this, _bridgeEnabled?.Checked == true, CurrentWebSocket(), CurrentServerId(), CurrentSecret());
        if (toast)
            Toast.MakeText(this, "Settings saved", ToastLength.Short)?.Show();
    }

    private bool TryCurrentPort(out int port)
    {
        if (int.TryParse(_port?.Text, out port) && port is >= 1 and <= 65535)
            return true;
        port = ServerPreferences.GetVoicePort(this);
        return _port == null;
    }

    private void RefreshUi()
    {
        var ip = GetLanIpv4() ?? "No LAN IPv4";
        var port = TryCurrentPort(out var current) ? current : ServerPreferences.GetVoicePort(this);
        var running = VcServerApp.IsRunning;
        var service = VoiceCraftServerService.IsServiceRunning;

        if (_statusTitle != null && _statusDetail != null)
        {
            if (VoiceCraftServerService.LastError != null)
            {
                _statusTitle.Text = "ERROR";
                _statusTitle.SetTextColor(Red);
                _statusDetail.Text = "Open Logs for details";
            }
            else if (running)
            {
                _statusTitle.Text = "RUNNING";
                _statusTitle.SetTextColor(Green);
                _statusDetail.Text = $"UDP/TCP {port} • foreground service active";
            }
            else if (service)
            {
                _statusTitle.Text = "STARTING";
                _statusTitle.SetTextColor(Amber);
                _statusDetail.Text = $"Preparing port {port}";
            }
            else
            {
                _statusTitle.Text = "STOPPED";
                _statusTitle.SetTextColor(Ink);
                _statusDetail.Text = "Ready to start";
            }
        }

        if (_address != null)
            _address.Text = $"{ip}:{port}";
        if (_clientCount != null)
            _clientCount.Text = VcServerApp.ConnectedClients.ToString();
        if (_bridgeState != null)
            _bridgeState.Text = ShortBridge(VoiceCraftServerService.BridgeStatus);
        if (_relaySummary != null)
            _relaySummary.Text = string.IsNullOrEmpty(CurrentWebSocket()) ? "Relay not configured" : CurrentWebSocket();
        if (_start != null)
            _start.Enabled = !running && !service;
        if (_stop != null)
            _stop.Enabled = running || service;
        RefreshBridgePreview();
        RefreshLog();
    }

    private static string ShortBridge(string status) => status switch
    {
        "endstone-connected" => "ONLINE",
        "relay-only" => "RELAY",
        "starting" => "STARTING",
        "invalid-config" => "CONFIG",
        "disabled" => "OFF",
        _ => status.ToUpperInvariant()
    };

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

    private void CopyIp() => Copy("VoiceCraft LAN IP", GetLanIpv4() ?? "0.0.0.0");
    private void CopyPort() => Copy("VoiceCraft port", (TryCurrentPort(out var port) ? port : ServerPreferences.GetVoicePort(this)).ToString());
    private void CopyAddress() => Copy("VoiceCraft address", $"{GetLanIpv4() ?? "0.0.0.0"}:{(TryCurrentPort(out var port) ? port : ServerPreferences.GetVoicePort(this))}");

    private void CopyWebSocket()
    {
        if (string.IsNullOrEmpty(CurrentWebSocket()))
        {
            Toast.MakeText(this, "Enter a valid Render URL first", ToastLength.Short)?.Show();
            return;
        }
        Copy("VoiceCraft WebSocket", CurrentWebSocket());
    }

    private void CopyBridgeSecret()
    {
        if (string.IsNullOrEmpty(CurrentSecret()))
        {
            Toast.MakeText(this, "Bridge secret is empty", ToastLength.Short)?.Show();
            return;
        }
        Copy("VoiceCraft Bridge Secret", CurrentSecret(), true);
    }

    private void CopyServerKey()
    {
        var key = _serverKey?.Text?.Trim() ?? string.Empty;
        if (key.Length == 0)
            return;
        Copy("VoiceCraft Server Key", key, true);
    }

    private void CopyPluginConfig()
    {
        if (!ValidateBridge(CurrentWebSocket(), CurrentServerId(), CurrentSecret(), out var error))
        {
            Toast.MakeText(this, error, ToastLength.Long)?.Show();
            ShowPage(1);
            return;
        }
        Copy("VoiceCraft Endstone config.toml", PluginConfig(false), true);
    }

    private void CopyAllSetup()
    {
        if (!ValidateBridge(CurrentWebSocket(), CurrentServerId(), CurrentSecret(), out var error))
        {
            Toast.MakeText(this, error, ToastLength.Long)?.Show();
            return;
        }
        var text =
            $"Render Environment Variable:\nBRIDGE_SECRET={CurrentSecret()}\n\n" +
            $"Android Bridge:\nWebSocket={CurrentWebSocket()}\nServer ID={CurrentServerId()}\nBridge Secret={CurrentSecret()}\n\n" +
            "Endstone config.toml:\n" + PluginConfig(false);
        Copy("VoiceCraft complete bridge setup", text, true);
    }

    private void CopyLog()
    {
        var log = AndroidRuntimeLog.Snapshot();
        if (string.IsNullOrWhiteSpace(log))
        {
            Toast.MakeText(this, "Log is empty", ToastLength.Short)?.Show();
            return;
        }
        Copy("VoiceCraft Server log", log);
    }

    private void Copy(string label, string value, bool secret = false)
    {
        if (GetSystemService(ClipboardService) is not global::Android.Content.ClipboardManager clipboard)
            return;
        clipboard.PrimaryClip = ClipData.NewPlainText(label, value);
        Toast.MakeText(this, secret ? "Copied — clipboard contains a secret" : "Copied", secret ? ToastLength.Long : ToastLength.Short)?.Show();
    }

    private void GenerateBridgeSecret()
    {
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        if (_bridgeSecret != null)
        {
            _bridgeSecret.Text = secret;
            _bridgeSecret.SetSelection(secret.Length);
        }
    }

    private void GenerateServerKey()
    {
        var key = Guid.NewGuid().ToString("N");
        if (_serverKey != null)
        {
            _serverKey.Text = key;
            _serverKey.SetSelection(key.Length);
        }
    }

    private void ToggleBridgeSecret()
    {
        if (_bridgeSecret == null)
            return;
        _bridgeSecretVisible = !_bridgeSecretVisible;
        _bridgeSecret.InputType = _bridgeSecretVisible
            ? InputTypes.ClassText | InputTypes.TextVariationVisiblePassword
            : InputTypes.ClassText | InputTypes.TextVariationPassword;
        _bridgeSecret.SetSelection(_bridgeSecret.Text?.Length ?? 0);
    }

    private void ToggleServerKey()
    {
        if (_serverKey == null)
            return;
        _serverKeyVisible = !_serverKeyVisible;
        _serverKey.InputType = _serverKeyVisible
            ? InputTypes.ClassText | InputTypes.TextVariationVisiblePassword
            : InputTypes.ClassText | InputTypes.TextVariationPassword;
        _serverKey.SetSelection(_serverKey.Text?.Length ?? 0);
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
            error = "Paste a valid Render URL so WebSocket can be generated";
            return false;
        }
        if (!string.Equals(uri.AbsolutePath.TrimEnd('/'), "/bridge", StringComparison.OrdinalIgnoreCase))
        {
            error = "WebSocket path must be /bridge";
            return false;
        }
        error = string.Empty;
        return true;
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

    protected override void OnPause()
    {
        SavePreferences(false);
        base.OnPause();
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
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(x => x.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                ?.ToString();
        }
        catch
        {
            return null;
        }
    }
}
