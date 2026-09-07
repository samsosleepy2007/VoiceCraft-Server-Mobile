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
    private static readonly Color Green = Color.Rgb(34, 197, 94);
    private static readonly Color Amber = Color.Rgb(245, 158, 11);
    private static readonly Color Red = Color.Rgb(239, 68, 68);

    private bool _thai;
    private bool _dark;
    private float _density = 1f;
    private int _currentPage;

    private Color Page => _dark ? Color.Rgb(15, 23, 42) : Color.Rgb(247, 250, 252);
    private Color CardFill => _dark ? Color.Rgb(30, 41, 59) : Color.White;
    private Color FieldFill => _dark ? Color.Rgb(23, 32, 49) : Color.White;
    private Color LightBlue => _dark ? Color.Rgb(20, 47, 78) : Color.Rgb(234, 244, 255);
    private Color Ink => _dark ? Color.Rgb(241, 245, 249) : Color.Rgb(31, 41, 55);
    private Color Muted => _dark ? Color.Rgb(148, 163, 184) : Color.Rgb(107, 114, 128);
    private Color Border => _dark ? Color.Rgb(51, 65, 85) : Color.Rgb(220, 230, 242);
    private Color DangerFill => _dark ? Color.Rgb(58, 23, 28) : Color.Rgb(254, 242, 242);

    private readonly List<View> _pages = new();
    private readonly List<Button> _navButtons = new();

    private TextView? _statusTitle;
    private TextView? _statusDetail;
    private TextView? _address;
    private TextView? _clientCount;
    private TextView? _bridgeState;
    private TextView? _relaySummary;
    private LinearLayout? _errorCard;
    private TextView? _errorText;
    private Button? _start;
    private Button? _stop;

    private ScrollView? _bridgeScroll;
    private EditText? _renderUrl;
    private TextView? _webSocketUrl;
    private EditText? _serverId;
    private EditText? _bridgeSecret;
    private TextView? _readiness;
    private TextView? _configPreview;
    private bool _bridgeSecretVisible;

    private ScrollView? _settingsScroll;
    private EditText? _port;
    private EditText? _serverKey;
    private bool _serverKeyVisible;

    private TextView? _logView;
    private Handler? _handler;
    private IRunnable? _refreshRunnable;
    private long _renderedLogVersion = -1;
    private string _shownRuntimeError = string.Empty;
    private string _shownBridgeError = string.Empty;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ActionBar?.Hide();
        _density = Resources?.DisplayMetrics?.Density ?? 1f;
        _thai = ServerPreferences.GetLanguage(this) == "th";
        _dark = ServerPreferences.GetDarkTheme(this);
        ApplySystemBars();
        BuildUi(0);
        RequestNotificationPermission();
        StartRefreshLoop();
        AndroidRuntimeLog.Append("UI", "MainActivity opened");
    }

    private string T(string thai, string english) => _thai ? thai : english;
    private int Dp(int value) => (int)(value * _density + 0.5f);

    private void ApplySystemBars()
    {
        Window?.SetStatusBarColor(_dark ? Color.Rgb(10, 18, 32) : Blue);
        Window?.SetNavigationBarColor(_dark ? Color.Rgb(15, 23, 42) : Color.White);
    }

    private void BuildUi(int selectedPage)
    {
        _pages.Clear();
        _navButtons.Clear();
        _renderedLogVersion = -1;

        var shell = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Background = Solid(Page)
        };
        shell.AddView(BuildHeader(), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(118)));

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
        ShowPage(Math.Clamp(selectedPage, 0, _pages.Count - 1));
        UpdateWebSocketFromRenderUrl();
        RefreshUi();
        RefreshLog(true);
    }

    private View BuildHeader()
    {
        var root = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Background = Solid(CardFill)
        };
        root.SetPadding(Dp(16), Dp(8), Dp(16), Dp(8));

        var titleRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        titleRow.SetGravity(GravityFlags.CenterVertical);
        var title = new LinearLayout(this) { Orientation = Orientation.Vertical };
        title.AddView(Label("VoiceCraft Server", 20, Ink, true));
        title.AddView(Label("Android • VoiceCraft 1.7.1", 11, Muted));
        titleRow.AddView(title, new LinearLayout.LayoutParams(0, Dp(50), 1f));

        var by = Label("By SamSoSleepy", 11, Blue, true);
        by.Gravity = GravityFlags.Right | GravityFlags.CenterVertical;
        titleRow.AddView(by, new LinearLayout.LayoutParams(Dp(118), Dp(50)));
        root.AddView(titleRow);

        var tools = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        tools.SetGravity(GravityFlags.CenterVertical);

        var language = MakeButton(_thai ? "ENGLISH" : "ไทย");
        WireButton(language, ToggleLanguage);
        tools.AddView(language, HeaderWeight());

        var theme = MakeButton(_dark ? T("ธีมสว่าง", "LIGHT") : T("ธีมมืด", "DARK"));
        WireButton(theme, ToggleTheme);
        tools.AddView(theme, HeaderWeight());

        var info = MakeButton(T("วิธีใช้", "INFO"));
        WireButton(info, ShowInformation);
        tools.AddView(info, HeaderWeight());

        root.AddView(tools, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(50)));
        return root;
    }

    private LinearLayout.LayoutParams HeaderWeight() => new(0, Dp(46), 1f)
    {
        LeftMargin = Dp(3),
        RightMargin = Dp(3)
    };

    private View BuildNav()
    {
        var nav = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            Background = Solid(CardFill)
        };
        nav.SetGravity(GravityFlags.Center);
        nav.SetPadding(Dp(7), Dp(7), Dp(7), Dp(7));
        AddNav(nav, T("หน้าหลัก", "HOME"), 0);
        AddNav(nav, T("บริดจ์", "BRIDGE"), 1);
        AddNav(nav, T("ล็อก", "LOGS"), 2);
        AddNav(nav, T("ตั้งค่า", "SETTINGS"), 3);
        return nav;
    }

    private void AddNav(LinearLayout nav, string name, int index)
    {
        var button = MakeButton(name);
        WireButton(button, () => ShowPage(index));
        nav.AddView(button, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f)
        {
            LeftMargin = Dp(3),
            RightMargin = Dp(3)
        });
        _navButtons.Add(button);
    }

    private ScrollView BuildHome()
    {
        var (scroll, body) = NewPage(
            T("แดชบอร์ด", "Dashboard"),
            T("สถานะเซิร์ฟเวอร์และคำสั่งที่ใช้บ่อย", "Server status and quick actions"));

        var hero = Card(LightBlue, Border);
        hero.AddView(Label("VOICECRAFT SERVER", 12, Blue, true));
        _statusTitle = Label(T("หยุดอยู่", "STOPPED"), 30, Ink, true);
        _statusDetail = Label(T("พร้อมเริ่มเซิร์ฟเวอร์", "Ready to start"), 13, Muted);
        hero.AddView(_statusTitle);
        hero.AddView(_statusDetail);

        var metrics = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        metrics.AddView(Metric("0", T("Voice Clients", "Voice clients"), out _clientCount), Weight());
        metrics.AddView(Metric(T("ออฟไลน์", "OFFLINE"), T("บริดจ์", "Bridge"), out _bridgeState), Weight());
        hero.AddView(metrics);
        body.AddView(hero, CardLayout());

        _errorCard = Card(DangerFill, Red);
        _errorCard.Visibility = ViewStates.Gone;
        _errorCard.AddView(Label(T("พบข้อผิดพลาด", "Error detected"), 16, Red, true));
        _errorText = Label(string.Empty, 12, Ink);
        _errorCard.AddView(_errorText);
        var errorButtons = ButtonRow();
        AddButton(errorButtons, T("ไปที่ Log", "OPEN LOGS"), () => ShowPage(2), true);
        _errorCard.AddView(errorButtons);
        body.AddView(_errorCard, CardLayout());

        var connection = Card();
        connection.AddView(CardTitle(T("การเชื่อมต่อ", "Connection")));
        _address = Label(T("กำลังค้นหา LAN IP…", "Detecting LAN address…"), 19, Ink, true);
        _address.SetTextIsSelectable(true);
        connection.AddView(_address);
        connection.AddView(Label(
            T("VoiceCraft Client ใช้ IP:Port นี้เพื่อเชื่อมต่อ UDP", "VoiceCraft Client connects to this UDP address."),
            12,
            Muted));
        var connButtons = ButtonRow();
        AddButton(connButtons, T("คัดลอก IP", "COPY IP"), CopyIp);
        AddButton(connButtons, T("คัดลอก Port", "COPY PORT"), CopyPort);
        AddButton(connButtons, T("คัดลอก IP:Port", "COPY ADDRESS"), CopyAddress, true);
        connection.AddView(connButtons);
        body.AddView(connection, CardLayout());

        var bridge = Card();
        bridge.AddView(CardTitle("Render / Endstone Bridge"));
        bridge.AddView(Label(
            T("จำเป็นต้องตั้งค่า Render Relay ให้ครบก่อนเริ่ม Server", "Render Relay setup is required before the server can start."),
            12,
            Red,
            true));
        _relaySummary = Label(T("ยังไม่ได้ตั้งค่า Relay", "Relay not configured"), 14, Ink, true);
        _relaySummary.SetTextIsSelectable(true);
        _relaySummary.SetPadding(0, Dp(8), 0, 0);
        bridge.AddView(_relaySummary);
        var bridgeButtons = ButtonRow();
        AddButton(bridgeButtons, T("ไปตั้งค่า", "SET UP"), () => ShowPage(1), true);
        AddButton(bridgeButtons, T("คัดลอก WSS", "COPY WSS"), CopyWebSocket);
        AddButton(bridgeButtons, T("Plugin Config", "PLUGIN CONFIG"), CopyPluginConfig);
        bridge.AddView(bridgeButtons);
        body.AddView(bridge, CardLayout());

        var control = Card();
        control.AddView(CardTitle(T("ควบคุมเซิร์ฟเวอร์", "Server Control")));
        control.AddView(Label(
            T("เมื่อกดเริ่ม ระบบจะตรวจ Render URL, WebSocket, Server ID, Secret และ Port ก่อนทุกครั้ง", "Start always checks Render URL, WebSocket, Server ID, Secret and Port first."),
            12,
            Muted));
        var controls = ButtonRow();
        _start = MakeButton(T("เริ่มเซิร์ฟเวอร์", "START SERVER"), true);
        _stop = MakeButton(T("หยุดเซิร์ฟเวอร์", "STOP SERVER"), false, true);
        WireButton(_start, StartServer);
        WireButton(_stop, StopServer);
        controls.AddView(_start, Weight(Dp(50)));
        controls.AddView(_stop, Weight(Dp(50)));
        control.AddView(controls);
        body.AddView(control, CardLayout());
        return scroll;
    }

    private ScrollView BuildBridge()
    {
        var (scroll, body) = NewPage(
            T("ตั้งค่า Bridge", "Bridge Setup"),
            T("Render Relay เป็นค่าบังคับก่อนเริ่ม VoiceCraft Server", "Render Relay is required before VoiceCraft Server can start"));
        _bridgeScroll = scroll;

        var required = Card(DangerFill, Red);
        required.AddView(Label(T("● จำเป็นก่อนเริ่ม Server", "● REQUIRED BEFORE START"), 14, Red, true));
        required.AddView(Label(
            T("หาก URL, Server ID หรือ Secret ยังไม่ครบ ปุ่มเริ่ม Server จะหยุดการเริ่มและแสดงจุดที่ต้องแก้", "If URL, Server ID or Secret is incomplete, Start Server is blocked and shows exactly what to fix."),
            12,
            Ink));
        body.AddView(required, CardLayout());

        var setup = Card();
        setup.AddView(CardTitle("Render Relay"));

        setup.AddView(InputLabel("Render Service URL"));
        _renderUrl = Input(ToServiceUrl(ServerPreferences.GetBridgeUrl(this)), InputTypes.ClassText | InputTypes.TextVariationUri);
        _renderUrl.Hint = "https://voicecraft-server-mobile.onrender.com";
        _renderUrl.TextChanged += (_, _) => UpdateWebSocketFromRenderUrl();
        setup.AddView(_renderUrl);
        var renderButtons = ButtonRow();
        AddButton(renderButtons, T("เปิด Render", "OPEN RENDER"), OpenRender);
        AddButton(renderButtons, T("คัดลอก WebSocket", "COPY WEBSOCKET"), CopyWebSocket, true);
        setup.AddView(renderButtons);

        setup.AddView(InputLabel(T("WebSocket URL • สร้างอัตโนมัติ", "WebSocket URL • auto generated")));
        _webSocketUrl = ReadOnly(T("ใส่ Render URL ด้านบน", "Enter Render URL above"));
        setup.AddView(_webSocketUrl);

        setup.AddView(InputLabel("Server ID"));
        _serverId = Input(ServerPreferences.GetBridgeServerId(this), InputTypes.ClassText);
        _serverId.Hint = "mcsv-main";
        _serverId.TextChanged += (_, _) => RefreshBridgePreview();
        setup.AddView(_serverId);

        setup.AddView(InputLabel("Bridge Secret"));
        _bridgeSecret = Input(ServerPreferences.GetBridgeSecret(this), InputTypes.ClassText | InputTypes.TextVariationPassword);
        _bridgeSecret.Hint = T("ต้องตรงกับ BRIDGE_SECRET บน Render", "Must match BRIDGE_SECRET on Render");
        _bridgeSecret.TextChanged += (_, _) => RefreshBridgePreview();
        setup.AddView(_bridgeSecret);
        var secretButtons = ButtonRow();
        AddButton(secretButtons, T("แสดง/ซ่อน", "SHOW / HIDE"), ToggleBridgeSecret);
        AddButton(secretButtons, T("สร้าง Secret", "GENERATE"), GenerateBridgeSecret);
        AddButton(secretButtons, T("คัดลอก Secret", "COPY SECRET"), CopyBridgeSecret, true);
        setup.AddView(secretButtons);

        _readiness = Label(T("● ต้องตั้งค่าให้ครบก่อนเริ่ม", "● Setup required before start"), 13, Red, true);
        _readiness.SetPadding(0, Dp(12), 0, 0);
        setup.AddView(_readiness);
        body.AddView(setup, CardLayout());

        var config = Card();
        config.AddView(CardTitle(T("Endstone Plugin Config พร้อมวาง", "Ready-to-paste Endstone Plugin Config")));
        config.AddView(Label(
            T("URL และ Secret จะถูกใส่ให้อัตโนมัติ ค่าอื่นใช้ค่าที่ทดสอบแล้ว", "URL and secret are inserted automatically. Other values stay at tested defaults."),
            12,
            Muted));
        _configPreview = Label(string.Empty, 11, Ink);
        _configPreview.Typeface = Typeface.Monospace;
        _configPreview.SetTextIsSelectable(true);
        _configPreview.SetPadding(Dp(12), Dp(12), Dp(12), Dp(12));
        _configPreview.Background = Round(Page, 12, Border);
        config.AddView(_configPreview, Top(Dp(12)));
        var configButtons = ButtonRow();
        AddButton(configButtons, T("คัดลอก Plugin Config", "COPY PLUGIN CONFIG"), CopyPluginConfig, true);
        AddButton(configButtons, T("คัดลอกทั้งหมด", "COPY ALL SETUP"), CopyAllSetup);
        config.AddView(configButtons);
        body.AddView(config, CardLayout());

        var flow = Card(LightBlue, Border);
        flow.AddView(CardTitle(T("เส้นทางการเชื่อมต่อ", "Connection Flow")));
        flow.AddView(Label("Minecraft / MCSV", 14, Ink, true));
        flow.AddView(Label("↓  Endstone v0.2.x", 13, Blue));
        flow.AddView(Label("↓  Render WebSocket Relay", 13, Blue));
        flow.AddView(Label("↓  VoiceCraft Server Android", 13, Blue));
        flow.AddView(Label(
            T("หมายเหตุ: เสียงยังใช้ UDP ไปยัง Android โดยตรง", "Note: Voice audio still uses UDP to Android directly."),
            12,
            Muted));
        body.AddView(flow, CardLayout());
        return scroll;
    }

    private ScrollView BuildLogs()
    {
        var (scroll, body) = NewPage(
            T("วิเคราะห์ระบบ", "Diagnostics"),
            T("Log ของ Runtime, McHttp, Voice และ Bridge", "Runtime, McHttp, voice and bridge activity"));
        var card = Card();
        card.AddView(CardTitle("Runtime Log"));
        _logView = Label(T("(ยังไม่มี Log)", "(no log entries yet)"), 11, Ink);
        _logView.Typeface = Typeface.Monospace;
        _logView.SetMinHeight(Dp(340));
        _logView.SetTextIsSelectable(true);
        _logView.SetPadding(Dp(12), Dp(12), Dp(12), Dp(12));
        _logView.Background = Round(Page, 12, Border);
        card.AddView(_logView);
        var buttons = ButtonRow();
        AddButton(buttons, T("คัดลอก Log", "COPY LOG"), CopyLog, true);
        AddButton(buttons, T("ล้าง Log", "CLEAR LOG"), () =>
        {
            AndroidRuntimeLog.Clear();
            AndroidRuntimeLog.Append("UI", "Log cleared");
            RefreshLog(true);
        });
        card.AddView(buttons);
        body.AddView(card, CardLayout());

        var privacy = Card(LightBlue, Border);
        privacy.AddView(CardTitle(T("ความเป็นส่วนตัว", "Privacy")));
        privacy.AddView(Label(
            T("Bridge Secret, Login Token และ Binding Key จะไม่ถูกแสดงใน Runtime Log", "Bridge secrets, login tokens and binding keys are intentionally hidden from runtime logs."),
            12,
            Muted));
        body.AddView(privacy, CardLayout());
        return scroll;
    }

    private ScrollView BuildSettings()
    {
        var (scroll, body) = NewPage(
            T("ตั้งค่า", "Settings"),
            T("ตั้งค่า Voice Server และ McHttp", "Voice server and legacy McHttp options"));
        _settingsScroll = scroll;

        var server = Card();
        server.AddView(CardTitle("Voice Server"));
        server.AddView(InputLabel("Voice / McHttp Port"));
        _port = Input(ServerPreferences.GetVoicePort(this).ToString(), InputTypes.ClassNumber);
        _port.TextChanged += (_, _) => ApplyValidationHighlights();
        server.AddView(_port);
        var portButtons = ButtonRow();
        AddButton(portButtons, T("คัดลอก Port", "COPY PORT"), CopyPort);
        AddButton(portButtons, T("คัดลอก IP:Port", "COPY IP:PORT"), CopyAddress, true);
        server.AddView(portButtons);
        body.AddView(server, CardLayout());

        var security = Card();
        security.AddView(CardTitle(T("Legacy McHttp Server Key", "Legacy McHttp Server Key")));
        security.AddView(Label(
            T("ค่านี้แยกจาก Bridge Secret", "This is separate from Bridge Secret."),
            12,
            Muted));
        _serverKey = Input(ServerPreferences.GetServerKey(this), InputTypes.ClassText | InputTypes.TextVariationPassword);
        security.AddView(_serverKey, Top(Dp(10)));
        var keyButtons = ButtonRow();
        AddButton(keyButtons, T("แสดง/ซ่อน", "SHOW / HIDE"), ToggleServerKey);
        AddButton(keyButtons, T("สร้าง Key", "GENERATE"), GenerateServerKey);
        AddButton(keyButtons, T("คัดลอก Key", "COPY KEY"), CopyServerKey, true);
        security.AddView(keyButtons);
        body.AddView(security, CardLayout());

        var appearance = Card();
        appearance.AddView(CardTitle(T("ภาษาและธีม", "Language & Theme")));
        appearance.AddView(Label(
            T("ภาษาเริ่มต้นคือไทย ปุ่มเปลี่ยนภาษาและธีมอยู่ด้านบนและตอบสนองทันทีโดยไม่ต้องปิดแอป", "Thai is the default. Language and theme buttons at the top apply immediately without closing the app."),
            12,
            Muted));
        body.AddView(appearance, CardLayout());

        var save = Card(LightBlue, Border);
        save.AddView(CardTitle(T("บันทึกการตั้งค่า", "Save Configuration")));
        var saveButton = MakeButton(T("บันทึก", "SAVE SETTINGS"), true);
        WireButton(saveButton, () => SavePreferences(true));
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
            Background = Round(fill ?? CardFill, 18, stroke ?? Border)
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
        var box = new LinearLayout(this) { Orientation = Orientation.Vertical };
        box.SetGravity(GravityFlags.Center);
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
            Background = Round(FieldFill, 12, Border)
        };
        input.SetSingleLine(true);
        input.SetTextColor(Ink);
        input.SetHintTextColor(Muted);
        input.SetPadding(Dp(12), Dp(8), Dp(12), Dp(8));
        input.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(50));
        return input;
    }

    private TextView ReadOnly(string text)
    {
        var field = Label(text, 14, Ink);
        field.SetTextIsSelectable(true);
        field.Gravity = GravityFlags.CenterVertical;
        field.SetPadding(Dp(12), Dp(8), Dp(12), Dp(8));
        field.Background = Round(Page, 12, Border);
        field.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(50));
        return field;
    }

    private Button MakeButton(string text, bool primary = false, bool danger = false)
    {
        var fill = primary ? Blue : danger ? DangerFill : CardFill;
        var stroke = primary ? Blue : danger ? Red : Border;
        var color = primary ? Color.White : danger ? Red : Blue;
        var button = new Button(this)
        {
            Text = text,
            TextSize = 11,
            Gravity = GravityFlags.Center,
            Clickable = true,
            Focusable = true,
            MinHeight = Dp(44),
            MinWidth = Dp(48)
        };
        button.SetTextColor(color);
        button.Background = Round(fill, 12, stroke);
        return button;
    }

    private void WireButton(Button button, Action action)
    {
        button.Click += (_, _) =>
        {
            if (!button.Enabled)
                return;

            button.PerformHapticFeedback(FeedbackConstants.KeyboardTap);
            button.ScaleX = 0.96f;
            button.ScaleY = 0.96f;
            button.Alpha = 0.70f;
            button.Enabled = false;
            button.PostDelayed(new Runnable(() =>
            {
                button.ScaleX = 1f;
                button.ScaleY = 1f;
                button.Alpha = 1f;
                button.Enabled = true;
                action();
            }), 90);
        };
    }

    private LinearLayout ButtonRow()
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(0, Dp(12), 0, 0);
        return row;
    }

    private void AddButton(LinearLayout row, string text, Action action, bool primary = false)
    {
        var button = MakeButton(text, primary);
        WireButton(button, action);
        row.AddView(button, new LinearLayout.LayoutParams(0, Dp(48), 1f)
        {
            LeftMargin = Dp(3),
            RightMargin = Dp(3)
        });
    }

    private LinearLayout.LayoutParams Weight(int height = -2) => new(0, height, 1f);
    private LinearLayout.LayoutParams Top(int top) => new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = top };
    private LinearLayout.LayoutParams CardLayout() => new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { BottomMargin = Dp(12) };

    private GradientDrawable Round(Color fill, int radius, Color? stroke = null, int strokeWidth = 1)
    {
        var drawable = new GradientDrawable();
        drawable.SetColor(fill);
        drawable.SetCornerRadius(Dp(radius));
        if (stroke.HasValue)
            drawable.SetStroke(Dp(strokeWidth), stroke.Value);
        return drawable;
    }

    private static ColorDrawable Solid(Color color) => new(color);

    private void ShowPage(int index)
    {
        _currentPage = Math.Clamp(index, 0, _pages.Count - 1);
        for (var i = 0; i < _pages.Count; i++)
            _pages[i].Visibility = i == _currentPage ? ViewStates.Visible : ViewStates.Gone;
        for (var i = 0; i < _navButtons.Count; i++)
        {
            var selected = i == _currentPage;
            _navButtons[i].SetTextColor(selected ? Blue : Muted);
            _navButtons[i].Background = Round(selected ? LightBlue : CardFill, 14);
        }
        if (_currentPage == 2)
            RefreshLog(true);
    }

    private void ToggleLanguage()
    {
        SaveCurrentConfiguration();
        _thai = !_thai;
        ServerPreferences.SaveUi(this, _thai ? "th" : "en", _dark);
        AndroidRuntimeLog.Append("UI", $"Language changed to {(_thai ? "th" : "en")}");
        var page = _currentPage;
        ApplySystemBars();
        BuildUi(page);
        Toast.MakeText(this, _thai ? "เปลี่ยนเป็นภาษาไทยแล้ว" : "Switched to English", ToastLength.Short)?.Show();
    }

    private void ToggleTheme()
    {
        SaveCurrentConfiguration();
        _dark = !_dark;
        ServerPreferences.SaveUi(this, _thai ? "th" : "en", _dark);
        AndroidRuntimeLog.Append("UI", $"Theme changed to {(_dark ? "dark" : "light")}");
        var page = _currentPage;
        ApplySystemBars();
        BuildUi(page);
        Toast.MakeText(this, T(_dark ? "เปิดธีมมืดแล้ว" : "เปิดธีมสว่างแล้ว", _dark ? "Dark theme enabled" : "Light theme enabled"), ToastLength.Short)?.Show();
    }

    private void UpdateWebSocketFromRenderUrl()
    {
        if (_webSocketUrl == null)
            return;
        var generated = MakeWebSocketUrl(_renderUrl?.Text?.Trim() ?? string.Empty);
        _webSocketUrl.Text = string.IsNullOrEmpty(generated)
            ? T("ใส่ Render Service URL ที่ถูกต้อง", "Enter a valid Render service URL")
            : generated;
        _webSocketUrl.SetTextColor(string.IsNullOrEmpty(generated) ? Red : Ink);
        RefreshBridgePreview();
    }

    private void RefreshBridgePreview()
    {
        var ready = ValidateBridge(CurrentWebSocket(), CurrentServerId(), CurrentSecret(), out _);
        if (_readiness != null)
        {
            _readiness.Text = ready
                ? T("● พร้อมเริ่ม Server — WebSocket, Server ID และ Secret ครบแล้ว", "● Ready to start — WebSocket, Server ID and Secret are complete")
                : T("● ต้องแก้ไข — กรอกช่องกรอบสีแดงให้ครบก่อนเริ่ม Server", "● Action required — complete the red-highlighted fields before starting");
            _readiness.SetTextColor(ready ? Green : Red);
        }
        if (_configPreview != null)
            _configPreview.Text = PluginConfig(maskSecret: true);
        ApplyValidationHighlights();
    }

    private void ApplyValidationHighlights()
    {
        var renderValid = !string.IsNullOrEmpty(MakeWebSocketUrl(_renderUrl?.Text?.Trim() ?? string.Empty));
        var serverIdValid = !string.IsNullOrWhiteSpace(CurrentServerId());
        var secretValid = CurrentSecret().Length >= 16;
        var websocketValid = IsBridgeUrlValid(CurrentWebSocket());
        var portValid = _port == null || (int.TryParse(_port.Text, out var port) && port is >= 1 and <= 65535);

        SetFieldState(_renderUrl, renderValid);
        SetFieldState(_serverId, serverIdValid);
        SetFieldState(_bridgeSecret, secretValid);
        SetFieldState(_webSocketUrl, websocketValid, true);
        SetFieldState(_port, portValid);
    }

    private void SetFieldState(View? view, bool valid, bool readOnly = false)
    {
        if (view == null)
            return;
        var fill = valid ? (readOnly ? Page : FieldFill) : DangerFill;
        view.Background = Round(fill, 12, valid ? Border : Red, valid ? 1 : 2);
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
        if (string.IsNullOrWhiteSpace(websocket) || !Uri.TryCreate(websocket, UriKind.Absolute, out var uri))
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

    private string CurrentServerId() => _serverId?.Text?.Trim() ?? ServerPreferences.GetBridgeServerId(this);
    private string CurrentSecret() => _bridgeSecret?.Text?.Trim() ?? ServerPreferences.GetBridgeSecret(this);

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

    private static string Toml(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal);

    private void StartServer()
    {
        var issues = CollectStartIssues();
        if (issues.Count > 0)
        {
            AbortInvalidStart(issues);
            ShowStartValidationDialog(issues);
            return;
        }

        TryCurrentPort(out var port);
        var key = _serverKey?.Text?.Trim() ?? ServerPreferences.GetServerKey(this);
        if (string.IsNullOrWhiteSpace(key))
        {
            key = Guid.NewGuid().ToString("N");
            if (_serverKey != null)
                _serverKey.Text = key;
        }

        ServerPreferences.Save(this, port, key);
        ServerPreferences.SaveBridge(this, true, CurrentWebSocket(), CurrentServerId(), CurrentSecret());
        ServerPreferences.SaveUi(this, _thai ? "th" : "en", _dark);
        AndroidRuntimeLog.Append("UI", $"START SERVER pressed; port={port}; bridge=required+enabled");

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

    private List<ValidationIssue> CollectStartIssues()
    {
        var issues = new List<ValidationIssue>();
        var renderInput = _renderUrl?.Text?.Trim() ?? ToServiceUrl(ServerPreferences.GetBridgeUrl(this));
        var generatedWebSocket = MakeWebSocketUrl(renderInput);

        if (string.IsNullOrEmpty(generatedWebSocket))
        {
            issues.Add(new ValidationIssue(
                T("ยังไม่ได้ใส่ Render Relay URL หรือ URL ไม่ถูกต้อง", "Render Relay URL is missing or invalid"),
                T("ไปที่หน้า บริดจ์ → Render Relay → Render Service URL แล้ววางลิงก์ https://...onrender.com", "Go to Bridge → Render Relay → Render Service URL and paste your https://...onrender.com link"),
                1,
                _renderUrl));
        }

        if (string.IsNullOrWhiteSpace(CurrentServerId()))
        {
            issues.Add(new ValidationIssue(
                T("Server ID ยังว่าง", "Server ID is empty"),
                T("ไปที่หน้า บริดจ์ → Server ID และใช้ mcsv-main ได้หากมี Minecraft Server เดียว", "Go to Bridge → Server ID. You can use mcsv-main when you have one Minecraft server"),
                1,
                _serverId));
        }

        if (CurrentSecret().Length < 16)
        {
            issues.Add(new ValidationIssue(
                T("Bridge Secret ยังไม่ได้กรอกหรือสั้นเกินไป", "Bridge Secret is missing or too short"),
                T("ไปที่หน้า บริดจ์ → Bridge Secret แล้วใส่ค่าเดียวกับ BRIDGE_SECRET บน Render อย่างน้อย 16 ตัวอักษร", "Go to Bridge → Bridge Secret and enter the same BRIDGE_SECRET used on Render, at least 16 characters"),
                1,
                _bridgeSecret));
        }

        if (!string.IsNullOrEmpty(generatedWebSocket) && !IsBridgeUrlValid(CurrentWebSocket()))
        {
            issues.Add(new ValidationIssue(
                T("WebSocket URL ยังไม่พร้อม", "WebSocket URL is not ready"),
                T("กลับไปที่หน้า บริดจ์ แล้วแก้ Render Service URL ให้แอปสร้าง wss://.../bridge อัตโนมัติ", "Return to Bridge and correct the Render Service URL so the app can generate wss://.../bridge automatically"),
                1,
                _renderUrl));
        }

        if (!int.TryParse(_port?.Text, out var port) || port is < 1 or > 65535)
        {
            issues.Add(new ValidationIssue(
                T("Port ไม่ถูกต้อง", "Invalid port"),
                T("ไปที่หน้า ตั้งค่า → Voice / McHttp Port แล้วใส่เลข 1 ถึง 65535", "Go to Settings → Voice / McHttp Port and enter a value from 1 to 65535"),
                3,
                _port));
        }

        ApplyValidationHighlights();
        return issues;
    }

    private void AbortInvalidStart(IReadOnlyCollection<ValidationIssue> issues)
    {
        AndroidRuntimeLog.Append("UI", $"START BLOCKED: required configuration missing/invalid; issues={issues.Count}");
        VcServerApp.Shutdown();
        StopService(new Intent(this, typeof(VoiceCraftServerService)));
    }

    private void ShowStartValidationDialog(List<ValidationIssue> issues)
    {
        var first = issues[0];
        var message = string.Join("\n\n", issues.Select((issue, index) =>
            $"{index + 1}. {issue.Message}\n{T("เพิ่ม/แก้ได้ที่", "Where to fix")}: {PageName(issue.Page)}\n{T("วิธีแก้", "Fix")}: {issue.Fix}"));

        var builder = new AlertDialog.Builder(this)
            .SetTitle(T("หยุดการเริ่ม Server — ข้อมูลยังไม่ครบ", "Server start stopped — setup incomplete"))
            .SetMessage(message)
            .SetPositiveButton(
                first.Page == 1 ? T("ไปหน้า Bridge", "GO TO BRIDGE") : T("ไปหน้า Settings", "GO TO SETTINGS"),
                (_, _) => GoToIssue(first))
            .SetNegativeButton(T("ปิด", "CLOSE"), (_, _) => { });

        if (issues.Any(x => x.Page == 1))
            builder.SetNeutralButton(T("เปิด Render", "OPEN RENDER"), (_, _) => OpenRender());

        builder.Show();
    }

    private string PageName(int page) => page switch
    {
        1 => T("หน้า บริดจ์ (Bridge Setup)", "Bridge Setup"),
        3 => T("หน้า ตั้งค่า (Settings)", "Settings"),
        _ => T("หน้าหลัก", "Home")
    };

    private void GoToIssue(ValidationIssue issue)
    {
        ShowPage(issue.Page);
        FocusProblem(issue.Target, issue.Page == 1 ? _bridgeScroll : _settingsScroll);
    }

    private void FocusProblem(View? view, ScrollView? scroll = null)
    {
        if (view == null)
            return;
        view.RequestFocus();
        view.Background = Round(DangerFill, 12, Red, 3);
        view.Animate().ScaleX(1.035f).ScaleY(1.035f).SetDuration(100).Start();
        view.PostDelayed(new Runnable(() =>
        {
            view.Animate().ScaleX(1f).ScaleY(1f).SetDuration(160).Start();
            view.RequestRectangleOnScreen(new Rect(0, 0, Math.Max(1, view.Width), Math.Max(1, view.Height)), true);
            scroll?.RequestChildFocus(view, view);
        }), 130);
    }

    private void StopServer()
    {
        AndroidRuntimeLog.Append("UI", "STOP SERVER pressed");
        VcServerApp.Shutdown();
        StopService(new Intent(this, typeof(VoiceCraftServerService)));
        RefreshUi();
    }

    private void SaveCurrentConfiguration()
    {
        var port = TryCurrentPort(out var current) ? current : ServerPreferences.GetVoicePort(this);
        var key = _serverKey?.Text?.Trim() ?? ServerPreferences.GetServerKey(this);
        ServerPreferences.Save(this, port, key);
        ServerPreferences.SaveBridge(this, true, CurrentWebSocket(), CurrentServerId(), CurrentSecret());
    }

    private void SavePreferences(bool toast)
    {
        SaveCurrentConfiguration();
        ServerPreferences.SaveUi(this, _thai ? "th" : "en", _dark);
        if (toast)
            Toast.MakeText(this, T("บันทึกการตั้งค่าแล้ว", "Settings saved"), ToastLength.Short)?.Show();
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
        var ip = GetLanIpv4() ?? T("ไม่พบ LAN IPv4", "No LAN IPv4");
        var port = TryCurrentPort(out var current) ? current : ServerPreferences.GetVoicePort(this);
        var running = VcServerApp.IsRunning;
        var service = VoiceCraftServerService.IsServiceRunning;

        if (_statusTitle != null && _statusDetail != null)
        {
            if (!string.IsNullOrWhiteSpace(VoiceCraftServerService.LastError))
            {
                _statusTitle.Text = T("ผิดพลาด", "ERROR");
                _statusTitle.SetTextColor(Red);
                var advice = RuntimeDiagnostics.Describe(VoiceCraftServerService.LastError, _thai);
                _statusDetail.Text = advice.Cause;
            }
            else if (running)
            {
                _statusTitle.Text = T("กำลังทำงาน", "RUNNING");
                _statusTitle.SetTextColor(Green);
                _statusDetail.Text = T($"UDP/TCP {port} • Foreground Service ทำงานอยู่", $"UDP/TCP {port} • foreground service active");
            }
            else if (service)
            {
                _statusTitle.Text = T("กำลังเริ่ม", "STARTING");
                _statusTitle.SetTextColor(Amber);
                _statusDetail.Text = T($"กำลังเตรียมพอร์ต {port}", $"Preparing port {port}");
            }
            else
            {
                _statusTitle.Text = T("หยุดอยู่", "STOPPED");
                _statusTitle.SetTextColor(Ink);
                _statusDetail.Text = T("พร้อมเริ่มเมื่อ Render Relay ตั้งค่าครบ", "Ready when Render Relay setup is complete");
            }
        }

        if (_address != null)
            _address.Text = $"{ip}:{port}";
        if (_clientCount != null)
            _clientCount.Text = VcServerApp.ConnectedClients.ToString();
        if (_bridgeState != null)
            _bridgeState.Text = ShortBridge(VoiceCraftServerService.BridgeStatus);
        if (_relaySummary != null)
        {
            var wss = CurrentWebSocket();
            _relaySummary.Text = string.IsNullOrEmpty(wss) ? T("ยังไม่ได้ตั้งค่า Relay", "Relay not configured") : wss;
            _relaySummary.SetTextColor(string.IsNullOrEmpty(wss) ? Red : Ink);
        }
        if (_start != null)
        {
            _start.Enabled = !running && !service;
            _start.Alpha = _start.Enabled ? 1f : 0.5f;
        }
        if (_stop != null)
        {
            _stop.Enabled = running || service;
            _stop.Alpha = _stop.Enabled ? 1f : 0.5f;
        }

        RefreshErrorCard();
        MaybeShowRuntimeError();
        RefreshBridgePreview();
        RefreshLog();
    }

    private void RefreshErrorCard()
    {
        if (_errorCard == null || _errorText == null)
            return;
        var error = VoiceCraftServerService.LastError;
        if (string.IsNullOrWhiteSpace(error))
            error = VoiceCraftServerService.BridgeLastError;
        if (string.IsNullOrWhiteSpace(error))
        {
            _errorCard.Visibility = ViewStates.Gone;
            return;
        }

        var advice = RuntimeDiagnostics.Describe(error, _thai);
        _errorText.Text = $"{advice.Title}\n{T("สาเหตุ", "Cause")}: {advice.Cause}\n{T("วิธีแก้", "Fix")}: {advice.Fix}";
        _errorCard.Visibility = ViewStates.Visible;
    }

    private void MaybeShowRuntimeError()
    {
        var runtimeError = VoiceCraftServerService.LastError ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(runtimeError) && runtimeError != _shownRuntimeError)
        {
            _shownRuntimeError = runtimeError;
            ShowErrorDialog(runtimeError, false);
            return;
        }

        var bridgeError = VoiceCraftServerService.BridgeLastError ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(bridgeError) && bridgeError != _shownBridgeError)
        {
            _shownBridgeError = bridgeError;
            ShowErrorDialog(bridgeError, true);
        }
    }

    private void ShowErrorDialog(string error, bool bridge)
    {
        var advice = RuntimeDiagnostics.Describe(error, _thai);
        var configProblem = error.Contains("CONFIG_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
                            error.Contains("invalid-config", StringComparison.OrdinalIgnoreCase);
        var builder = new AlertDialog.Builder(this)
            .SetTitle(bridge ? T("Bridge มีปัญหา", "Bridge problem") : T("Server เกิดข้อผิดพลาด", "Server error"))
            .SetMessage($"{advice.Title}\n\n{T("สาเหตุ", "Cause")}:\n{advice.Cause}\n\n{T("วิธีแก้", "Fix")}:\n{advice.Fix}")
            .SetPositiveButton(
                configProblem ? T("ไปหน้า Bridge", "GO TO BRIDGE") : T("เปิด Log", "OPEN LOGS"),
                (_, _) => ShowPage(configProblem ? 1 : 2))
            .SetNegativeButton(T("ปิด", "CLOSE"), (_, _) => { });
        if (configProblem)
            builder.SetNeutralButton(T("เปิด Render", "OPEN RENDER"), (_, _) => OpenRender());
        builder.Show();
    }

    private string ShortBridge(string status) => status switch
    {
        "endstone-connected" => T("ออนไลน์", "ONLINE"),
        "relay-only" => "RELAY",
        "relay-disconnected" => T("หลุด", "OFFLINE"),
        "starting" => T("กำลังเริ่ม", "STARTING"),
        "invalid-config" => T("CONFIG ผิด", "CONFIG"),
        "disabled" => T("ออฟไลน์", "OFFLINE"),
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
        var text = AndroidRuntimeLog.Snapshot(_thai);
        _logView.Text = string.IsNullOrWhiteSpace(text) ? T("(ยังไม่มี Log)", "(no log entries yet)") : text;
    }

    private void CopyIp() => Copy("VoiceCraft LAN IP", GetLanIpv4() ?? "0.0.0.0");
    private void CopyPort() => Copy("VoiceCraft port", (TryCurrentPort(out var port) ? port : ServerPreferences.GetVoicePort(this)).ToString());
    private void CopyAddress() => Copy("VoiceCraft address", $"{GetLanIpv4() ?? "0.0.0.0"}:{(TryCurrentPort(out var port) ? port : ServerPreferences.GetVoicePort(this))}");

    private void CopyWebSocket()
    {
        if (string.IsNullOrEmpty(CurrentWebSocket()))
        {
            ShowStartValidationDialog(CollectStartIssues().Where(x => x.Page == 1).ToList());
            return;
        }
        Copy("VoiceCraft WebSocket", CurrentWebSocket());
    }

    private void CopyBridgeSecret()
    {
        if (string.IsNullOrEmpty(CurrentSecret()))
        {
            var issues = CollectStartIssues().Where(x => x.Target == _bridgeSecret).ToList();
            if (issues.Count > 0)
                ShowStartValidationDialog(issues);
            return;
        }
        Copy("VoiceCraft Bridge Secret", CurrentSecret(), true);
    }

    private void CopyServerKey()
    {
        var key = _serverKey?.Text?.Trim() ?? string.Empty;
        if (key.Length > 0)
            Copy("VoiceCraft Server Key", key, true);
    }

    private void CopyPluginConfig()
    {
        if (!ValidateBridge(CurrentWebSocket(), CurrentServerId(), CurrentSecret(), out _))
        {
            var issues = CollectStartIssues().Where(x => x.Page == 1).ToList();
            if (issues.Count > 0)
                ShowStartValidationDialog(issues);
            return;
        }
        Copy("VoiceCraft Endstone config.toml", PluginConfig(false), true);
    }

    private void CopyAllSetup()
    {
        if (!ValidateBridge(CurrentWebSocket(), CurrentServerId(), CurrentSecret(), out _))
        {
            var issues = CollectStartIssues().Where(x => x.Page == 1).ToList();
            if (issues.Count > 0)
                ShowStartValidationDialog(issues);
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
        var log = AndroidRuntimeLog.Snapshot(_thai);
        if (string.IsNullOrWhiteSpace(log))
        {
            Toast.MakeText(this, T("Log ยังว่าง", "Log is empty"), ToastLength.Short)?.Show();
            return;
        }
        Copy("VoiceCraft Server log", log);
    }

    private void Copy(string label, string value, bool secret = false)
    {
        if (GetSystemService(ClipboardService) is not global::Android.Content.ClipboardManager clipboard)
            return;
        clipboard.PrimaryClip = ClipData.NewPlainText(label, value);
        Toast.MakeText(
            this,
            secret ? T("คัดลอกแล้ว — Clipboard มีข้อมูลลับ", "Copied — clipboard contains a secret") : T("คัดลอกแล้ว", "Copied"),
            secret ? ToastLength.Long : ToastLength.Short)?.Show();
    }

    private void GenerateBridgeSecret()
    {
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        if (_bridgeSecret != null)
        {
            _bridgeSecret.Text = secret;
            _bridgeSecret.SetSelection(secret.Length);
        }
        Toast.MakeText(this, T("สร้าง Secret แล้ว — นำค่าเดียวกันไปใส่ BRIDGE_SECRET บน Render", "Secret generated — use the same value for BRIDGE_SECRET on Render"), ToastLength.Long)?.Show();
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

    private void OpenRender()
    {
        var serviceUrl = _renderUrl?.Text?.Trim() ?? ToServiceUrl(ServerPreferences.GetBridgeUrl(this));
        if (!Uri.TryCreate(serviceUrl, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https"))
            serviceUrl = "https://dashboard.render.com/";
        try
        {
            StartActivity(new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(serviceUrl)));
        }
        catch (Exception ex)
        {
            Toast.MakeText(this, T($"เปิด Render ไม่สำเร็จ: {ex.Message}", $"Could not open Render: {ex.Message}"), ToastLength.Long)?.Show();
        }
    }

    private void ShowInformation()
    {
        var scroll = new ScrollView(this);
        var guide = Label(_thai ? ThaiGuide() : EnglishGuide(), 14, Ink);
        guide.SetPadding(Dp(18), Dp(10), Dp(18), Dp(20));
        guide.SetLineSpacing(0, 1.15f);
        scroll.AddView(guide);

        new AlertDialog.Builder(this)
            .SetTitle(T("วิธีใช้งาน VoiceCraft Server Mobile", "VoiceCraft Server Mobile Setup Guide"))
            .SetView(scroll)
            .SetPositiveButton(T("ไปหน้า Bridge", "GO TO BRIDGE"), (_, _) => ShowPage(1))
            .SetNeutralButton(T("เปิด Render", "OPEN RENDER"), (_, _) => OpenRender())
            .SetNegativeButton(T("ปิด", "CLOSE"), (_, _) => { })
            .Show();
    }

    private string ThaiGuide() =>
        "ก่อนเริ่ม Server ต้องตั้งค่า Render Relay ให้ครบทุกครั้ง\n\n" +
        "1) สร้าง Render Relay\n" +
        "• Render → New Web Service → เลือก GitHub repository VoiceCraft-Server-Mobile\n" +
        "• Root Directory: VoiceCraft.Bridge.Relay\n" +
        "• Runtime: Node\n" +
        "• Build Command: npm install --omit=dev\n" +
        "• Start Command: npm start\n" +
        "• Health Check Path: /health\n" +
        "• Environment Variable: BRIDGE_SECRET=<secret ที่เดายาก>\n" +
        "• Deploy จนสถานะเป็น Live\n\n" +
        "2) ตั้งค่า Android\n" +
        "• ไปหน้า บริดจ์\n" +
        "• วาง Render Service URL เช่น https://voicecraft-server-mobile.onrender.com\n" +
        "• แอปจะสร้าง wss://.../bridge ให้อัตโนมัติ\n" +
        "• Server ID ใช้ mcsv-main ได้ถ้ามีเซิร์ฟเวอร์เดียว\n" +
        "• Bridge Secret ต้องตรงกับ BRIDGE_SECRET บน Render\n" +
        "• ถ้ายังไม่มี Secret กด สร้าง Secret แล้วคัดลอกไปใส่ Render\n" +
        "• ช่องที่ขาดหรือผิดจะเป็นกรอบสีแดง\n\n" +
        "3) Endstone บน MCSV\n" +
        "• ใช้ Endstone 0.11.x\n" +
        "• อัปโหลด endstone_voicecraft-0.2.0-py3-none-any.whl ไป plugins/\n" +
        "• Start หนึ่งครั้งเพื่อสร้างไฟล์ config\n" +
        "• กลับมาที่แอป กด คัดลอก Plugin Config แล้วนำไปแทน config.toml ของปลั๊กอิน\n" +
        "• Restart Minecraft Server และตรวจ Log ว่า BRIDGE connected / android=connected\n\n" +
        "4) เริ่ม VoiceCraft Server\n" +
        "• กด เริ่มเซิร์ฟเวอร์ ที่หน้า Home\n" +
        "• แอปจะตรวจ Render URL, WebSocket, Server ID, Secret และ Port ก่อนเริ่ม\n" +
        "• ถ้าขาดแม้แต่รายการเดียว การเริ่มจะถูกหยุดทันทีและขึ้น Popup บอกว่าขาดอะไร เพิ่มที่หน้าไหน และมีปุ่มพาไปแก้\n" +
        "• เมื่อครบจึงเริ่ม Foreground Service, UDP/TCP และ VoiceCraft Runtime\n\n" +
        "5) VoiceCraft Client\n" +
        "• ตอนทดสอบให้ Client อยู่ LAN เดียวกับ Android\n" +
        "• ใช้ VoiceCraft Client 1.7.x ที่เข้ากันได้\n" +
        "• ใส่ IP:Port จากหน้า Home และตั้ง Positioning Type = Server\n" +
        "• เชื่อมแล้วอ่าน Binding Key 5 ตัวจาก Description\n\n" +
        "6) Bind ใน Minecraft\n" +
        "• ใช้ /vcbind ABC12 โดยแทน ABC12 ด้วย Binding Key ของคุณ\n\n" +
        "7) ข้อจำกัดปัจจุบัน\n" +
        "• Render WSS ส่ง state + binding เท่านั้น\n" +
        "• เสียงยังใช้ UDP ไป Android โดยตรง ผู้เล่นนอก LAN ยังต้องมี public UDP relay ใน Phase ถัดไป\n\n" +
        "ถ้าเกิด Error ให้เปิดหน้า Log ระบบจะพยายามบอกสาเหตุและวิธีแก้ให้";

    private string EnglishGuide() =>
        "Render Relay must be fully configured before the server can start.\n\n" +
        "1) Create the Render relay\n" +
        "• Render → New Web Service → select the VoiceCraft-Server-Mobile GitHub repository.\n" +
        "• Root Directory: VoiceCraft.Bridge.Relay\n" +
        "• Runtime: Node\n" +
        "• Build Command: npm install --omit=dev\n" +
        "• Start Command: npm start\n" +
        "• Health Check Path: /health\n" +
        "• Environment Variable: BRIDGE_SECRET=<strong secret>\n" +
        "• Deploy until the service is Live.\n\n" +
        "2) Configure Android\n" +
        "• Open Bridge Setup and paste the Render service URL.\n" +
        "• The app automatically creates wss://.../bridge.\n" +
        "• Server ID can remain mcsv-main for one Minecraft server.\n" +
        "• Bridge Secret must exactly match BRIDGE_SECRET on Render.\n" +
        "• Missing or invalid required fields are highlighted red.\n\n" +
        "3) Configure Endstone on MCSV\n" +
        "• Use Endstone 0.11.x and upload endstone_voicecraft-0.2.0-py3-none-any.whl to plugins/.\n" +
        "• Start once, then replace the plugin config.toml with the ready-to-paste config copied from this app.\n" +
        "• Restart Minecraft and verify BRIDGE connected / android=connected.\n\n" +
        "4) Start VoiceCraft Server\n" +
        "• Tap Start Server on Home.\n" +
        "• The app checks Render URL, generated WebSocket, Server ID, Secret and Port before launching anything.\n" +
        "• If anything is missing, startup is stopped and a popup tells you what is missing, where to add it, and takes you there.\n\n" +
        "5) Connect VoiceCraft Client\n" +
        "• For current testing, keep the client on the same LAN as Android.\n" +
        "• Use a compatible VoiceCraft 1.7.x client, connect to the Home IP:Port, and set Positioning Type = Server.\n\n" +
        "6) Bind in Minecraft\n" +
        "• Run /vcbind ABC12 using your actual 5-character binding key.\n\n" +
        "7) Current limitation\n" +
        "• Render WSS carries state/binding only. Audio still uses UDP directly to Android; public Internet voice needs the future UDP relay phase.\n\n" +
        "If an error occurs, open Logs for cause and suggested fix.";

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
        if (!IsBridgeUrlValid(url))
        {
            error = "Paste a valid Render URL so WebSocket can be generated";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool IsBridgeUrlValid(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("ws" or "wss"))
            return false;
        return string.Equals(uri.AbsolutePath.TrimEnd('/'), "/bridge", StringComparison.OrdinalIgnoreCase);
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

    private sealed record ValidationIssue(string Message, string Fix, int Page, View? Target);
}
