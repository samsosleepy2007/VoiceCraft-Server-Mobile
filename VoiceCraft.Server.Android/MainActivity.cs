using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
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
    private static readonly Color PrimaryBlue = Color.Rgb(47, 128, 237);
    private static readonly Color AccentBlue = Color.Rgb(86, 204, 242);
    private static readonly Color LightBlue = Color.Rgb(234, 244, 255);
    private static readonly Color PageBackground = Color.Rgb(247, 250, 252);
    private static readonly Color CardBackground = Color.White;
    private static readonly Color TextPrimary = Color.Rgb(31, 41, 55);
    private static readonly Color TextSecondary = Color.Rgb(107, 114, 128);
    private static readonly Color BorderColor = Color.Rgb(220, 230, 242);
    private static readonly Color SuccessGreen = Color.Rgb(34, 197, 94);
    private static readonly Color WarningAmber = Color.Rgb(245, 158, 11);
    private static readonly Color ErrorRed = Color.Rgb(239, 68, 68);

    private readonly List<View> _pages = new();
    private readonly List<Button> _navButtons = new();

    private float _density = 1f;
    private int _selectedPage;

    private TextView? _heroStatus;
    private TextView? _heroDetail;
    private TextView? _homeAddress;
    private TextView? _homeClients;
    private TextView? _homeBridge;
    private TextView? _homeRelay;

    private EditText? _port;
    private EditText? _serverKey;
    private bool _serverKeyVisible;

    private Android.Widget.Switch? _bridgeEnabled;
    private EditText? _renderUrl;
    private TextView? _bridgeUrl;
    private EditText? _bridgeServerId;
    private EditText? _bridgeSecret;
    private bool _bridgeSecretVisible;
    private TextView? _bridgeReadiness;
    private TextView? _pluginConfigPreview;

    private TextView? _logView;
    private Button? _startButton;
    private Button? _stopButton;

    private Handler? _handler;
    private IRunnable? _refreshRunnable;
    private long _renderedLogVersion = -1;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ActionBar?.Hide();
        _density = Resources?.DisplayMetrics?.Density ?? 1f;
        Window?.SetStatusBarColor(PrimaryBlue);
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
            Background = Solid(PageBackground)
        };

        shell.AddView(BuildTopBar(), new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            Dp(72)));

        var content = new FrameLayout(this)
        {
            Background = Solid(PageBackground)
        };
        shell.AddView(content, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            0,
            1f));

        _pages.Add(BuildHomePage());
        _pages.Add(BuildBridgePage());
        _pages.Add(BuildLogsPage());
        _pages.Add(BuildSettingsPage());

        foreach (var page in _pages)
            content.AddView(page, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent));

        shell.AddView(BuildBottomNavigation(), new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            Dp(68)));

        SetContentView(shell);
        ShowPage(0);
        UpdateGeneratedBridgeUrl();
        RefreshUi();
        RefreshLog(true);
    }

    private View BuildTopBar()
    {
        var bar = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            Gravity = GravityFlags.CenterVertical,
            Background = Solid(Color.White)
        };
        bar.SetPadding(Dp(20), Dp(10), Dp(20), Dp(10));

        var titleBox = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var title = NewText("VoiceCraft Server", 21, TextPrimary, true);
        var subtitle = NewText("Android • VoiceCraft 1.7.1", 12, TextSecondary);
        titleBox.AddView(title);
        titleBox.AddView(subtitle);
        bar.AddView(titleBox, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        var badge = NewText("MODERN", 11, PrimaryBlue, true);
        badge.Gravity = GravityFlags.Center;
        badge.Background = Rounded(LightBlue, 14, BorderColor);
        badge.SetPadding(Dp(12), Dp(7), Dp(12), Dp(7));
        bar.AddView(badge);
        return bar;
    }

    private View BuildBottomNavigation()
    {
        var nav = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            Gravity = GravityFlags.Center,
            Background = Solid(Color.White)
        };
        nav.SetPadding(Dp(8), Dp(7), Dp(8), Dp(7));

        AddNavButton(nav, "HOME", 0);
        AddNavButton(nav, "BRIDGE", 1);
        AddNavButton(nav, "LOGS", 2);
        AddNavButton(nav, "SETTINGS", 3);
        return nav;
    }

    private void AddNavButton(LinearLayout nav, string text, int index)
    {
        var button = new Button(this)
        {
            Text = text,
            TextSize = 11,
            Gravity = GravityFlags.Center
        };
        button.SetTextColor(TextSecondary);
        button.Background = Rounded(Color.White, 14);
        button.Click += (_, _) => ShowPage(index);

        var lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f)
        {
            LeftMargin = Dp(3),
            RightMargin = Dp(3)
        };
        nav.AddView(button, lp);
        _navButtons.Add(button);
    }

    private ScrollView BuildHomePage()
    {
        var (scroll, body) = NewPage();
        body.AddView(NewSectionTitle("Dashboard", "Server status and quick actions"));

        var hero = NewCard(LightBlue, PrimaryBlue);
        var heroLabel = NewText("VOICECRAFT SERVER", 12, PrimaryBlue, true);
        _heroStatus = NewText("STOPPED", 30, TextPrimary, true);
        _heroDetail = NewText("Ready to start", 14, TextSecondary);
        hero.AddView(heroLabel);
        hero.AddView(_heroStatus);
        hero.AddView(_heroDetail);

        var statusRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        _homeClients = NewMetric("0", "Voice clients");
        _homeBridge = NewMetric("OFF", "Bridge");
        statusRow.AddView(_homeClients, Weighted());
        statusRow.AddView(_homeBridge, Weighted());
        hero.AddView(statusRow);
        body.AddView(hero, CardParams());

        var network = NewCard();
        network.AddView(NewCardTitle("Connection"));
        _homeAddress = NewText("Detecting LAN address…", 18, TextPrimary, true);
        _homeAddress.SetTextIsSelectable(true);
        network.AddView(_homeAddress);
        network.AddView(NewText("VoiceCraft client uses UDP on the address above.", 12, TextSecondary));

        var addressButtons = NewButtonRow();
        AddRowButton(addressButtons, NewButton("COPY IP"), (_, _) => CopyIp());
        AddRowButton(addressButtons, NewButton("COPY PORT"), (_, _) => CopyPort());
        AddRowButton(addressButtons, NewButton("COPY ADDRESS", true), (_, _) => CopyAddress());
        network.AddView(addressButtons);
        body.AddView(network, CardParams());

        var bridge = NewCard();
        bridge.AddView(NewCardTitle("Endstone Bridge"));
        _homeRelay = NewText("Not configured", 14, TextPrimary, true);
        _homeRelay.SetTextIsSelectable(true);
        bridge.AddView(_homeRelay);
        bridge.AddView(NewText("Render → WebSocket conversion and plugin config are available in Bridge.", 12, TextSecondary));

        var bridgeButtons = NewButtonRow();
        AddRowButton(bridgeButtons, NewButton("COPY WSS"), (_, _) => CopyWebSocket());
        AddRowButton(bridgeButtons, NewButton("COPY SECRET"), (_, _) => CopyBridgeSecret());
        AddRowButton(bridgeButtons, NewButton("PLUGIN CONFIG", true), (_, _) => CopyPluginConfig());
        bridge.AddView(bridgeButtons);
        body.AddView(bridge, CardParams());

        var controls = NewCard();
        controls.AddView(NewCardTitle("Server Control"));
        var controlRow = NewButtonRow();
        _startButton = NewButton("START SERVER", true);
        _stopButton = NewButton("STOP SERVER", false, true);
        AddRowButton(controlRow, _startButton, (_, _) => StartServer());
        AddRowButton(controlRow, _stopButton, (_, _) => StopServer());
        controls.AddView(controlRow);
        body.AddView(controls, CardParams());

        return scroll;
    }

    private ScrollView BuildBridgePage()
    {
        var (scroll, body) = NewPage();
        body.AddView(NewSectionTitle("Bridge Setup", "Paste once, copy everything ready-to-use"));

        var setup = NewCard();
        setup.AddView(NewCardTitle("Render Relay"));

        _bridgeEnabled = new Android.Widget.Switch(this)
        {
            Text = "Enable Endstone bridge",
            Checked = ServerPreferences.GetBridgeEnabled(this)
        };
        _bridgeEnabled.SetTextColor(TextPrimary);
        _bridgeEnabled.CheckedChange += (_, _) => RefreshBridgePreview();
        setup.AddView(_bridgeEnabled);

        setup.AddView(NewInputLabel("Render Service URL"));
        _renderUrl = NewInput(
            ToServiceUrl(ServerPreferences.GetBridgeUrl(this)),
            InputTypes.ClassText | InputTypes.TextVariationUri);
        _renderUrl.Hint = "https://voicecraft-server-mobile.onrender.com";
        _renderUrl.TextChanged += (_, _) => UpdateGeneratedBridgeUrl();
        setup.AddView(_renderUrl);

        setup.AddView(NewInputLabel("WebSocket URL • generated automatically"));
        _bridgeUrl = NewReadOnlyField("Enter Render URL above");
        setup.AddView(_bridgeUrl);

        var wssButtons = NewButtonRow();
        AddRowButton(wssButtons, NewButton("COPY WEBSOCKET", true), (_, _) => CopyWebSocket());
        setup.AddView(wssButtons);

        setup.AddView(NewInputLabel("Server ID"));
        _bridgeServerId = NewInput(ServerPreferences.GetBridgeServerId(this), InputTypes.ClassText);
        _bridgeServerId.TextChanged += (_, _) => RefreshBridgePreview();
        setup.AddView(_bridgeServerId);

        setup.AddView(NewInputLabel("Bridge Secret"));
        _bridgeSecret = NewInput(
            ServerPreferences.GetBridgeSecret(this),
            InputTypes.ClassText | InputTypes.TextVariationPassword);
        _bridgeSecret.TextChanged += (_, _) => RefreshBridgePreview();
        setup.AddView(_bridgeSecret);

        var secretButtons = NewButtonRow();
        AddRowButton(secretButtons, NewButton("SHOW / HIDE"), (_, _) => ToggleBridgeSecret());
        AddRowButton(secretButtons, NewButton("GENERATE"), (_, _) => GenerateBridgeSecret());
        AddRowButton(secretButtons, NewButton("COPY SECRET", true), (_, _) => CopyBridgeSecret());
        setup.AddView(secretButtons);

        _bridgeReadiness = NewText("○ Setup incomplete", 13, WarningAmber, true);
        _bridgeReadiness.SetPadding(0, Dp(12), 0, 0);
        setup.AddView(_bridgeReadiness);
        body.AddView(setup, CardParams());

        var config = NewCard();
        config.AddView(NewCardTitle("Endstone Plugin Config"));
        config.AddView(NewText(
            "URL and secret are inserted automatically. All other plugin defaults stay ready to paste.",
            12,
            TextSecondary));

        _pluginConfigPreview = NewText(string.Empty, 11, TextPrimary);
        _pluginConfigPreview.Typeface = Typeface.Monospace;
        _pluginConfigPreview.SetTextIsSelectable(true);
        _pluginConfigPreview.SetPadding(Dp(12), Dp(12), Dp(12), Dp(12));
        _pluginConfigPreview.Background = Rounded(PageBackground, 12, BorderColor);
        config.AddView(_pluginConfigPreview, MarginTop(Dp(12)));

        var configButtons = NewButtonRow();
        AddRowButton(configButtons, NewButton("COPY PLUGIN CONFIG", true), (_, _) => CopyPluginConfig());
        AddRowButton(configButtons, NewButton("COPY ALL SETUP"), (_, _) => CopyBridgeSetup());
        config.AddView(configButtons);
        body.AddView(config, CardParams());

        var flow = NewCard();
        flow.AddView(NewCardTitle("Connection Flow"));
        flow.AddView(NewText("Minecraft / MCSV", 14, TextPrimary, true));
        flow.AddView(NewText("↓  Endstone v0.2.x", 13, PrimaryBlue));
        flow.AddView(NewText("↓  Render WSS Relay", 13, PrimaryBlue));
        flow.AddView(NewText("↓  VoiceCraft Server Android", 13, PrimaryBlue));
        flow.AddView(NewText("Voice audio still uses UDP to the Android server.", 12, TextSecondary));
        body.AddView(flow, CardParams());

        return scroll;
    }

    private ScrollView BuildLogsPage()
    {
        var (scroll, body) = NewPage();
        body.AddView(NewSectionTitle("Diagnostics", "Runtime, McHttp and bridge activity"));

        var logCard = NewCard();
        logCard.AddView(NewCardTitle("Runtime Log"));
        _logView = NewText("(no log entries yet)", 11, TextPrimary);
        _logView.Typeface = Typeface.Monospace;
        _logView.SetMinHeight(Dp(340));
        _logView.SetTextIsSelectable(true);
        _logView.SetPadding(Dp(12), Dp(12), Dp(12), Dp(12));
        _logView.Background = Rounded(PageBackground, 12, BorderColor);
        logCard.AddView(_logView);

        var logButtons = NewButtonRow();
        AddRowButton(logButtons, NewButton("COPY LOG", true), (_, _) => CopyLog());
        AddRowButton(logButtons, NewButton("CLEAR LOG"), (_, _) =>
        {
            AndroidRuntimeLog.Clear();
            AndroidRuntimeLog.Append("UI", "Log cleared");
            RefreshLog(true);
        });
        logCard.AddView(logButtons);
        body.AddView(logCard, CardParams());

        var privacy = NewCard(LightBlue, BorderColor);
        privacy.AddView(NewCardTitle("Privacy"));
        privacy.AddView(NewText(
            "Bridge secret, login token and VoiceCraft binding keys are intentionally hidden from runtime logs.",
            12,
            TextSecondary));
        body.AddView(privacy, CardParams());
        return scroll;
    }

    private ScrollView BuildSettingsPage()
    {
        var (scroll, body) = NewPage();
        body.AddView(NewSectionTitle("Settings", "Voice server and legacy McHttp options"));

        var server = NewCard();
        server.AddView(NewCardTitle("Voice Server"));
        server.AddView(NewInputLabel("Voice / McHttp Port"));
        _port = NewInput(ServerPreferences.GetVoicePort(this).ToString(), InputTypes.ClassNumber);
        server.AddView(_port);

        var portButtons = NewButtonRow();
        AddRowButton(portButtons, NewButton("COPY PORT"), (_, _) => CopyPort());
        AddRowButton(portButtons, NewButton("COPY IP:PORT", true), (_, _) => CopyAddress());
        server.AddView(portButtons);
        body.AddView(server, CardParams());

        var security = NewCard();
        security.AddView(NewCardTitle("Legacy McHttp Server Key"));
        security.AddView(NewText(
            "This key is separate from the Bridge Secret. Endstone Phase 2 normally uses the bridge instead of McHttp.",
            12,
            TextSecondary));
        _serverKey = NewInput(
            ServerPreferences.GetServerKey(this),
            InputTypes.ClassText | InputTypes.TextVariationPassword);
        security.AddView(_serverKey, MarginTop(Dp(10)));

        var keyButtons = NewButtonRow();
        AddRowButton(keyButtons, NewButton("SHOW / HIDE"), (_, _) => ToggleServerKey());
        AddRowButton(keyButtons, NewButton("GENERATE"), (_, _) => GenerateServerKey());
        AddRowButton(keyButtons, NewButton("COPY KEY", true), (_, _) => CopyServerKey());
        security.AddView(keyButtons);
        body.AddView(security, CardParams());

        var save = NewCard(LightBlue, BorderColor);
        save.AddView(NewCardTitle("Save Configuration"));
        save.AddView(NewText("Settings are also saved automatically when the server starts or the app leaves the foreground.", 12, TextSecondary));
        var saveButton = NewButton("SAVE SETTINGS", true);
        saveButton.Click += (_, _) => SaveCurrentPreferences(true);
        save.AddView(saveButton, MarginTop(Dp(12)));
        body.AddView(save, CardParams());
        return scroll;
    }

    private (ScrollView Scroll, LinearLayout Body) NewPage()
    {
        var scroll = new ScrollView(this)
        {
            FillViewport = true,
            Background = Solid(PageBackground)
        };
        var body = new LinearLayout(this) { Orientation = Orientation.Vertical };
        body.SetPadding(Dp(18), Dp(18), Dp(18), Dp(28));
        scroll.AddView(body);
        return (scroll, body);
    }

    private LinearLayout NewCard(Color? fill = null, Color? stroke = null)
    {
        var card = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Background = Rounded(fill ?? CardBackground, 18, stroke ?? BorderColor)
        };
        card.SetPadding(Dp(16), Dp(16), Dp(16), Dp(16));
        card.Elevation = Dp(1);
        return card;
    }

    private View NewSectionTitle(string title, string subtitle)
    {
        var box = new LinearLayout(this) { Orientation = Orientation.Vertical };
        box.AddView(NewText(title, 24, TextPrimary, true));
        box.AddView(NewText(subtitle, 13, TextSecondary));
        var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            BottomMargin = Dp(16)
        };
        box.LayoutParameters = lp;
        return box;
    }

    private TextView NewCardTitle(string text)
    {
        var title = NewText(text, 16, TextPrimary, true);
        title.SetPadding(0, 0, 0, Dp(10));
        return title;
    }

    private TextView NewText(string text, float size, Color color, bool bold = false)
    {
        var view = new TextView(this)
        {
            Text = text,
            TextSize = size
        };
        view.SetTextColor(color);
        if (bold)
            view.SetTypeface(Typeface.Default, TypefaceStyle.Bold);
        return view;
    }

    private TextView NewMetric(string value, string label)
    {
        var box = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Gravity = GravityFlags.Center
        };
        box.SetPadding(Dp(8), Dp(14), Dp(8), Dp(4));
        var valueView = NewText(value, 18, TextPrimary, true);
        valueView.Gravity = GravityFlags.Center;
        var labelView = NewText(label, 11, TextSecondary);
        labelView.Gravity = GravityFlags.Center;
        box.AddView(valueView);
        box.AddView(labelView);

        // Returning the value view lets RefreshUi update it while the parent remains the metric tile.
        valueView.Tag = box;
        return valueView;
    }

    private TextView NewInputLabel(string text)
    {
        var label = NewText(text, 12, TextSecondary, true);
        label.SetPadding(0, Dp(12), 0, Dp(6));
        return label;
    }

    private EditText NewInput(string text, InputTypes type)
    {
        var input = new EditText(this)
        {
            Text = text,
            TextSize = 14,
            InputType = type,
            SingleLine = true,
            Background = Rounded(Color.White, 12, BorderColor)
        };
        input.SetTextColor(TextPrimary);
        input.SetHintTextColor(TextSecondary);
        input.SetPadding(Dp(12), Dp(9), Dp(12), Dp(9));
        input.LayoutParameters = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            Dp(48));
        return input;
    }

    private TextView NewReadOnlyField(string text)
    {
        var field = NewText(text, 14, TextPrimary);
        field.SetTextIsSelectable(true);
        field.Gravity = GravityFlags.CenterVertical;
        field.SetPadding(Dp(12), Dp(8), Dp(12), Dp(8));
        field.Background = Rounded(PageBackground, 12, BorderColor);
        field.LayoutParameters = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            Dp(48));
        return field;
    }

    private Button NewButton(string text, bool primary = false, bool danger = false)
    {
        var fill = primary ? PrimaryBlue : danger ? Color.Rgb(254, 242, 242) : Color.White;
        var stroke = primary ? PrimaryBlue : danger ? ErrorRed : BorderColor;
        var color = primary ? Color.White : danger ? ErrorRed : PrimaryBlue;
        var button = new Button(this)
        {
            Text = text,
            TextSize = 11,
            Gravity = GravityFlags.Center,
            Background = Rounded(fill, 12, stroke)
        };
        button.SetTextColor(color);
        return button;
    }

    private LinearLayout NewButtonRow()
    {
        var row = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            Gravity = GravityFlags.CenterVertical
        };
        row.SetPadding(0, Dp(12), 0, 0);
        return row;
    }

    private void AddRowButton(LinearLayout row, Button button, EventHandler handler)
    {
        button.Click += handler;
        var lp = new LinearLayout.LayoutParams(0, Dp(46), 1f)
        {
            LeftMargin = Dp(3),
            RightMargin = Dp(3)
        };
        row.AddView(button, lp);
    }

    private LinearLayout.LayoutParams Weighted() =>
        new(0, ViewGroup.LayoutParams.WrapContent, 1f);

    private LinearLayout.LayoutParams CardParams() =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            BottomMargin = Dp(12)
        };

    private LinearLayout.LayoutParams MarginTop(int top) =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = top
        };

    private GradientDrawable Rounded(Color fill, int radiusDp, Color? stroke = null)
    {
        var drawable = new GradientDrawable();
        drawable.SetColor(fill);
        drawable.SetCornerRadius(Dp(radiusDp));
        if (stroke.HasValue)
            drawable.SetStroke(Dp(1), stroke.Value);
        return drawable;
    }

    private static ColorDrawable Solid(Color color) => new(color);

    private void ShowPage(int index)
    {
        if (index < 0 || index >= _pages.Count)
            return;
        _selectedPage = index;
        for (var i = 0; i < _pages.Count; i++)
            _pages[i].Visibility = i == index ? ViewStates.Visible : ViewStates.Gone;

        for (var i = 0; i < _navButtons.Count; i++)
        {
            var selected = i == index;
            _navButtons[i].SetTextColor(selected ? PrimaryBlue : TextSecondary);
            _navButtons[i].Background = Rounded(selected ? LightBlue : Color.White, 14);
        }

        if (index == 2)
            RefreshLog(true);
    }

    private void UpdateGeneratedBridgeUrl()
    {
        if (_bridgeUrl == null)
            return;

        var input = _renderUrl?.Text?.Trim() ?? string.Empty;
        var generated = BuildWebSocketUrl(input);
        if (string.IsNullOrWhiteSpace(generated))
        {
            _bridgeUrl.Text = "Enter a valid Render service URL";
            _bridgeUrl.SetTextColor(TextSecondary);
        }
        else
        {
            _bridgeUrl.Text = generated;
            _bridgeUrl.SetTextColor(TextPrimary);
        }
        RefreshBridgePreview();
    }

    private void RefreshBridgePreview()
    {
        var url = CurrentBridgeUrl();
        var serverId = _bridgeServerId?.Text?.Trim() ?? string.Empty;
        var secret = _bridgeSecret?.Text?.Trim() ?? string.Empty;
        var ready = ValidateBridge(url, serverId, secret, out _);

        if (_bridgeReadiness != null)
        {
            _bridgeReadiness.Text = ready
                ? "● Configuration ready — WebSocket, secret and plugin config are ready to copy"
                : "○ Setup incomplete — enter a valid Render URL, Server ID and 16+ character secret";
            _bridgeReadiness.SetTextColor(ready ? SuccessGreen : WarningAmber);
        }

        if (_pluginConfigPreview != null)
            _pluginConfigPreview.Text = BuildPluginConfig(maskSecret: true);
    }

    private static string BuildWebSocketUrl(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        input = input.Trim();
        if (!input.Contains("://", StringComparison.Ordinal))
            input = "https://" + input;

        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri))
            return string.Empty;

        var scheme = uri.Scheme.ToLowerInvariant() switch
        {
            "https" => "wss",
            "http" => "ws",
            "wss" => "wss",
            "ws" => "ws",
            _ => string.Empty
        };
        if (string.IsNullOrEmpty(scheme) || string.IsNullOrWhiteSpace(uri.Host))
            return string.Empty;

        var builder = new UriBuilder(uri)
        {
            Scheme = scheme,
            Path = "/bridge",
            Query = string.Empty,
            Fragment = string.Empty
        };

        if ((scheme == "wss" && uri.IsDefaultPort) || (scheme == "ws" && uri.IsDefaultPort))
            builder.Port = -1;

        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }

    private static string ToServiceUrl(string bridgeUrl)
    {
        if (string.IsNullOrWhiteSpace(bridgeUrl) ||
            bridgeUrl.Contains("YOUR-RELAY", StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(bridgeUrl, UriKind.Absolute, out var uri))
            return string.Empty;

        var scheme = uri.Scheme.ToLowerInvariant() switch
        {
            "wss" => "https",
            "ws" => "http",
            "https" => "https",
            "http" => "http",
            _ => "https"
        };
        var builder = new UriBuilder(uri)
        {
            Scheme = scheme,
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty
        };
        if (uri.IsDefaultPort)
            builder.Port = -1;
        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }

    private string CurrentBridgeUrl()
    {
        var text = _bridgeUrl?.Text?.Trim() ?? string.Empty;
        return text.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)
            ? text
            : string.Empty;
    }

    private string BuildPluginConfig(bool maskSecret)
    {
        var url = CurrentBridgeUrl();
        var serverId = _bridgeServerId?.Text?.Trim() ?? ServerPreferences.GetBridgeServerId(this);
        var secret = _bridgeSecret?.Text?.Trim() ?? ServerPreferences.GetBridgeSecret(this);
        if (maskSecret && !string.IsNullOrEmpty(secret))
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
            $"url = \"{TomlEscape(url)}\"\n" +
            $"server_id = \"{TomlEscape(serverId)}\"\n" +
            $"secret = \"{TomlEscape(secret)}\"\n" +
            "reconnect_seconds = 5\n";
    }

    private static string TomlEscape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal);

    private void StartServer()
    {
        if (!int.TryParse(_port?.Text, out var port) || port is < 1 or > 65535)
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
        var bridgeUrl = CurrentBridgeUrl();
        var bridgeServerId = _bridgeServerId?.Text?.Trim() ?? string.Empty;
        var bridgeSecret = _bridgeSecret?.Text?.Trim() ?? string.Empty;

        if (bridgeEnabled && !ValidateBridge(bridgeUrl, bridgeServerId, bridgeSecret, out var bridgeError))
        {
            Toast.MakeText(this, bridgeError, ToastLength.Long)?.Show();
            ShowPage(1);
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

    private void SaveCurrentPreferences(bool showToast)
    {
        var port = ServerPreferences.GetVoicePort(this);
        if (int.TryParse(_port?.Text, out var parsed) && parsed is >= 1 and <= 65535)
            port = parsed;

        var key = _serverKey?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            key = Guid.NewGuid().ToString("N");
            if (_serverKey != null)
                _serverKey.Text = key;
        }

        ServerPreferences.Save(this, port, key);
        ServerPreferences.SaveBridge(
            this,
            _bridgeEnabled?.Checked == true,
            CurrentBridgeUrl(),
            _bridgeServerId?.Text?.Trim() ?? "mcsv-main",
            _bridgeSecret?.Text?.Trim() ?? string.Empty);

        if (showToast)
            Toast.MakeText(this, "Settings saved", ToastLength.Short)?.Show();
    }

    private void RefreshUi()
    {
        var ip = GetLanIpv4() ?? "No LAN IPv4";
        var port = int.TryParse(_port?.Text, out var uiPort) && uiPort is >= 1 and <= 65535
            ? uiPort
            : ServerPreferences.GetVoicePort(this);
        var running = VcServerApp.IsRunning;
        var service = VoiceCraftServerService.IsServiceRunning;
        var bridgeStatus = VoiceCraftServerService.BridgeStatus;
        var bridgeUrl = CurrentBridgeUrl();

        if (_heroStatus != null)
        {
            if (VoiceCraftServerService.LastError != null)
            {
                _heroStatus.Text = "ERROR";
                _heroStatus.SetTextColor(ErrorRed);
                _heroDetail!.Text = "Open Logs for details";
            }
            else if (running)
            {
                _heroStatus.Text = "RUNNING";
                _heroStatus.SetTextColor(SuccessGreen);
                _heroDetail!.Text = $"UDP/TCP {port} • Foreground service active";
            }
            else if (service)
            {
                _heroStatus.Text = "STARTING";
                _heroStatus.SetTextColor(WarningAmber);
                _heroDetail!.Text = $"Preparing VoiceCraft on port {port}";
            }
            else
            {
                _heroStatus.Text = "STOPPED";
                _heroStatus.SetTextColor(TextPrimary);
                _heroDetail!.Text = "Ready to start";
            }
        }

        if (_homeAddress != null)
            _homeAddress.Text = $"{ip}:{port}";
        if (_homeClients != null)
            _homeClients.Text = VcServerApp.ConnectedClients.ToString();
        if (_homeBridge != null)
            _homeBridge.Text = BridgeShortStatus(bridgeStatus);
        if (_homeRelay != null)
            _homeRelay.Text = string.IsNullOrWhiteSpace(bridgeUrl) ? "Relay not configured" : bridgeUrl;

        if (_startButton != null)
            _startButton.Enabled = !running && !service;
        if (_stopButton != null)
            _stopButton.Enabled = running || service;

        RefreshBridgePreview();
        RefreshLog();
    }

    private static string BridgeShortStatus(string status) => status switch
    {
        "endstone-connected" => "ONLINE",
        "relay-only" => "RELAY",
        "starting" => "STARTING",
        "disabled" => "OFF",
        "invalid-config" => "CONFIG",
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

    private void CopyIp()
    {
        var ip = GetLanIpv4() ?? "0.0.0.0";
        CopyText("LAN IP", ip);
    }

    private void CopyPort()
    {
        var port = int.TryParse(_port?.Text, out var value) && value is >= 1 and <= 65535
            ? value
            : ServerPreferences.GetVoicePort(this);
        CopyText("VoiceCraft port", port.ToString());
    }

    private void CopyAddress()
    {
        var ip = GetLanIpv4() ?? "0.0.0.0";
        var port = int.TryParse(_port?.Text, out var value) && value is >= 1 and <= 65535
            ? value
            : ServerPreferences.GetVoicePort(this);
        CopyText("VoiceCraft address", $"{ip}:{port}");
    }

    private void CopyWebSocket()
    {
        var url = CurrentBridgeUrl();
        if (string.IsNullOrWhiteSpace(url))
        {
            Toast.MakeText(this, "Enter a valid Render URL first", ToastLength.Short)?.Show();
            return;
        }
        CopyText("VoiceCraft WebSocket", url);
    }

    private void CopyBridgeSecret()
    {
        var secret = _bridgeSecret?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(secret))
        {
            Toast.MakeText(this, "Bridge secret is empty", ToastLength.Short)?.Show();
            return;
        }
        CopyText("VoiceCraft Bridge Secret", secret, true);
    }

    private void CopyServerKey()
    {
        var key = _serverKey?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            Toast.MakeText(this, "Server key is empty", ToastLength.Short)?.Show();
            return;
        }
        CopyText("VoiceCraft Server Key", key, true);
    }

    private void CopyPluginConfig()
    {
        var url = CurrentBridgeUrl();
        var serverId = _bridgeServerId?.Text?.Trim() ?? string.Empty;
        var secret = _bridgeSecret?.Text?.Trim() ?? string.Empty;
        if (!ValidateBridge(url, serverId, secret, out var error))
        {
            Toast.MakeText(this, error, ToastLength.Long)?.Show();
            ShowPage(1);
            return;
        }
        CopyText("VoiceCraft Endstone config.toml", BuildPluginConfig(maskSecret: false), true);
    }

    private void CopyBridgeSetup()
    {
        var url = CurrentBridgeUrl();
        var serverId = _bridgeServerId?.Text?.Trim() ?? string.Empty;
        var secret = _bridgeSecret?.Text?.Trim() ?? string.Empty;
        if (!ValidateBridge(url, serverId, secret, out var error))
        {
            Toast.MakeText(this, error, ToastLength.Long)?.Show();
            return;
        }

        var text =
            "Render Environment Variable:\n" +
            $"BRIDGE_SECRET={secret}\n\n" +
            "Android Bridge:\n" +
            $"WebSocket={url}\n" +
            $"Server ID={serverId}\n" +
            $"Bridge Secret={secret}\n\n" +
            "Endstone config.toml:\n" +
            BuildPluginConfig(maskSecret: false);
        CopyText("VoiceCraft complete bridge setup", text, true);
    }

    private void CopyLog()
    {
        var text = AndroidRuntimeLog.Snapshot();
        if (string.IsNullOrWhiteSpace(text))
        {
            Toast.MakeText(this, "Log is empty", ToastLength.Short)?.Show();
            return;
        }
        CopyText("VoiceCraft Server log", text);
    }

    private void CopyText(string label, string text, bool containsSecret = false)
    {
        if (GetSystemService(ClipboardService) is not global::Android.Content.ClipboardManager clipboard)
            return;

        clipboard.PrimaryClip = ClipData.NewPlainText(label, text);
        Toast.MakeText(
            this,
            containsSecret ? "Copied — clipboard contains a secret" : "Copied",
            containsSecret ? ToastLength.Long : ToastLength.Short)?.Show();
    }

    private void GenerateBridgeSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        var secret = Convert.ToHexString(bytes).ToLowerInvariant();
        if (_bridgeSecret != null)
        {
            _bridgeSecret.Text = secret;
            _bridgeSecret.SetSelection(secret.Length);
        }
        RefreshBridgePreview();
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
            error = "Paste a valid Render URL so the app can generate ws:// or wss://";
            return false;
        }
        if (!string.Equals(uri.AbsolutePath.TrimEnd('/'), "/bridge", StringComparison.OrdinalIgnoreCase))
        {
            error = "WebSocket path must be /bridge";
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
        SaveCurrentPreferences(false);
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
