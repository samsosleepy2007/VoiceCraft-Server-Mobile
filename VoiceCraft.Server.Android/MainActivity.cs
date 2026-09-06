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
    private string _shownRuntimeError = string.Empty;
    private string _shownBridgeError = string.Empty;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ActionBar?.Hide();
        _density = Resources?.DisplayMetrics?.Density ?? 1f;
        _thai = ServerPreferences.GetLanguage(this) == "th";
        _dark = ServerPreferences.GetDarkTheme(this);
        Window?.SetStatusBarColor(_dark ? Color.Rgb(10, 18, 32) : Blue);
        Window?.SetNavigationBarColor(_dark ? Color.Rgb(15, 23, 42) : Color.White);
        BuildUi();
        RequestNotificationPermission();
        StartRefreshLoop();
        AndroidRuntimeLog.Append("UI", "MainActivity opened");
    }

    private string T(string thai, string english) => _thai ? thai : english;
    private int Dp(int value) => (int)(value * _density + 0.5f);

    private void BuildUi()
    {
        _pages.Clear();
        _navButtons.Clear();

        var shell = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Background = Solid(Page)
        };
        shell.AddView(BuildHeader(), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(96)));

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
            Background = Solid(CardFill)
        };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(Dp(18), Dp(8), Dp(12), Dp(8));

        var title = new LinearLayout(this) { Orientation = Orientation.Vertical };
        title.AddView(Label("VoiceCraft Server", 21, Ink, true));
        title.AddView(Label("Android • VoiceCraft 1.7.1", 12, Muted));
        row.AddView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        var right = new LinearLayout(this) { Orientation = Orientation.Vertical };
        right.SetGravity(GravityFlags.Right);
        var tools = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        tools.SetGravity(GravityFlags.Right | GravityFlags.CenterVertical);

        var language = HeaderButton(_thai ? "EN" : "TH");
        language.Click += (_, _) => ToggleLanguage();
        tools.AddView(language, HeaderButtonLayout());

        var theme = HeaderButton(_dark ? T("สว่าง", "LIGHT") : T("มืด", "DARK"));
        theme.Click += (_, _) => ToggleTheme();
        tools.AddView(theme, HeaderButtonLayout(Dp(68)));

        var info = HeaderButton("INFO");
        info.Click += (_, _) => ShowInformation();
        tools.AddView(info, HeaderButtonLayout(Dp(60)));
        right.AddView(tools);

        var by = Label("By SamSoSleepy", 11, Blue, true);
        by.Gravity = GravityFlags.Right;
        by.SetPadding(0, Dp(5), Dp(4), 0);
        right.AddView(by);
        row.AddView(right);
        return row;
    }

    private LinearLayout.LayoutParams HeaderButtonLayout(int width = -1)
    {
        var p = new LinearLayout.LayoutParams(width > 0 ? width : Dp(48), Dp(38));
        p.LeftMargin = Dp(3);
        return p;
    }

    private Button HeaderButton(string text)
    {
        var button = MakeButton(text);
        button.TextSize = 10;
        return button;
    }

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
        var (scroll, body) = NewPage(T("แดชบอร์ด", "Dashboard"), T("สถานะเซิร์ฟเวอร์และคำสั่งที่ใช้บ่อย", "Server status and quick actions"));

        var hero = Card(LightBlue, Border);
        hero.AddView(Label("VOICECRAFT SERVER", 12, Blue, true));
        _statusTitle = Label(T("หยุดอยู่", "STOPPED"), 30, Ink, true);
        _statusDetail = Label(T("พร้อมเริ่มเซิร์ฟเวอร์", "Ready to start"), 13, Muted);
        hero.AddView(_statusTitle);
        hero.AddView(_statusDetail);

        var metrics = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        metrics.AddView(Metric("0", T("Voice Clients", "Voice clients"), out _clientCount), Weight());
        metrics.AddView(Metric(T("ปิด", "OFF"), T("บริดจ์", "Bridge"), out _bridgeState), Weight());
        hero.AddView(metrics);
        body.AddView(hero, CardLayout());

        _errorCard = Card(DangerFill, Red);
        _errorCard.Visibility = ViewStates.Gone;
        _errorCard.AddView(Label(T("พบข้อผิดพลาด", "Error detected"), 16, Red, true));
        _errorText = Label(string.Empty, 12, Ink);
        _errorCard.AddView(_errorText);
        var errorButtons = ButtonRow();
        AddButton(errorButtons, T("ไปที่ Log", "OPEN LOGS"), (_, _) => ShowPage(2), true);
        _errorCard.AddView(errorButtons);
        body.AddView(_errorCard, CardLayout());

        var connection = Card();
        connection.AddView(CardTitle(T("การเชื่อมต่อ", "Connection")));
        _address = Label(T("กำลังค้นหา LAN IP…", "Detecting LAN address…"), 19, Ink, true);
        _address.SetTextIsSelectable(true);
        connection.AddView(_address);
        connection.AddView(Label(T("VoiceCraft Client ใช้ IP:Port นี้เพื่อเชื่อมต่อ UDP", "VoiceCraft Client connects to this UDP address."), 12, Muted));
        var connButtons = ButtonRow();
        AddButton(connButtons, T("คัดลอก IP", "COPY IP"), (_, _) => CopyIp());
        AddButton(connButtons, T("คัดลอก Port", "COPY PORT"), (_, _) => CopyPort());
        AddButton(connButtons, T("คัดลอก IP:Port", "COPY ADDRESS"), (_, _) => CopyAddress(), true);
        connection.AddView(connButtons);
        body.AddView(connection, CardLayout());

        var bridge = Card();
        bridge.AddView(CardTitle("Endstone Bridge"));
        _relaySummary = Label(T("ยังไม่ได้ตั้งค่า Relay", "Relay not configured"), 14, Ink, true);
        _relaySummary.SetTextIsSelectable(true);
        bridge.AddView(_relaySummary);
        bridge.AddView(Label(T("วาง Render URL ครั้งเดียว แอปจะสร้าง /bridge และ Plugin Config ให้พร้อมใช้", "Paste the Render URL once. The app generates /bridge and the full plugin config."), 12, Muted));
        var bridgeButtons = ButtonRow();
        AddButton(bridgeButtons, T("คัดลอก WSS", "COPY WSS"), (_, _) => CopyWebSocket());
        AddButton(bridgeButtons, T("คัดลอก Secret", "COPY SECRET"), (_, _) => CopyBridgeSecret());
        AddButton(bridgeButtons, T("Plugin Config", "PLUGIN CONFIG"), (_, _) => CopyPluginConfig(), true);
        bridge.AddView(bridgeButtons);
        body.AddView(bridge, CardLayout());

        var control = Card();
        control.AddView(CardTitle(T("ควบคุมเซิร์ฟเวอร์", "Server Control")));
        var controls = ButtonRow();
        _start = MakeButton(T("เริ่มเซิร์ฟเวอร์", "START SERVER"), true);
        _stop = MakeButton(T("หยุดเซิร์ฟเวอร์", "STOP SERVER"), false, true);
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
        var (scroll, body) = NewPage(T("ตั้งค่า Bridge", "Bridge Setup"), T("วาง Render URL ครั้งเดียว แล้วคัดลอกค่าที่พร้อมใช้งานได้ทันที", "Paste once, copy everything ready-to-use"));

        var setup = Card();
        setup.AddView(CardTitle("Render Relay"));
        _bridgeEnabled = new global::Android.Widget.Switch(this)
        {
            Text = T("เปิดใช้งาน Endstone Bridge", "Enable Endstone bridge"),
            Checked = ServerPreferences.GetBridgeEnabled(this)
        };
        _bridgeEnabled.SetTextColor(Ink);
        _bridgeEnabled.CheckedChange += (_, _) => RefreshBridgePreview();
        setup.AddView(_bridgeEnabled);

        setup.AddView(InputLabel(T("Render Service URL", "Render Service URL")));
        _renderUrl = Input(ToServiceUrl(ServerPreferences.GetBridgeUrl(this)), InputTypes.ClassText | InputTypes.TextVariationUri);
        _renderUrl.Hint = "https://voicecraft-server-mobile.onrender.com";
        _renderUrl.TextChanged += (_, _) => UpdateWebSocketFromRenderUrl();
        setup.AddView(_renderUrl);
        var renderButtons = ButtonRow();
        AddButton(renderButtons, T("เปิด Render", "OPEN RENDER"), (_, _) => OpenRender());
        AddButton(renderButtons, T("คัดลอก WebSocket", "COPY WEBSOCKET"), (_, _) => CopyWebSocket(), true);
        setup.AddView(renderButtons);

        setup.AddView(InputLabel(T("WebSocket URL • สร้างอัตโนมัติ", "WebSocket URL • auto generated")));
        _webSocketUrl = ReadOnly(T("ใส่ Render URL ด้านบน", "Enter Render URL above"));
        setup.AddView(_webSocketUrl);

        setup.AddView(InputLabel("Server ID"));
        _serverId = Input(ServerPreferences.GetBridgeServerId(this), InputTypes.ClassText);
        _serverId.TextChanged += (_, _) => RefreshBridgePreview();
        setup.AddView(_serverId);

        setup.AddView(InputLabel("Bridge Secret"));
        _bridgeSecret = Input(ServerPreferences.GetBridgeSecret(this), InputTypes.ClassText | InputTypes.TextVariationPassword);
        _bridgeSecret.Hint = T("ต้องตรงกับ BRIDGE_SECRET บน Render", "Must match BRIDGE_SECRET on Render");
        _bridgeSecret.TextChanged += (_, _) => RefreshBridgePreview();
        setup.AddView(_bridgeSecret);
        var secretButtons = ButtonRow();
        AddButton(secretButtons, T("แสดง/ซ่อน", "SHOW / HIDE"), (_, _) => ToggleBridgeSecret());
        AddButton(secretButtons, T("สร้าง Secret", "GENERATE"), (_, _) => GenerateBridgeSecret());
        AddButton(secretButtons, T("คัดลอก Secret", "COPY SECRET"), (_, _) => CopyBridgeSecret(), true);
        setup.AddView(secretButtons);

        _readiness = Label(T("○ การตั้งค่ายังไม่ครบ", "○ Setup incomplete"), 13, Red, true);
        _readiness.SetPadding(0, Dp(12), 0, 0);
        setup.AddView(_readiness);
        body.AddView(setup, CardLayout());

        var config = Card();
        config.AddView(CardTitle(T("Endstone Plugin Config พร้อมวาง", "Ready-to-paste Endstone Plugin Config")));
        config.AddView(Label(T("URL และ Secret จะถูกใส่ให้อัตโนมัติ ค่าอื่นใช้ค่าที่ทดสอบแล้ว ไม่ต้องแก้ทีละจุด", "URL and secret are inserted automatically. Everything else stays at the tested defaults."), 12, Muted));
        _configPreview = Label(string.Empty, 11, Ink);
        _configPreview.Typeface = Typeface.Monospace;
        _configPreview.SetTextIsSelectable(true);
        _configPreview.SetPadding(Dp(12), Dp(12), Dp(12), Dp(12));
        _configPreview.Background = Round(Page, 12, Border);
        config.AddView(_configPreview, Top(Dp(12)));
        var configButtons = ButtonRow();
        AddButton(configButtons, T("คัดลอก Plugin Config", "COPY PLUGIN CONFIG"), (_, _) => CopyPluginConfig(), true);
        AddButton(configButtons, T("คัดลอกทั้งหมด", "COPY ALL SETUP"), (_, _) => CopyAllSetup());
        config.AddView(configButtons);
        body.AddView(config, CardLayout());

        var flow = Card(LightBlue, Border);
        flow.AddView(CardTitle(T("เส้นทางการเชื่อมต่อ", "Connection Flow")));
        flow.AddView(Label("Minecraft / MCSV", 14, Ink, true));
        flow.AddView(Label("↓  Endstone v0.2.x", 13, Blue));
        flow.AddView(Label("↓  Render WebSocket Relay", 13, Blue));
        flow.AddView(Label("↓  VoiceCraft Server Android", 13, Blue));
        flow.AddView(Label(T("หมายเหตุ: เสียงยังใช้ UDP ไปยัง Android โดยตรง", "Note: Voice audio still uses UDP to Android directly."), 12, Muted));
        body.AddView(flow, CardLayout());
        return scroll;
    }

    private ScrollView BuildLogs()
    {
        var (scroll, body) = NewPage(T("วิเคราะห์ระบบ", "Diagnostics"), T("Log ของ Runtime, McHttp, Voice และ Bridge", "Runtime, McHttp, voice and bridge activity"));
        var card = Card();
        card.AddView(CardTitle(T("Runtime Log", "Runtime Log")));
        _logView = Label(T("(ยังไม่มี Log)", "(no log entries yet)"), 11, Ink);
        _logView.Typeface = Typeface.Monospace;
        _logView.SetMinHeight(Dp(340));
        _logView.SetTextIsSelectable(true);
        _logView.SetPadding(Dp(12), Dp(12), Dp(12), Dp(12));
        _logView.Background = Round(Page, 12, Border);
        card.AddView(_logView);
        var buttons = ButtonRow();
        AddButton(buttons, T("คัดลอก Log", "COPY LOG"), (_, _) => CopyLog(), true);
        AddButton(buttons, T("ล้าง Log", "CLEAR LOG"), (_, _) =>
        {
            AndroidRuntimeLog.Clear();
            AndroidRuntimeLog.Append("UI", "Log cleared");
            RefreshLog(true);
        });
        card.AddView(buttons);
        body.AddView(card, CardLayout());

        var privacy = Card(LightBlue, Border);
        privacy.AddView(CardTitle(T("ความเป็นส่วนตัว", "Privacy")));
        privacy.AddView(Label(T("Bridge Secret, Login Token และ Binding Key จะไม่ถูกแสดงใน Runtime Log โดยตั้งใจ", "Bridge secrets, login tokens and binding keys are intentionally hidden from runtime logs."), 12, Muted));
        body.AddView(privacy, CardLayout());
        return scroll;
    }

    private ScrollView BuildSettings()
    {
        var (scroll, body) = NewPage(T("ตั้งค่า", "Settings"), T("ตั้งค่า Voice Server และ McHttp", "Voice server and legacy McHttp options"));

        var server = Card();
        server.AddView(CardTitle(T("Voice Server", "Voice Server")));
        server.AddView(InputLabel(T("Voice / McHttp Port", "Voice / McHttp Port")));
        _port = Input(ServerPreferences.GetVoicePort(this).ToString(), InputTypes.ClassNumber);
        _port.TextChanged += (_, _) => ApplyValidationHighlights();
        server.AddView(_port);
        var portButtons = ButtonRow();
        AddButton(portButtons, T("คัดลอก Port", "COPY PORT"), (_, _) => CopyPort());
        AddButton(portButtons, T("คัดลอก IP:Port", "COPY IP:PORT"), (_, _) => CopyAddress(), true);
        server.AddView(portButtons);
        body.AddView(server, CardLayout());

        var security = Card();
        security.AddView(CardTitle(T("Legacy McHttp Server Key", "Legacy McHttp Server Key")));
        security.AddView(Label(T("ค่านี้แยกจาก Bridge Secret และ Endstone Phase 2 ปกติจะใช้ Bridge แทน McHttp", "Separate from Bridge Secret. Endstone Phase 2 normally uses the bridge instead."), 12, Muted));
        _serverKey = Input(ServerPreferences.GetServerKey(this), InputTypes.ClassText | InputTypes.TextVariationPassword);
        security.AddView(_serverKey, Top(Dp(10)));
        var keyButtons = ButtonRow();
        AddButton(keyButtons, T("แสดง/ซ่อน", "SHOW / HIDE"), (_, _) => ToggleServerKey());
        AddButton(keyButtons, T("สร้าง Key", "GENERATE"), (_, _) => GenerateServerKey());
        AddButton(keyButtons, T("คัดลอก Key", "COPY KEY"), (_, _) => CopyServerKey(), true);
        security.AddView(keyButtons);
        body.AddView(security, CardLayout());

        var appearance = Card();
        appearance.AddView(CardTitle(T("ภาษาและธีม", "Language & Theme")));
        appearance.AddView(Label(T("ภาษาเริ่มต้นคือภาษาไทย เปลี่ยนเป็น English และสลับธีมมืด/สว่างได้จากด้านบนของแอป", "Thai is the default language. Use the controls at the top to switch English and light/dark theme."), 12, Muted));
        body.AddView(appearance, CardLayout());

        var save = Card(LightBlue, Border);
        save.AddView(CardTitle(T("บันทึกการตั้งค่า", "Save Configuration")));
        save.AddView(Label(T("การตั้งค่าจะถูกบันทึกเมื่อเริ่มเซิร์ฟเวอร์หรือออกจากหน้าแอปด้วย", "Settings are also saved when the server starts or this app leaves the foreground."), 12, Muted));
        var saveButton = MakeButton(T("บันทึก", "SAVE SETTINGS"), true);
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
        var fill = primary ? Blue : danger ? DangerFill : CardFill;
        var stroke = primary ? Blue : danger ? Red : Border;
        var color = primary ? Color.White : danger ? Red : Blue;
        var button = new Button(this) { Text = text, TextSize = 11, Gravity = GravityFlags.Center };
        button.SetTextColor(color);
        button.Background = Round(fill, 12, stroke);
        AttachPressAnimation(button);
        return button;
    }

    private void AttachPressAnimation(View view)
    {
        view.Touch += (_, e) =>
        {
            var action = e.Event?.Action;
            if (action == MotionEventActions.Down)
            {
                view.Animate().ScaleX(0.96f).ScaleY(0.96f).Alpha(0.72f).SetDuration(70).Start();
            }
            else if (action is MotionEventActions.Up or MotionEventActions.Cancel)
            {
                view.Animate().ScaleX(1f).ScaleY(1f).Alpha(1f).SetDuration(120).Start();
            }
            e.Handled = false;
        };
    }

    private LinearLayout ButtonRow()
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
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
        for (var i = 0; i < _pages.Count; i++)
            _pages[i].Visibility = i == index ? ViewStates.Visible : ViewStates.Gone;
        for (var i = 0; i < _navButtons.Count; i++)
        {
            var selected = i == index;
            _navButtons[i].SetTextColor(selected ? Blue : Muted);
            _navButtons[i].Background = Round(selected ? LightBlue : CardFill, 14);
        }
        if (index == 2)
            RefreshLog(true);
    }

    private void ToggleLanguage()
    {
        SavePreferences(false);
        ServerPreferences.SaveUi(this, _thai ? "en" : "th", _dark);
        Toast.MakeText(this, _thai ? "Switching to English" : "กำลังเปลี่ยนเป็นภาษาไทย", ToastLength.Short)?.Show();
        Recreate();
    }

    private void ToggleTheme()
    {
        SavePreferences(false);
        ServerPreferences.SaveUi(this, _thai ? "th" : "en", !_dark);
        Recreate();
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
        var enabled = _bridgeEnabled?.Checked == true;
        var ready = ValidateBridge(CurrentWebSocket(), CurrentServerId(), CurrentSecret(), out _);
        if (_readiness != null)
        {
            if (!enabled)
            {
                _readiness.Text = T("○ Bridge ปิดอยู่ — เซิร์ฟเวอร์เริ่มได้โดยไม่ใช้ Endstone Bridge", "○ Bridge disabled — server can start without Endstone Bridge");
                _readiness.SetTextColor(Muted);
            }
            else
            {
                _readiness.Text = ready
                    ? T("● พร้อมใช้งาน — WebSocket, Secret และ Plugin Config ครบแล้ว", "● Configuration ready — WebSocket, secret and plugin config are ready")
                    : T("● ต้องแก้ไข — กรอกช่องกรอบสีแดงให้ครบก่อนเริ่ม", "● Action required — complete the red-highlighted fields before starting");
                _readiness.SetTextColor(ready ? Green : Red);
            }
        }
        if (_configPreview != null)
            _configPreview.Text = PluginConfig(maskSecret: true);
        ApplyValidationHighlights();
    }

    private void ApplyValidationHighlights()
    {
        var bridgeRequired = _bridgeEnabled?.Checked == true;
        var renderValid = !string.IsNullOrEmpty(MakeWebSocketUrl(_renderUrl?.Text?.Trim() ?? string.Empty));
        var serverIdValid = !string.IsNullOrWhiteSpace(CurrentServerId());
        var secretValid = CurrentSecret().Length >= 16;
        var websocketValid = IsBridgeUrlValid(CurrentWebSocket());
        var portValid = int.TryParse(_port?.Text, out var port) && port is >= 1 and <= 65535;

        SetFieldState(_renderUrl, !bridgeRequired || renderValid);
        SetFieldState(_serverId, !bridgeRequired || serverIdValid);
        SetFieldState(_bridgeSecret, !bridgeRequired || secretValid);
        SetFieldState(_webSocketUrl, !bridgeRequired || websocketValid, true);
        SetFieldState(_port, _port == null || portValid);
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
        var issues = CollectStartIssues();
        if (issues.Count > 0)
        {
            ShowStartValidationDialog(issues);
            return;
        }

        TryCurrentPort(out var port);
        var key = _serverKey?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            key = Guid.NewGuid().ToString("N");
            if (_serverKey != null)
                _serverKey.Text = key;
        }

        var bridgeEnabled = _bridgeEnabled?.Checked == true;
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

    private List<ValidationIssue> CollectStartIssues()
    {
        var issues = new List<ValidationIssue>();
        if (!int.TryParse(_port?.Text, out var port) || port is < 1 or > 65535)
        {
            issues.Add(new ValidationIssue(
                T("Port ไม่ถูกต้อง", "Invalid port"),
                T("ใส่เลข Port ตั้งแต่ 1 ถึง 65535", "Enter a port from 1 to 65535"),
                3,
                _port));
        }

        if (_bridgeEnabled?.Checked == true)
        {
            if (string.IsNullOrEmpty(MakeWebSocketUrl(_renderUrl?.Text?.Trim() ?? string.Empty)))
                issues.Add(new ValidationIssue(
                    T("ยังไม่มี Render Service URL ที่ถูกต้อง", "Render Service URL is missing or invalid"),
                    T("ไปหน้า Bridge แล้ววาง URL แบบ https://ชื่อบริการ.onrender.com", "Open Bridge Setup and paste https://your-service.onrender.com"),
                    1,
                    _renderUrl));

            if (string.IsNullOrWhiteSpace(CurrentServerId()))
                issues.Add(new ValidationIssue(
                    T("Server ID ว่าง", "Server ID is empty"),
                    T("ใช้ mcsv-main ได้หากมี Minecraft Server เดียว", "Use mcsv-main if you have one Minecraft server"),
                    1,
                    _serverId));

            if (CurrentSecret().Length < 16)
                issues.Add(new ValidationIssue(
                    T("Bridge Secret ยังไม่ครบ", "Bridge Secret is incomplete"),
                    T("ใส่ BRIDGE_SECRET ค่าเดียวกับ Render อย่างน้อย 16 ตัวอักษร หรือกดสร้าง Secret แล้วนำไปใส่ Render", "Use the same BRIDGE_SECRET as Render with at least 16 characters, or generate one and copy it to Render"),
                    1,
                    _bridgeSecret));

            if (!IsBridgeUrlValid(CurrentWebSocket()))
                issues.Add(new ValidationIssue(
                    T("WebSocket URL ยังไม่พร้อม", "WebSocket URL is not ready"),
                    T("แอปต้องสร้าง URL ที่ลงท้ายด้วย /bridge", "The generated WebSocket URL must end with /bridge"),
                    1,
                    _renderUrl));
        }
        ApplyValidationHighlights();
        return issues;
    }

    private void ShowStartValidationDialog(List<ValidationIssue> issues)
    {
        var first = issues[0];
        var message = string.Join("\n\n", issues.Select((issue, index) =>
            $"{index + 1}. {issue.Message}\n{T("วิธีแก้", "Fix")}: {issue.Fix}"));

        new AlertDialog.Builder(this)
            .SetTitle(T("ยังเริ่ม Server ไม่ได้", "Server is not ready to start"))
            .SetMessage(message)
            .SetPositiveButton(T("ไปแก้ไข", "GO FIX IT"), (_, _) =>
            {
                ShowPage(first.Page);
                FocusProblem(first.Target);
            })
            .SetNegativeButton(T("ปิด", "CLOSE"), (_, _) => { })
            .Show();
    }

    private void FocusProblem(View? view)
    {
        if (view == null)
            return;
        view.RequestFocus();
        view.Animate().ScaleX(1.035f).ScaleY(1.035f).SetDuration(120).Start();
        _handler?.PostDelayed(new Runnable(() =>
            view.Animate().ScaleX(1f).ScaleY(1f).SetDuration(160).Start()), 180);
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
            if (VoiceCraftServerService.LastError != null)
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
                _statusDetail.Text = T("พร้อมเริ่มเซิร์ฟเวอร์", "Ready to start");
            }
        }

        if (_address != null)
            _address.Text = $"{ip}:{port}";
        if (_clientCount != null)
            _clientCount.Text = VcServerApp.ConnectedClients.ToString();
        if (_bridgeState != null)
            _bridgeState.Text = ShortBridge(VoiceCraftServerService.BridgeStatus);
        if (_relaySummary != null)
            _relaySummary.Text = string.IsNullOrEmpty(CurrentWebSocket()) ? T("ยังไม่ได้ตั้งค่า Relay", "Relay not configured") : CurrentWebSocket();
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
        new AlertDialog.Builder(this)
            .SetTitle(bridge ? T("Bridge มีปัญหา", "Bridge problem") : T("Server เกิดข้อผิดพลาด", "Server error"))
            .SetMessage($"{advice.Title}\n\n{T("สาเหตุ", "Cause")}:\n{advice.Cause}\n\n{T("วิธีแก้", "Fix")}:\n{advice.Fix}")
            .SetPositiveButton(T("เปิด Log", "OPEN LOGS"), (_, _) => ShowPage(2))
            .SetNegativeButton(T("ปิด", "CLOSE"), (_, _) => { })
            .Show();
    }

    private string ShortBridge(string status) => status switch
    {
        "endstone-connected" => T("ออนไลน์", "ONLINE"),
        "relay-only" => "RELAY",
        "relay-disconnected" => T("หลุด", "OFFLINE"),
        "starting" => T("กำลังเริ่ม", "STARTING"),
        "invalid-config" => T("CONFIG ผิด", "CONFIG"),
        "disabled" => T("ปิด", "OFF"),
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
            Toast.MakeText(this, T("ใส่ Render URL ที่ถูกต้องก่อน", "Enter a valid Render URL first"), ToastLength.Short)?.Show();
            ShowPage(1);
            FocusProblem(_renderUrl);
            return;
        }
        Copy("VoiceCraft WebSocket", CurrentWebSocket());
    }

    private void CopyBridgeSecret()
    {
        if (string.IsNullOrEmpty(CurrentSecret()))
        {
            Toast.MakeText(this, T("Bridge Secret ยังว่าง", "Bridge secret is empty"), ToastLength.Short)?.Show();
            ShowPage(1);
            FocusProblem(_bridgeSecret);
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
            ShowBridgeConfigProblem(error);
            return;
        }
        Copy("VoiceCraft Endstone config.toml", PluginConfig(false), true);
    }

    private void CopyAllSetup()
    {
        if (!ValidateBridge(CurrentWebSocket(), CurrentServerId(), CurrentSecret(), out var error))
        {
            ShowBridgeConfigProblem(error);
            return;
        }
        var text =
            $"Render Environment Variable:\nBRIDGE_SECRET={CurrentSecret()}\n\n" +
            $"Android Bridge:\nWebSocket={CurrentWebSocket()}\nServer ID={CurrentServerId()}\nBridge Secret={CurrentSecret()}\n\n" +
            "Endstone config.toml:\n" + PluginConfig(false);
        Copy("VoiceCraft complete bridge setup", text, true);
    }

    private void ShowBridgeConfigProblem(string error)
    {
        var issues = CollectStartIssues().Where(x => x.Page == 1).ToList();
        if (issues.Count > 0)
        {
            ShowStartValidationDialog(issues);
            return;
        }
        Toast.MakeText(this, error, ToastLength.Long)?.Show();
        ShowPage(1);
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
        Toast.MakeText(this, T("สร้าง Secret แล้ว อย่าลืมใช้ค่าเดียวกันบน Render", "Secret generated — use the same value on Render"), ToastLength.Long)?.Show();
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
        var serviceUrl = _renderUrl?.Text?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(serviceUrl, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https"))
            serviceUrl = "https://dashboard.render.com/";
        try
        {
            var intent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(serviceUrl));
            StartActivity(intent);
        }
        catch (Exception ex)
        {
            Toast.MakeText(this, T($"เปิด Render ไม่สำเร็จ: {ex.Message}", $"Could not open Render: {ex.Message}"), ToastLength.Long)?.Show();
        }
    }

    private void ShowInformation()
    {
        var text = _thai ? ThaiGuide() : EnglishGuide();
        var scroll = new ScrollView(this);
        var guide = Label(text, 14, Ink);
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
        "ขั้นตอนติดตั้งแบบละเอียด\n\n" +
        "1) เตรียม Render Relay\n" +
        "• สร้าง Web Service จาก GitHub repository VoiceCraft-Server-Mobile\n" +
        "• Root Directory: VoiceCraft.Bridge.Relay\n" +
        "• Runtime: Node\n" +
        "• Build Command: npm install --omit=dev\n" +
        "• Start Command: npm start\n" +
        "• Health Check Path: /health\n" +
        "• เพิ่ม Environment Variable ชื่อ BRIDGE_SECRET และตั้งเป็น secret ที่ยาวและเดายาก\n" +
        "• Deploy จนสถานะ Web Service เป็น Live\n\n" +
        "2) ตั้งค่า Android App\n" +
        "• ไปหน้า Bridge แล้วเปิด Enable Endstone Bridge\n" +
        "• วาง Render Service URL เช่น https://voicecraft-server-mobile.onrender.com\n" +
        "• แอปจะสร้าง wss://.../bridge ให้อัตโนมัติ ไม่ต้องเติม /bridge เอง\n" +
        "• Server ID ใช้ mcsv-main ได้หากมี Minecraft Server เดียว\n" +
        "• Bridge Secret ต้องเป็นค่าเดียวกับ BRIDGE_SECRET บน Render\n" +
        "• ถ้ายังไม่มี Secret สามารถกด สร้าง Secret แล้วคัดลอกค่านั้นไปใส่ Render\n" +
        "• เมื่อทุกอย่างครบ สถานะจะเป็นสีเขียวและสามารถคัดลอก Plugin Config ได้\n\n" +
        "3) ติดตั้ง Endstone Plugin บน MCSV\n" +
        "• ใช้ Endstone 0.11.x\n" +
        "• อัปโหลด endstone_voicecraft-0.2.0-py3-none-any.whl ไปที่โฟลเดอร์ plugins/\n" +
        "• Start Server หนึ่งครั้งเพื่อให้ Endstone สร้างโฟลเดอร์ข้อมูล/ไฟล์ config ของปลั๊กอิน\n" +
        "• เปิด config.toml ของ VoiceCraft Endstone แล้วแทนเนื้อหาด้วย Plugin Config ที่คัดลอกจากแอป\n" +
        "• Restart Minecraft Server\n" +
        "• Log ที่ถูกต้องควรเห็น BRIDGE connected และ android=connected\n\n" +
        "4) เริ่ม VoiceCraft Server บน Android\n" +
        "• กลับหน้า Home แล้วกด เริ่มเซิร์ฟเวอร์\n" +
        "• หากมีค่าขาด แอปจะแสดง Popup พร้อมปุ่มพาไปยังช่องที่ต้องแก้\n" +
        "• เมื่อทำงานแล้วดู IP:Port ที่หน้า Home เช่น 192.168.1.7:9050\n\n" +
        "5) เชื่อม VoiceCraft Client\n" +
        "• ในการทดสอบปัจจุบัน Client ควรอยู่ LAN เดียวกับ Android\n" +
        "• Client และ Server ต้องใช้ VoiceCraft 1.7.x ที่เข้ากันได้\n" +
        "• ใส่ IP:Port จากหน้า Home\n" +
        "• ตั้ง Positioning Type เป็น Server\n" +
        "• เชื่อมต่อแล้ว Client จะได้รับ Binding Key 5 ตัวใน Description\n\n" +
        "6) Bind กับ Minecraft\n" +
        "• เข้า Minecraft Server แล้วใช้ /vcbind ABC12 โดยแทน ABC12 ด้วย Binding Key ของคุณ\n" +
        "• หลัง Bind สำเร็จ Android จะใช้ตำแหน่ง/มิติ/การหมุนจาก Endstone สำหรับ VoiceCraft entity\n\n" +
        "7) ข้อจำกัดปัจจุบัน\n" +
        "• Render WebSocket Relay ส่งเฉพาะ state + binding control plane\n" +
        "• เสียง VoiceCraft ยังใช้ UDP ไปที่ Android โดยตรง\n" +
        "• ผู้เล่นนอก LAN ยังต้องมี public UDP relay/endpoint ใน Phase ถัดไป\n\n" +
        "หากเกิด Error ให้เปิดหน้า Log ระบบจะแสดงสาเหตุที่เป็นไปได้และวิธีแก้สำหรับปัญหาที่ตรวจจับได้";

    private string EnglishGuide() =>
        "Detailed setup\n\n" +
        "1) Prepare the Render relay\n" +
        "• Create a Render Web Service from the VoiceCraft-Server-Mobile GitHub repository.\n" +
        "• Root Directory: VoiceCraft.Bridge.Relay\n" +
        "• Runtime: Node\n" +
        "• Build Command: npm install --omit=dev\n" +
        "• Start Command: npm start\n" +
        "• Health Check Path: /health\n" +
        "• Add BRIDGE_SECRET as a strong environment variable.\n" +
        "• Deploy until the Web Service is Live.\n\n" +
        "2) Configure the Android app\n" +
        "• Open Bridge and enable Endstone Bridge.\n" +
        "• Paste the Render service URL, for example https://voicecraft-server-mobile.onrender.com.\n" +
        "• The app automatically generates wss://.../bridge.\n" +
        "• Use mcsv-main as Server ID when you have one Minecraft server.\n" +
        "• Bridge Secret must exactly match BRIDGE_SECRET on Render.\n" +
        "• When ready, copy the generated Plugin Config.\n\n" +
        "3) Install Endstone on MCSV\n" +
        "• Use Endstone 0.11.x and upload endstone_voicecraft-0.2.0-py3-none-any.whl to plugins/.\n" +
        "• Start once so Endstone creates the plugin data/config.\n" +
        "• Replace the VoiceCraft Endstone config.toml contents with the config copied from this app.\n" +
        "• Restart the Minecraft server and verify BRIDGE connected / android=connected in logs.\n\n" +
        "4) Start VoiceCraft Server on Android\n" +
        "• Tap Start Server on Home. Missing required values will show a popup that takes you directly to the field to fix.\n" +
        "• Copy the LAN IP:Port shown on Home.\n\n" +
        "5) Connect VoiceCraft Client\n" +
        "• For the current test, keep the client on the same LAN as Android.\n" +
        "• Use a compatible VoiceCraft 1.7.x client and set Positioning Type to Server.\n" +
        "• Connect to the Android IP:Port and read the 5-character binding key from the client description.\n\n" +
        "6) Bind in Minecraft\n" +
        "• Run /vcbind ABC12 using your actual binding key.\n" +
        "• Endstone position, dimension and rotation will then drive the VoiceCraft entity.\n\n" +
        "7) Current limitation\n" +
        "• Render WSS carries state/binding only. Voice audio still uses UDP directly to Android. Public Internet voice needs the future UDP relay/endpoint phase.\n\n" +
        "If an error occurs, open Logs. Known failures include an explanation and suggested fix.";

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
