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
using JavaRunnable = Java.Lang.Runnable;
using JavaIRunnable = Java.Lang.IRunnable;
using VcServerApp = VoiceCraft.Server.App;

namespace VoiceCraft.Server.Android;

[Activity(
    Name = "chat.voicecraft.server.ModernMainActivity",
    Label = "VoiceCraft Server",
    MainLauncher = true,
    Exported = true)]
public sealed class ModernMainActivity : Activity
{
    private static readonly Color Primary = Color.Rgb(73, 116, 255);
    private static readonly Color Primary2 = Color.Rgb(105, 86, 255);
    private static readonly Color Sky = Color.Rgb(89, 200, 250);
    private static readonly Color Green = Color.Rgb(34, 197, 94);
    private static readonly Color Amber = Color.Rgb(245, 158, 11);
    private static readonly Color Red = Color.Rgb(239, 68, 68);

    private bool _thai;
    private bool _dark;
    private float _density = 1f;
    private int _currentPage;
    private string _lastVisualState = string.Empty;
    private bool _bridgeSecretVisible;
    private bool _serverKeyVisible;

    private Color Page => _dark ? Color.Rgb(12, 18, 32) : Color.Rgb(247, 248, 255);
    private Color Surface => _dark ? Color.Rgb(22, 30, 48) : Color.White;
    private Color SurfaceSoft => _dark ? Color.Rgb(28, 38, 60) : Color.Rgb(248, 250, 255);
    private Color Tint => _dark ? Color.Rgb(35, 48, 82) : Color.Rgb(237, 242, 255);
    private Color Tint2 => _dark ? Color.Rgb(45, 38, 80) : Color.Rgb(245, 241, 255);
    private Color Ink => _dark ? Color.Rgb(246, 248, 255) : Color.Rgb(35, 39, 54);
    private Color Muted => _dark ? Color.Rgb(160, 170, 190) : Color.Rgb(116, 124, 145);
    private Color Border => _dark ? Color.Rgb(51, 63, 87) : Color.Rgb(228, 232, 244);
    private Color DangerFill => _dark ? Color.Rgb(64, 29, 36) : Color.Rgb(255, 242, 244);
    private Color SuccessFill => _dark ? Color.Rgb(21, 58, 48) : Color.Rgb(239, 252, 246);
    private Color WarningFill => _dark ? Color.Rgb(66, 49, 22) : Color.Rgb(255, 249, 235);

    private readonly List<View> _pages = new();
    private readonly List<Button> _navButtons = new();

    private TextView? _statusTitle;
    private TextView? _statusDetail;
    private TextView? _statusBadge;
    private TextView? _address;
    private TextView? _clientCount;
    private TextView? _minecraftCount;
    private TextView? _boundCount;
    private TextView? _playerList;
    private TextView? _bridgeState;
    private TextView? _relaySummary;
    private LinearLayout? _errorCard;
    private TextView? _errorText;
    private Button? _start;
    private Button? _stop;
    private Button? _navPower;

    private ScrollView? _bridgeScroll;
    private EditText? _renderUrl;
    private TextView? _webSocketUrl;
    private EditText? _serverId;
    private EditText? _bridgeSecret;
    private TextView? _readiness;
    private TextView? _configPreview;

    private ScrollView? _settingsScroll;
    private EditText? _port;
    private EditText? _serverKey;

    private TextView? _logView;
    private TextView? _logHelp;
    private Handler? _handler;
    private JavaIRunnable? _refreshRunnable;
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
        AndroidRuntimeLog.Append("UI", "ModernMainActivity opened");
    }

    private string T(string thai, string english) => _thai ? thai : english;
    private int Dp(int value) => (int)(value * _density + 0.5f);

    private void ApplySystemBars()
    {
        Window?.SetStatusBarColor(_dark ? Color.Rgb(8, 12, 23) : Color.Rgb(240, 244, 255));
        Window?.SetNavigationBarColor(_dark ? Color.Rgb(12, 18, 32) : Color.White);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M && Window?.DecorView is View decor)
        {
#pragma warning disable CA1416
            decor.SystemUiVisibility = _dark ? (StatusBarVisibility)0 : (StatusBarVisibility)SystemUiFlags.LightStatusBar;
#pragma warning restore CA1416
        }
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

        shell.AddView(BuildHeader(), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(112)));

        var content = new FrameLayout(this) { Background = Solid(Page) };
        shell.AddView(content, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));

        _pages.Add(BuildHome());
        _pages.Add(BuildBridge());
        _pages.Add(BuildLogs());
        _pages.Add(BuildSettings());
        foreach (var page in _pages)
            content.AddView(page, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        shell.AddView(BuildNav(), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(82)));
        SetContentView(shell);
        ShowPage(System.Math.Clamp(selectedPage, 0, _pages.Count - 1), false);
        UpdateWebSocketFromRenderUrl();
        RefreshUi();
        RefreshLog(true);
        AnimatePageEntrance(_currentPage);
    }

    private View BuildHeader()
    {
        var outer = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Background = Solid(Page)
        };
        outer.SetPadding(Dp(16), Dp(10), Dp(16), Dp(4));

        var card = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Background = Round(Surface, 22, Border)
        };
        card.Elevation = Dp(2);
        card.SetPadding(Dp(16), Dp(10), Dp(12), Dp(8));

        var titleRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        titleRow.SetGravity(GravityFlags.CenterVertical);

        var title = new LinearLayout(this) { Orientation = Orientation.Vertical };
        title.AddView(Label("VoiceCraft Server", 19, Ink, true));
        title.AddView(Label(T("ศูนย์ควบคุม VoiceCraft บน Android", "Android VoiceCraft control center"), 11, Muted));
        titleRow.AddView(title, new LinearLayout.LayoutParams(0, Dp(48), 1f));

        var by = Pill("By SamSoSleepy", Tint, Primary, true);
        titleRow.AddView(by, new LinearLayout.LayoutParams(Dp(126), Dp(36)));
        card.AddView(titleRow);

        var tools = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        tools.SetGravity(GravityFlags.CenterVertical);
        tools.SetPadding(0, Dp(2), 0, 0);

        AddHeaderTool(tools, _thai ? "EN" : "ไทย", ToggleLanguage);
        AddHeaderTool(tools, _dark ? T("สว่าง", "LIGHT") : T("มืด", "DARK"), ToggleTheme);
        AddHeaderTool(tools, "INFO", ShowInformation);
        card.AddView(tools, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(44)));
        outer.AddView(card, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        return outer;
    }

    private void AddHeaderTool(LinearLayout row, string text, Action action)
    {
        var button = MakeButton(text, compact: true);
        WireButton(button, action);
        row.AddView(button, new LinearLayout.LayoutParams(0, Dp(38), 1f)
        {
            LeftMargin = Dp(3),
            RightMargin = Dp(3)
        });
    }

    private View BuildNav()
    {
        var outer = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Background = Solid(Page)
        };
        outer.SetPadding(Dp(14), Dp(4), Dp(14), Dp(10));

        var nav = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            Background = Round(Surface, 24, Border)
        };
        nav.Elevation = Dp(5);
        nav.SetGravity(GravityFlags.Center);
        nav.SetPadding(Dp(6), Dp(6), Dp(6), Dp(6));

        AddNav(nav, T("หน้าหลัก", "HOME"), 0);
        AddNav(nav, T("บริดจ์", "BRIDGE"), 1);

        _navPower = MakeButton(T("เริ่ม", "START"), primary: true, compact: true);
        WireButton(_navPower, () =>
        {
            if (VcServerApp.IsRunning || VoiceCraftServerService.IsServiceRunning)
                StopServer();
            else
                StartServer();
        });
        _navPower.Elevation = Dp(5);
        nav.AddView(_navPower, new LinearLayout.LayoutParams(0, Dp(56), 1.15f)
        {
            LeftMargin = Dp(4),
            RightMargin = Dp(4)
        });

        AddNav(nav, T("ล็อก", "LOGS"), 2);
        AddNav(nav, T("ตั้งค่า", "SETTINGS"), 3);
        outer.AddView(nav, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(68)));
        return outer;
    }

    private void AddNav(LinearLayout nav, string name, int index)
    {
        var button = MakeButton(name, compact: true);
        WireButton(button, () => ShowPage(index));
        nav.AddView(button, new LinearLayout.LayoutParams(0, Dp(50), 1f)
        {
            LeftMargin = Dp(2),
            RightMargin = Dp(2)
        });
        _navButtons.Add(button);
    }

    private ScrollView BuildHome()
    {
        var (scroll, body) = NewPage(
            T("สวัสดี 👋", "Hello 👋"),
            T("เช็กสถานะและควบคุม VoiceCraft Server ได้จากที่นี่", "Check status and control VoiceCraft Server from here"));

        var hero = Card(gradient: true);
        hero.SetPadding(Dp(18), Dp(18), Dp(18), Dp(18));
        hero.AddView(Label("VOICECRAFT 1.7.1", 11, Color.White, true));
        _statusTitle = Label(T("หยุดอยู่", "STOPPED"), 30, Color.White, true);
        _statusDetail = Label(T("พร้อมเริ่มเมื่อการตั้งค่าครบ", "Ready when setup is complete"), 13, Color.Rgb(235, 242, 255));
        hero.AddView(_statusTitle);
        hero.AddView(_statusDetail);

        var badgeRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        badgeRow.SetPadding(0, Dp(10), 0, 0);
        _statusBadge = Pill(T("● ออฟไลน์", "● OFFLINE"), Color.Argb(40, 255, 255, 255), Color.White, true);
        badgeRow.AddView(_statusBadge, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, Dp(34)));
        hero.AddView(badgeRow);

        var metrics = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        metrics.SetPadding(0, Dp(14), 0, 0);
        metrics.AddView(MetricCard("0", T("Voice Clients", "Voice clients"), out _clientCount), Weight());
        metrics.AddView(MetricCard(T("ออฟไลน์", "OFFLINE"), T("บริดจ์", "Bridge"), out _bridgeState), Weight());
        hero.AddView(metrics);

        var playerMetrics = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        playerMetrics.SetPadding(0, Dp(8), 0, 0);
        playerMetrics.AddView(MetricCard("0", T("ผู้เล่น Minecraft", "Minecraft players"), out _minecraftCount), Weight());
        playerMetrics.AddView(MetricCard("0", T("Bind แล้ว", "Bound"), out _boundCount), Weight());
        hero.AddView(playerMetrics);
        body.AddView(hero, CardLayout());

        _errorCard = Card(DangerFill, Red);
        _errorCard.Visibility = ViewStates.Gone;
        _errorCard.AddView(SectionTitle(T("ต้องตรวจสอบ", "Needs attention"), Red));
        _errorText = Label(string.Empty, 12, Ink);
        _errorCard.AddView(_errorText);
        var errorButtons = ButtonRow();
        AddButton(errorButtons, T("เปิด Log", "OPEN LOGS"), () => ShowPage(2), primary: true);
        AddButton(errorButtons, T("ไป Bridge", "OPEN BRIDGE"), () => ShowPage(1));
        _errorCard.AddView(errorButtons);
        body.AddView(_errorCard, CardLayout());

        var quick = Card();
        quick.AddView(SectionTitle(T("การเชื่อมต่อ", "Connection"), Primary));
        _address = Label(T("กำลังค้นหา LAN IP…", "Detecting LAN address…"), 22, Ink, true);
        _address.SetTextIsSelectable(true);
        quick.AddView(_address);
        quick.AddView(Label(T("ใช้ IP:Port นี้ใน VoiceCraft Client", "Use this IP:Port in VoiceCraft Client"), 12, Muted));
        var quickButtons = ButtonRow();
        AddButton(quickButtons, T("คัดลอก IP", "COPY IP"), CopyIp);
        AddButton(quickButtons, T("คัดลอก Port", "COPY PORT"), CopyPort);
        AddButton(quickButtons, T("คัดลอกทั้งหมด", "COPY ADDRESS"), CopyAddress, primary: true);
        quick.AddView(quickButtons);
        body.AddView(quick, CardLayout());

        var bridge = Card();
        bridge.AddView(SectionTitle("Render Relay", Primary2));
        bridge.AddView(Label(T("Render Relay เป็นส่วนจำเป็นของระบบนี้", "Render Relay is required for this setup"), 12, Muted));
        _relaySummary = Label(T("ยังไม่ได้ตั้งค่า Relay", "Relay not configured"), 14, Ink, true);
        _relaySummary.SetTextIsSelectable(true);
        _relaySummary.SetPadding(0, Dp(8), 0, Dp(4));
        bridge.AddView(_relaySummary);
        var bridgeButtons = ButtonRow();
        AddButton(bridgeButtons, T("ตั้งค่า", "SET UP"), () => ShowPage(1), primary: true);
        AddButton(bridgeButtons, T("คัดลอก WSS", "COPY WSS"), CopyWebSocket);
        AddButton(bridgeButtons, "PLUGIN CONFIG", CopyPluginConfig);
        bridge.AddView(bridgeButtons);
        body.AddView(bridge, CardLayout());

        var players = Card();
        players.AddView(SectionTitle(T("ผู้เล่นและสถานะ Bind", "Players & Binding"), Primary2));
        players.AddView(Label(T("ข้อมูลมาจาก Endstone ผ่าน Render Relay และไม่แสดง Binding Key", "Live state comes from Endstone through Render Relay; binding keys are never shown here"), 12, Muted));
        _playerList = Label(T("ยังไม่มีผู้เล่น Minecraft ที่ติดตาม", "No tracked Minecraft players yet"), 12, Ink);
        _playerList.SetPadding(0, Dp(10), 0, 0);
        _playerList.SetTextIsSelectable(true);
        players.AddView(_playerList);
        var playerButtons = ButtonRow();
        AddButton(playerButtons, T("ขอ Snapshot", "REQUEST SNAPSHOT"), () =>
        {
            var queued = VoiceCraftServerService.RequestBridgeSnapshot();
            Toast.MakeText(
                this,
                queued ? T("ขอข้อมูลผู้เล่นล่าสุดแล้ว", "Fresh player snapshot requested") : T("Bridge ยังไม่ทำงาน", "Bridge is not running"),
                ToastLength.Short)?.Show();
            RefreshUi();
        }, primary: true);
        AddButton(playerButtons, T("รีเฟรช", "REFRESH"), RefreshUi);
        players.AddView(playerButtons);
        body.AddView(players, CardLayout());

        var control = Card(Tint, Border);
        control.AddView(SectionTitle(T("ควบคุมเซิร์ฟเวอร์", "Server Control"), Primary));
        control.AddView(Label(T("ก่อนเริ่ม ระบบจะตรวจ URL, WebSocket, Server ID, Secret และ Port ทุกครั้ง", "Before startup, URL, WebSocket, Server ID, Secret and Port are validated every time"), 12, Muted));
        var controls = ButtonRow();
        _start = MakeButton(T("เริ่มเซิร์ฟเวอร์", "START SERVER"), primary: true);
        _stop = MakeButton(T("หยุดเซิร์ฟเวอร์", "STOP SERVER"), danger: true);
        WireButton(_start, StartServer);
        WireButton(_stop, StopServer);
        controls.AddView(_start, Weight(Dp(52)));
        controls.AddView(_stop, Weight(Dp(52)));
        control.AddView(controls);
        body.AddView(control, CardLayout());
        return scroll;
    }

    private ScrollView BuildBridge()
    {
        var (scroll, body) = NewPage(
            T("ตั้งค่า Bridge", "Bridge Setup"),
            T("Primary จำเป็น ส่วน Backup เพิ่มได้ตามต้องการและใช้ Secret เดียวกัน", "Primary is required; optional backups can be added and share the same secret"));
        _bridgeScroll = scroll;

        var required = Card(WarningFill, Amber);
        required.AddView(SectionTitle(T("ต้องตั้งค่าก่อนเริ่ม", "Required before start"), Amber));
        required.AddView(Label(T("ถ้าค่าใดหายหรือผิด การเริ่ม Server จะถูกหยุดและพาไปยังช่องที่ต้องแก้", "If anything is missing or invalid, startup is blocked and the app takes you to the exact field"), 12, Ink));
        body.AddView(required, CardLayout());

        var setup = Card();
        setup.AddView(SectionTitle("Render Relay", Primary));

        setup.AddView(InputLabel("Render Service URL"));
        _renderUrl = Input(ToServiceUrl(ServerPreferences.GetBridgeUrl(this)), InputTypes.ClassText | InputTypes.TextVariationUri);
        _renderUrl.Hint = "https://voicecraft-server-mobile.onrender.com";
        _renderUrl.TextChanged += (_, _) => UpdateWebSocketFromRenderUrl();
        setup.AddView(_renderUrl);

        var renderButtons = ButtonRow();
        AddButton(renderButtons, T("เปิด Render", "OPEN RENDER"), OpenRender, primary: true);
        AddButton(renderButtons, T("คัดลอก WSS", "COPY WSS"), CopyWebSocket);
        setup.AddView(renderButtons);

        var backupButtons = ButtonRow();
        AddButton(backupButtons, T("+ เพิ่ม/จัดการลิงก์สำรอง", "+ MANAGE BACKUP RELAYS"), OpenBackupRelays, primary: true);
        setup.AddView(backupButtons);
        setup.AddView(Label(T("Backup เป็น Optional — ไม่เพิ่มก็เปิด Server ด้วย Primary ได้ตามปกติ", "Backups are optional — Primary-only startup works normally."), 11, Muted));

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
        AddButton(secretButtons, T("คัดลอก", "COPY"), CopyBridgeSecret, primary: true);
        setup.AddView(secretButtons);

        _readiness = Label(T("● ต้องตั้งค่าให้ครบก่อนเริ่ม", "● Setup required before start"), 13, Red, true);
        _readiness.SetPadding(0, Dp(14), 0, 0);
        setup.AddView(_readiness);
        body.AddView(setup, CardLayout());

        var config = Card(Tint2, Border);
        config.AddView(SectionTitle(T("Plugin Config พร้อมวาง", "Ready-to-paste Plugin Config"), Primary2));
        config.AddView(Label(T("URL และ Secret จะถูกใส่ให้อัตโนมัติ ค่าอื่นใช้ค่าที่ทดสอบแล้ว", "URL and secret are inserted automatically; other values stay at tested defaults"), 12, Muted));
        _configPreview = Label(string.Empty, 11, Ink);
        _configPreview.Typeface = Typeface.Monospace;
        _configPreview.SetTextIsSelectable(true);
        _configPreview.SetPadding(Dp(12), Dp(12), Dp(12), Dp(12));
        _configPreview.Background = Round(SurfaceSoft, 14, Border);
        config.AddView(_configPreview, Top(Dp(12)));
        var configButtons = ButtonRow();
        AddButton(configButtons, T("คัดลอก Config", "COPY CONFIG"), CopyPluginConfig, primary: true);
        AddButton(configButtons, T("คัดลอกทั้งหมด", "COPY ALL SETUP"), CopyAllSetup);
        config.AddView(configButtons);
        body.AddView(config, CardLayout());

        var flow = Card();
        flow.AddView(SectionTitle(T("เส้นทางการเชื่อมต่อ", "Connection Flow"), Sky));
        flow.AddView(FlowStep("1", "Minecraft / MCSV", T("Endstone ติดตาม player", "Endstone tracks players")));
        flow.AddView(FlowStep("2", "Render Relay", T("ส่ง state ผ่าน WebSocket", "Forwards state over WebSocket")));
        flow.AddView(FlowStep("3", "Android Server", T("อัปเดต VoiceCraft entity", "Updates VoiceCraft entities")));
        flow.AddView(Label(T("เสียงยังใช้ UDP ไป Android โดยตรง", "Voice audio still uses UDP directly to Android"), 12, Muted));
        body.AddView(flow, CardLayout());
        return scroll;
    }

    private ScrollView BuildLogs()
    {
        var (scroll, body) = NewPage(
            T("วิเคราะห์ระบบ", "Diagnostics"),
            T("ดูเหตุการณ์ล่าสุด สาเหตุ Error และคำแนะนำการแก้ไข", "Inspect recent activity, error causes, and suggested fixes"));

        var card = Card();
        card.AddView(SectionTitle(T("Runtime Log", "Runtime Log"), Primary));
        _logView = Label(T("(ยังไม่มี Log)", "(no log entries yet)"), 11, Ink);
        _logView.Typeface = Typeface.Monospace;
        _logView.SetMinHeight(Dp(330));
        _logView.SetTextIsSelectable(true);
        _logView.SetPadding(Dp(12), Dp(12), Dp(12), Dp(12));
        _logView.Background = Round(SurfaceSoft, 14, Border);
        card.AddView(_logView);

        var buttons = ButtonRow();
        AddButton(buttons, T("คัดลอก Log", "COPY LOG"), CopyLog, primary: true);
        AddButton(buttons, T("ล้าง Log", "CLEAR LOG"), () =>
        {
            AndroidRuntimeLog.Clear();
            AndroidRuntimeLog.Append("UI", "Log cleared");
            RefreshLog(true);
        });
        card.AddView(buttons);
        body.AddView(card, CardLayout());

        var help = Card(Tint, Border);
        help.AddView(SectionTitle(T("คำแนะนำอัตโนมัติ", "Automatic help"), Primary));
        _logHelp = Label(T("หากเกิด Error ระบบจะแสดงสาเหตุและวิธีแก้ที่นี่", "When an error occurs, likely cause and suggested fix appear here"), 12, Muted);
        help.AddView(_logHelp);
        body.AddView(help, CardLayout());

        var privacy = Card();
        privacy.AddView(SectionTitle(T("ความเป็นส่วนตัว", "Privacy"), Primary2));
        privacy.AddView(Label(T("Bridge Secret, Login Token และ Binding Key จะไม่ถูกแสดงใน Runtime Log", "Bridge secrets, login tokens and binding keys are intentionally hidden from runtime logs"), 12, Muted));
        body.AddView(privacy, CardLayout());
        return scroll;
    }

    private ScrollView BuildSettings()
    {
        var (scroll, body) = NewPage(
            T("ตั้งค่า", "Settings"),
            T("ปรับพอร์ต คีย์ และการแสดงผลของแอป", "Configure port, keys, and app appearance"));
        _settingsScroll = scroll;

        var server = Card();
        server.AddView(SectionTitle("Voice Server", Primary));
        server.AddView(InputLabel("Voice / McHttp Port"));
        _port = Input(ServerPreferences.GetVoicePort(this).ToString(), InputTypes.ClassNumber);
        _port.TextChanged += (_, _) => ApplyValidationHighlights();
        server.AddView(_port);
        var portButtons = ButtonRow();
        AddButton(portButtons, T("คัดลอก Port", "COPY PORT"), CopyPort);
        AddButton(portButtons, T("คัดลอก IP:Port", "COPY IP:PORT"), CopyAddress, primary: true);
        server.AddView(portButtons);
        body.AddView(server, CardLayout());

        var security = Card();
        security.AddView(SectionTitle(T("Legacy McHttp Server Key", "Legacy McHttp Server Key"), Primary2));
        security.AddView(Label(T("ค่านี้แยกจาก Bridge Secret", "This is separate from Bridge Secret"), 12, Muted));
        _serverKey = Input(ServerPreferences.GetServerKey(this), InputTypes.ClassText | InputTypes.TextVariationPassword);
        security.AddView(_serverKey, Top(Dp(10)));
        var keyButtons = ButtonRow();
        AddButton(keyButtons, T("แสดง/ซ่อน", "SHOW / HIDE"), ToggleServerKey);
        AddButton(keyButtons, T("สร้าง Key", "GENERATE"), GenerateServerKey);
        AddButton(keyButtons, T("คัดลอก", "COPY"), CopyServerKey, primary: true);
        security.AddView(keyButtons);
        body.AddView(security, CardLayout());

        var appearance = Card(Tint, Border);
        appearance.AddView(SectionTitle(T("ภาษาและธีม", "Language & Theme"), Primary));
        appearance.AddView(Label(T("ภาษาเริ่มต้นคือไทย และสามารถเปลี่ยนภาษา/ธีมจากด้านบนได้ทันที", "Thai is the default language; switch language/theme instantly from the top controls"), 12, Muted));
        body.AddView(appearance, CardLayout());

        var save = Card();
        save.AddView(SectionTitle(T("บันทึกการตั้งค่า", "Save Configuration"), Green));
        var saveButton = MakeButton(T("บันทึก", "SAVE SETTINGS"), primary: true);
        WireButton(saveButton, () => SavePreferences(true));
        save.AddView(saveButton, Top(Dp(12)));
        body.AddView(save, CardLayout());
        return scroll;
    }

    private (ScrollView Scroll, LinearLayout Body) NewPage(string title, string subtitle)
    {
        var scroll = new ScrollView(this) { FillViewport = true, Background = Solid(Page) };
        var body = new LinearLayout(this) { Orientation = Orientation.Vertical };
        body.SetPadding(Dp(18), Dp(18), Dp(18), Dp(34));

        body.AddView(Label(title, 26, Ink, true));
        var sub = Label(subtitle, 13, Muted);
        sub.SetPadding(0, Dp(2), 0, Dp(16));
        body.AddView(sub);
        scroll.AddView(body);
        return (scroll, body);
    }

    private LinearLayout Card(Color? fill = null, Color? stroke = null, bool gradient = false)
    {
        var card = new LinearLayout(this) { Orientation = Orientation.Vertical };
        card.Background = gradient ? Gradient(Primary, Primary2, 24) : Round(fill ?? Surface, 22, stroke ?? Border);
        card.SetPadding(Dp(16), Dp(16), Dp(16), Dp(16));
        card.Elevation = Dp(2);
        return card;
    }

    private LinearLayout MetricCard(string value, string caption, out TextView valueView)
    {
        var box = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Background = Round(Color.Argb(28, 255, 255, 255), 16, Color.Argb(42, 255, 255, 255))
        };
        box.SetGravity(GravityFlags.Center);
        box.SetPadding(Dp(8), Dp(12), Dp(8), Dp(12));
        valueView = Label(value, 18, Color.White, true);
        valueView.Gravity = GravityFlags.Center;
        var captionView = Label(caption, 11, Color.Rgb(235, 242, 255));
        captionView.Gravity = GravityFlags.Center;
        box.AddView(valueView);
        box.AddView(captionView);
        return box;
    }

    private View FlowStep(string number, string title, string subtitle)
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(0, Dp(6), 0, Dp(6));
        var dot = Pill(number, Tint, Primary, true);
        dot.Gravity = GravityFlags.Center;
        row.AddView(dot, new LinearLayout.LayoutParams(Dp(38), Dp(38)) { RightMargin = Dp(10) });
        var copy = new LinearLayout(this) { Orientation = Orientation.Vertical };
        copy.AddView(Label(title, 14, Ink, true));
        copy.AddView(Label(subtitle, 11, Muted));
        row.AddView(copy, Weight());
        return row;
    }

    private TextView SectionTitle(string text, Color accent)
    {
        var label = Label(text, 16, Ink, true);
        label.SetPadding(0, 0, 0, Dp(10));
        return label;
    }

    private TextView Pill(string text, Color fill, Color textColor, bool bold = false)
    {
        var view = Label(text, 11, textColor, bold);
        view.Gravity = GravityFlags.Center;
        view.Background = Round(fill, 14);
        view.SetPadding(Dp(10), 0, Dp(10), 0);
        return view;
    }

    private TextView Label(string text, float size, Color color, bool bold = false)
    {
        var view = new TextView(this) { Text = text, TextSize = size };
        view.SetTextColor(color);
        if (bold)
            view.SetTypeface(Typeface.Default, TypefaceStyle.Bold);
        return view;
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
            Background = Round(SurfaceSoft, 14, Border)
        };
        input.SetSingleLine(true);
        input.SetTextColor(Ink);
        input.SetHintTextColor(Muted);
        input.SetPadding(Dp(13), Dp(8), Dp(13), Dp(8));
        input.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(52));
        input.FocusChange += (_, e) =>
        {
            if (e.HasFocus)
            {
                input.Background = Round(SurfaceSoft, 14, Primary, 2);
                input.Animate().ScaleX(1.01f).ScaleY(1.01f).SetDuration(120).Start();
            }
            else
            {
                input.Animate().ScaleX(1f).ScaleY(1f).SetDuration(120).Start();
                ApplyValidationHighlights();
            }
        };
        return input;
    }

    private TextView ReadOnly(string text)
    {
        var field = Label(text, 14, Ink);
        field.SetTextIsSelectable(true);
        field.Gravity = GravityFlags.CenterVertical;
        field.SetPadding(Dp(13), Dp(8), Dp(13), Dp(8));
        field.Background = Round(SurfaceSoft, 14, Border);
        field.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(52));
        return field;
    }

    private Button MakeButton(string text, bool primary = false, bool danger = false, bool compact = false)
    {
        var fill = primary ? Primary : danger ? DangerFill : Surface;
        var stroke = primary ? Primary : danger ? Red : Border;
        var color = primary ? Color.White : danger ? Red : Primary;
        var button = new Button(this)
        {
            Text = text,
            TextSize = compact ? 10.5f : 11f,
            Gravity = GravityFlags.Center,
            Clickable = true,
            Focusable = true,
            MinHeight = Dp(compact ? 38 : 46),
            MinWidth = Dp(48)
        };
        button.SetTextColor(color);
        button.Background = Round(fill, compact ? 14 : 16, stroke);
        button.SetPadding(Dp(8), 0, Dp(8), 0);
        return button;
    }

    private void WireButton(Button button, Action action)
    {
        button.Touch += (_, e) =>
        {
            var ev = e.Event;
            if (ev == null)
                return;
            if (ev.ActionMasked == MotionEventActions.Down)
            {
                button.Animate().ScaleX(0.955f).ScaleY(0.955f).Alpha(0.82f).SetDuration(70).Start();
            }
            else if (ev.ActionMasked is MotionEventActions.Up or MotionEventActions.Cancel)
            {
                button.Animate().ScaleX(1f).ScaleY(1f).Alpha(button.Enabled ? 1f : 0.45f).SetDuration(120).Start();
            }
        };
        button.Click += (_, _) =>
        {
            if (!button.Enabled)
                return;
            button.PerformHapticFeedback(FeedbackConstants.KeyboardTap);
            action();
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
    private LinearLayout.LayoutParams CardLayout() => new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { BottomMargin = Dp(14) };

    private GradientDrawable Round(Color fill, int radius, Color? stroke = null, int strokeWidth = 1)
    {
        var drawable = new GradientDrawable();
        drawable.SetColor(fill);
        drawable.SetCornerRadius(Dp(radius));
        if (stroke.HasValue)
            drawable.SetStroke(Dp(strokeWidth), stroke.Value);
        return drawable;
    }

    private GradientDrawable Gradient(Color start, Color end, int radius)
    {
        var drawable = new GradientDrawable(GradientDrawable.Orientation.LeftRight, new[] { start.ToArgb(), end.ToArgb() });
        drawable.SetCornerRadius(Dp(radius));
        return drawable;
    }

    private static ColorDrawable Solid(Color color) => new(color);

    private void ShowPage(int index, bool animate = true)
    {
        _currentPage = System.Math.Clamp(index, 0, _pages.Count - 1);
        for (var i = 0; i < _pages.Count; i++)
            _pages[i].Visibility = i == _currentPage ? ViewStates.Visible : ViewStates.Gone;

        for (var i = 0; i < _navButtons.Count; i++)
        {
            var selected = i == _currentPage;
            _navButtons[i].SetTextColor(selected ? Primary : Muted);
            _navButtons[i].Background = Round(selected ? Tint : Surface, 14, selected ? Color.Transparent : Border);
        }

        if (_currentPage == 2)
            RefreshLog(true);
        if (animate)
            AnimatePageEntrance(_currentPage);
    }

    private void AnimatePageEntrance(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= _pages.Count)
            return;
        var page = _pages[pageIndex];
        page.Alpha = 0f;
        page.TranslationY = Dp(14);
        page.Animate().Alpha(1f).TranslationY(0f).SetDuration(220).Start();

        if (page is ScrollView scroll && scroll.ChildCount > 0 && scroll.GetChildAt(0) is LinearLayout body)
        {
            var max = System.Math.Min(body.ChildCount, 8);
            for (var i = 0; i < max; i++)
            {
                var child = body.GetChildAt(i);
                if (child == null)
                    continue;
                child.Alpha = 0f;
                child.TranslationY = Dp(12);
                var delay = 35L * i;
                child.PostDelayed(new JavaRunnable(() => child.Animate().Alpha(1f).TranslationY(0f).SetDuration(180).Start()), delay);
            }
        }
    }

    private void ToggleLanguage()
    {
        SaveCurrentConfiguration();
        _thai = !_thai;
        ServerPreferences.SaveUi(this, _thai ? "th" : "en", _dark);
        AndroidRuntimeLog.Append("UI", $"Language changed to {(_thai ? "th" : "en")}");
        var page = _currentPage;
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
                ? T("● พร้อมเริ่ม Server", "● Ready to start")
                : T("● ต้องแก้ไขช่องกรอบสีแดงก่อน", "● Complete the red-highlighted fields first");
            _readiness.SetTextColor(ready ? Green : Red);
            _readiness.Background = Round(ready ? SuccessFill : DangerFill, 12, ready ? Green : Red);
            _readiness.SetPadding(Dp(10), Dp(8), Dp(10), Dp(8));
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
        var fill = valid ? SurfaceSoft : DangerFill;
        view.Background = Round(fill, 14, valid ? Border : Red, valid ? 1 : 2);
        if (!valid)
            view.Alpha = 1f;
    }

    private static string MakeWebSocketUrl(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;
        input = input.Trim();
        if (!input.Contains("://", StringComparison.Ordinal))
            input = "https://" + input;
        if (!System.Uri.TryCreate(input, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
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
        if (string.IsNullOrWhiteSpace(websocket) || !System.Uri.TryCreate(websocket, UriKind.Absolute, out var uri))
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
        var value = _webSocketUrl?.Text?.Trim() ?? ServerPreferences.GetBridgeUrl(this);
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
        var backups = ServerPreferences.GetBridgeBackupUrls(this);
        var backupArray = "[" + string.Join(", ", backups.Select(url => $"\"{url.Replace("\"", "\\\"")}\"")) + "]";
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
            $"url = \"{CurrentWebSocket()}\"\n" +
            $"backup_urls = {backupArray}\n" +
            $"server_id = \"{CurrentServerId()}\"\n" +
            $"secret = \"{secret}\"\n" +
            "reconnect_seconds = 5\n" +
            "max_attempts = 5\n" +
            "peer_timeout_seconds = 30\n";
    }

    private List<ValidationIssue> CollectStartIssues()
    {
        var issues = new List<ValidationIssue>();
        if (!TryCurrentPort(out _))
            issues.Add(new ValidationIssue(T("Port ไม่ถูกต้อง", "Invalid port"), T("กรอกเลข 1–65535 ใน Settings", "Enter 1–65535 in Settings"), 3, _port));

        var renderText = _renderUrl?.Text?.Trim() ?? ToServiceUrl(ServerPreferences.GetBridgeUrl(this));
        if (string.IsNullOrEmpty(MakeWebSocketUrl(renderText)))
            issues.Add(new ValidationIssue(T("ยังไม่ได้ใส่ Render Service URL หรือ URL ไม่ถูกต้อง", "Render Service URL is missing or invalid"), T("ไปหน้า Bridge → Render Relay แล้ววาง URL แบบ https://xxxxx.onrender.com", "Open Bridge → Render Relay and paste https://xxxxx.onrender.com"), 1, _renderUrl));

        if (!IsBridgeUrlValid(CurrentWebSocket()))
            issues.Add(new ValidationIssue(T("WebSocket URL ยังสร้างไม่ได้หรือไม่ลงท้าย /bridge", "WebSocket URL is invalid or does not end in /bridge"), T("ใส่ Render Service URL ให้ถูกต้อง แล้วให้แอปสร้าง WSS อัตโนมัติ", "Enter a valid Render Service URL and let the app generate WSS automatically"), 1, _webSocketUrl));

        if (string.IsNullOrWhiteSpace(CurrentServerId()))
            issues.Add(new ValidationIssue(T("Server ID ยังว่าง", "Server ID is empty"), T("กรอก Server ID เช่น mcsv-main", "Enter a Server ID such as mcsv-main"), 1, _serverId));

        if (CurrentSecret().Length < 16)
            issues.Add(new ValidationIssue(T("Bridge Secret ยังว่างหรือสั้นเกินไป", "Bridge Secret is missing or too short"), T("ใช้ Secret อย่างน้อย 16 ตัว และต้องตรงกับ BRIDGE_SECRET บน Render", "Use at least 16 characters and match BRIDGE_SECRET on Render"), 1, _bridgeSecret));
        return issues;
    }

    private void StartServer()
    {
        AndroidRuntimeLog.Append("UI", "START SERVER pressed");
        var issues = CollectStartIssues();
        if (issues.Count > 0)
        {
            AndroidRuntimeLog.Append("UI", $"START BLOCKED: {issues.Count} required setting(s) incomplete");
            ApplyValidationHighlights();
            ShowStartValidationDialog(issues);
            return;
        }

        SaveCurrentConfiguration();
        var port = TryCurrentPort(out var value) ? value : ServerPreferences.GetVoicePort(this);
        var key = _serverKey?.Text?.Trim() ?? ServerPreferences.GetServerKey(this);

        var intent = new Intent(this, typeof(VoiceCraftServerService));
        intent.PutExtra(ServerPreferences.ExtraVoicePort, port);
        intent.PutExtra(ServerPreferences.ExtraServerKey, key);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
#pragma warning disable CA1416
            StartForegroundService(intent);
#pragma warning restore CA1416
        else
            StartService(intent);

        if (_start != null)
            Pulse(_start);
        RefreshUi();
    }

    private void ShowStartValidationDialog(List<ValidationIssue> issues)
    {
        if (issues.Count == 0)
            return;
        var first = issues[0];
        var message = string.Join("\n\n", issues.Select((issue, index) =>
            $"{index + 1}. {issue.Message}\n{T("เพิ่ม/แก้ได้ที่", "Where to fix")}: {PageName(issue.Page)}\n{T("วิธีแก้", "Fix")}: {issue.Fix}"));

        var builder = new AlertDialog.Builder(this)
            .SetTitle(T("ยังเริ่ม Server ไม่ได้", "Server cannot start yet"))
            .SetMessage(message)
            .SetPositiveButton(first.Page == 1 ? T("ไปหน้า Bridge", "GO TO BRIDGE") : T("ไปหน้า Settings", "GO TO SETTINGS"), (_, _) => GoToIssue(first))
            .SetNegativeButton(T("ปิด", "CLOSE"), (_, _) => { });

        if (issues.Any(x => x.Page == 1))
            builder.SetNeutralButton(T("เปิด Render", "OPEN RENDER"), (_, _) => OpenRender());
        builder.Show();
    }

    private string PageName(int page) => page switch
    {
        1 => T("หน้า Bridge Setup", "Bridge Setup"),
        3 => T("หน้า Settings", "Settings"),
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
        view.Background = Round(DangerFill, 14, Red, 3);
        view.TranslationX = -Dp(6);
        view.Animate().TranslationX(0f).ScaleX(1.025f).ScaleY(1.025f).SetDuration(120).Start();
        view.PostDelayed(new JavaRunnable(() =>
        {
            view.Animate().ScaleX(1f).ScaleY(1f).SetDuration(150).Start();
            view.RequestRectangleOnScreen(new Rect(0, 0, System.Math.Max(1, view.Width), System.Math.Max(1, view.Height)), true);
            scroll?.RequestChildFocus(view, view);
        }), 140);
    }

    private void StopServer()
    {
        AndroidRuntimeLog.Append("UI", "STOP SERVER pressed");
        VcServerApp.Shutdown();
        StopService(new Intent(this, typeof(VoiceCraftServerService)));
        if (_stop != null)
            Pulse(_stop);
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
        var visualState = !string.IsNullOrWhiteSpace(VoiceCraftServerService.LastError) ? "error" : running ? "running" : service ? "starting" : "stopped";

        if (_statusTitle != null && _statusDetail != null)
        {
            if (visualState == "error")
            {
                _statusTitle.Text = T("ผิดพลาด", "ERROR");
                _statusDetail.Text = RuntimeDiagnostics.Describe(VoiceCraftServerService.LastError, _thai).Cause;
            }
            else if (visualState == "running")
            {
                _statusTitle.Text = T("กำลังทำงาน", "RUNNING");
                _statusDetail.Text = T($"UDP/TCP {port} • Foreground Service ทำงานอยู่", $"UDP/TCP {port} • foreground service active");
            }
            else if (visualState == "starting")
            {
                _statusTitle.Text = T("กำลังเริ่ม", "STARTING");
                _statusDetail.Text = T($"กำลังเตรียมพอร์ต {port}", $"Preparing port {port}");
            }
            else
            {
                _statusTitle.Text = T("หยุดอยู่", "STOPPED");
                _statusDetail.Text = T("พร้อมเริ่มเมื่อ Render Relay ตั้งค่าครบ", "Ready when Render Relay setup is complete");
            }
            _statusTitle.SetTextColor(Color.White);
        }

        if (_statusBadge != null)
        {
            _statusBadge.Text = visualState switch
            {
                "running" => T("● ออนไลน์", "● ONLINE"),
                "starting" => T("● กำลังเริ่ม", "● STARTING"),
                "error" => T("● ผิดพลาด", "● ERROR"),
                _ => T("● ออฟไลน์", "● OFFLINE")
            };
        }

        if (visualState != _lastVisualState)
        {
            _lastVisualState = visualState;
            if (_statusTitle != null)
                Pulse(_statusTitle);
        }

        if (_address != null)
            _address.Text = $"{ip}:{port}";
        if (_clientCount != null)
            _clientCount.Text = VcServerApp.ConnectedClients.ToString();

        var dashboard = VoiceCraftServerService.BridgeDashboard;
        if (_minecraftCount != null)
            _minecraftCount.Text = dashboard.MinecraftPlayers.ToString();
        if (_boundCount != null)
            _boundCount.Text = dashboard.BoundPlayers.ToString();
        if (_playerList != null)
        {
            if (dashboard.Players.Count == 0)
            {
                _playerList.Text = T("ยังไม่มีผู้เล่น Minecraft ที่ติดตาม", "No tracked Minecraft players yet");
                _playerList.SetTextColor(Muted);
            }
            else
            {
                var rows = dashboard.Players.Take(20).Select(player =>
                {
                    var binding = player.Bound
                        ? $"{T("Bind แล้ว", "BOUND")} • Entity #{player.EntityId}"
                        : T("ยังไม่ Bind", "NOT BOUND");
                    return $"{player.Name}\n{binding}\n{player.Dimension} • {player.X:0.0}, {player.Y:0.0}, {player.Z:0.0}";
                });
                var suffix = dashboard.Players.Count > 20
                    ? T($"\n\nและอีก {dashboard.Players.Count - 20} คน", $"\n\n+ {dashboard.Players.Count - 20} more")
                    : string.Empty;
                _playerList.Text = string.Join("\n\n", rows) + suffix;
                _playerList.SetTextColor(Ink);
            }
        }

        if (_bridgeState != null)
            _bridgeState.Text = ShortBridge(VoiceCraftServerService.BridgeStatus);
        if (_relaySummary != null)
        {
            var wss = CurrentWebSocket();
            var backups = ServerPreferences.GetBridgeBackupUrls(this);
            if (string.IsNullOrEmpty(wss))
            {
                _relaySummary.Text = T("ยังไม่ได้ตั้งค่า Relay", "Relay not configured");
                _relaySummary.SetTextColor(Red);
            }
            else if (VcServerApp.IsRunning || VoiceCraftServerService.IsServiceRunning)
            {
                _relaySummary.Text = $"{VoiceCraftServerService.BridgeActiveRelay} • Relay {VoiceCraftServerService.BridgeActiveRelayIndex + 1}/{VoiceCraftServerService.BridgeRelayCount}\n{wss}\n{T("Backup", "Backups")}: {backups.Count}";
                _relaySummary.SetTextColor(Ink);
            }
            else
            {
                _relaySummary.Text = $"Primary: {wss}\n{T("Backup (Optional)", "Backups (Optional)")}: {backups.Count}";
                _relaySummary.SetTextColor(Ink);
            }
        }

        if (_start != null)
        {
            _start.Enabled = !running && !service;
            _start.Alpha = _start.Enabled ? 1f : 0.45f;
        }
        if (_stop != null)
        {
            _stop.Enabled = running || service;
            _stop.Alpha = _stop.Enabled ? 1f : 0.45f;
        }
        if (_navPower != null)
        {
            var active = running || service;
            _navPower.Text = active ? T("หยุด", "STOP") : T("เริ่ม", "START");
            _navPower.SetTextColor(Color.White);
            _navPower.Background = Round(active ? Red : Primary, 18, active ? Red : Primary);
        }

        RefreshErrorCard();
        MaybeShowRuntimeError();
        RefreshBridgePreview();
        RefreshLog();
    }

    private void RefreshErrorCard()
    {
        var error = VoiceCraftServerService.LastError;
        if (string.IsNullOrWhiteSpace(error))
            error = VoiceCraftServerService.BridgeLastError;

        if (_errorCard != null && _errorText != null)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                _errorCard.Visibility = ViewStates.Gone;
            }
            else
            {
                var advice = RuntimeDiagnostics.Describe(error, _thai);
                _errorText.Text = $"{advice.Title}\n{T("สาเหตุ", "Cause")}: {advice.Cause}\n{T("วิธีแก้", "Fix")}: {advice.Fix}";
                _errorCard.Visibility = ViewStates.Visible;
            }
        }

        if (_logHelp != null)
        {
            if (string.IsNullOrWhiteSpace(error))
                _logHelp.Text = T("ยังไม่พบ Error ที่ต้องแก้", "No current error requiring action");
            else
            {
                var advice = RuntimeDiagnostics.Describe(error, _thai);
                _logHelp.Text = $"{advice.Title}\n\n{T("สาเหตุ", "Cause")}: {advice.Cause}\n\n{T("วิธีแก้", "Fix")}: {advice.Fix}";
                _logHelp.SetTextColor(Red);
            }
        }
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
        var configProblem = error.Contains("CONFIG_REQUIRED", StringComparison.OrdinalIgnoreCase) || error.Contains("invalid-config", StringComparison.OrdinalIgnoreCase);
        var builder = new AlertDialog.Builder(this)
            .SetTitle(bridge ? T("Bridge มีปัญหา", "Bridge problem") : T("Server เกิดข้อผิดพลาด", "Server error"))
            .SetMessage($"{advice.Title}\n\n{T("สาเหตุ", "Cause")}:\n{advice.Cause}\n\n{T("วิธีแก้", "Fix")}:\n{advice.Fix}")
            .SetPositiveButton(configProblem ? T("ไปหน้า Bridge", "GO TO BRIDGE") : T("เปิด Log", "OPEN LOGS"), (_, _) => ShowPage(configProblem ? 1 : 2))
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

    private void Pulse(View view)
    {
        view.ScaleX = 0.96f;
        view.ScaleY = 0.96f;
        view.Alpha = 0.75f;
        view.Animate().ScaleX(1f).ScaleY(1f).Alpha(1f).SetDuration(220).Start();
    }

    private void CopyIp() => Copy("VoiceCraft LAN IP", GetLanIpv4() ?? "0.0.0.0");
    private void CopyPort() => Copy("VoiceCraft port", (TryCurrentPort(out var port) ? port : ServerPreferences.GetVoicePort(this)).ToString());
    private void CopyAddress() => Copy("VoiceCraft address", $"{GetLanIpv4() ?? "0.0.0.0"}:{(TryCurrentPort(out var port) ? port : ServerPreferences.GetVoicePort(this))}");

    private void CopyWebSocket()
    {
        if (string.IsNullOrEmpty(CurrentWebSocket()))
        {
            var issues = CollectStartIssues().Where(x => x.Page == 1).ToList();
            if (issues.Count > 0)
                ShowStartValidationDialog(issues);
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
        var key = _serverKey?.Text?.Trim() ?? ServerPreferences.GetServerKey(this);
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
        Toast.MakeText(this, secret ? T("คัดลอกแล้ว — Clipboard มีข้อมูลลับ", "Copied — clipboard contains a secret") : T("คัดลอกแล้ว ✓", "Copied ✓"), secret ? ToastLength.Long : ToastLength.Short)?.Show();
    }

    private void GenerateBridgeSecret()
    {
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        if (_bridgeSecret != null)
        {
            _bridgeSecret.Text = secret;
            _bridgeSecret.SetSelection(secret.Length);
            Pulse(_bridgeSecret);
        }
        Toast.MakeText(this, T("สร้าง Secret แล้ว — ใช้ค่าเดียวกันกับ BRIDGE_SECRET บน Render", "Secret generated — use the same value for BRIDGE_SECRET on Render"), ToastLength.Long)?.Show();
    }

    private void GenerateServerKey()
    {
        var key = Guid.NewGuid().ToString("N");
        if (_serverKey != null)
        {
            _serverKey.Text = key;
            _serverKey.SetSelection(key.Length);
            Pulse(_serverKey);
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
        Pulse(_bridgeSecret);
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
        Pulse(_serverKey);
    }

    private void OpenBackupRelays()
    {
        SaveCurrentConfiguration();
        StartActivity(new Intent(this, typeof(BackupRelayActivity)));
    }

    private void OpenRender()
    {
        var serviceUrl = _renderUrl?.Text?.Trim() ?? ToServiceUrl(ServerPreferences.GetBridgeUrl(this));
        if (!System.Uri.TryCreate(serviceUrl, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https"))
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
        var wrap = new LinearLayout(this) { Orientation = Orientation.Vertical };
        wrap.SetPadding(Dp(18), Dp(12), Dp(18), Dp(18));
        wrap.AddView(Label(T("เริ่มจาก Render → Android → Endstone → VoiceCraft Client → /vc", "Start with Render → Android → Endstone → VoiceCraft Client → /vc"), 14, Primary, true));
        var guide = Label(_thai ? ThaiGuide() : EnglishGuide(), 13, Ink);
        guide.SetPadding(0, Dp(12), 0, 0);
        guide.SetLineSpacing(0, 1.12f);
        wrap.AddView(guide);
        scroll.AddView(wrap);

        new AlertDialog.Builder(this)
            .SetTitle(T("คู่มือใช้งานแบบละเอียด", "Detailed Setup Guide"))
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
        "• ไปหน้า Bridge แล้ววาง Render Service URL เช่น https://voicecraft-server-mobile.onrender.com\n" +
        "• แอปจะสร้าง wss://.../bridge ให้อัตโนมัติ\n" +
        "• Server ID ใช้ mcsv-main ได้ถ้ามีเซิร์ฟเวอร์เดียว\n" +
        "• Bridge Secret ต้องตรงกับ BRIDGE_SECRET บน Render\n" +
        "• ช่องที่ขาดหรือผิดจะเป็นกรอบสีแดง\n\n" +
        "3) Endstone บน MCSV\n" +
        "• ใช้ Endstone 0.11.x\n" +
        "• อัปโหลด endstone_voicecraft-0.2.5-py3-none-any.whl ไป plugins/\n" +
        "• Start หนึ่งครั้งเพื่อสร้างไฟล์ config\n" +
        "• กดคัดลอก Plugin Config ในแอป แล้วนำไปแทน config.toml\n" +
        "• Restart Minecraft Server แล้วตรวจว่า BRIDGE connected / android=connected\n\n" +
        "4) เริ่ม VoiceCraft Server\n" +
        "• กด เริ่ม ที่หน้า Home หรือปุ่มตรงกลางด้านล่าง\n" +
        "• ระบบจะตรวจ Render URL, WebSocket, Server ID, Secret และ Port ก่อนเริ่ม\n" +
        "• ถ้าขาดแม้แต่รายการเดียว จะมี Popup บอกว่าขาดอะไร อยู่หน้าไหน และพาไปแก้\n\n" +
        "5) VoiceCraft Client\n" +
        "• ตอนทดสอบให้ Client อยู่ LAN เดียวกับ Android\n" +
        "• ใช้ VoiceCraft Client 1.7.x ที่เข้ากันได้\n" +
        "• ใส่ IP:Port จากหน้า Home และตั้ง Positioning Type = Server\n" +
        "• เชื่อมแล้วอ่าน Binding Key 5 ตัวจาก Description\n\n" +
        "6) Bind ใน Minecraft\n" +
        "• ใช้ /vc แล้วเลือก Bind Microphone จากนั้นกรอก Binding Key ของคุณ\n\n" +
        "7) ข้อจำกัดปัจจุบัน\n" +
        "• Render WSS ส่ง state + binding เท่านั้น\n" +
        "• เสียงยังใช้ UDP ไป Android โดยตรง ผู้เล่นนอก LAN ยังต้องมี public UDP relay ใน Phase ถัดไป\n\n" +
        "ถ้าเกิด Error ให้เปิดหน้า Log ระบบจะพยายามบอกสาเหตุและวิธีแก้ให้";

    private string EnglishGuide() =>
        "Render Relay must be fully configured before the server can start.\n\n" +
        "1) Create the Render relay\n" +
        "• Render → New Web Service → select VoiceCraft-Server-Mobile.\n" +
        "• Root Directory: VoiceCraft.Bridge.Relay\n" +
        "• Runtime: Node\n" +
        "• Build Command: npm install --omit=dev\n" +
        "• Start Command: npm start\n" +
        "• Health Check Path: /health\n" +
        "• Environment Variable: BRIDGE_SECRET=<strong secret>\n" +
        "• Deploy until Live.\n\n" +
        "2) Configure Android\n" +
        "• Open Bridge Setup and paste the Render service URL.\n" +
        "• The app automatically creates wss://.../bridge.\n" +
        "• Server ID can remain mcsv-main for one server.\n" +
        "• Bridge Secret must exactly match BRIDGE_SECRET on Render.\n" +
        "• Missing or invalid fields are highlighted red.\n\n" +
        "3) Configure Endstone on MCSV\n" +
        "• Use Endstone 0.11.x and upload endstone_voicecraft-0.2.5-py3-none-any.whl to plugins/.\n" +
        "• Start once, then replace plugin config.toml with the ready-to-paste config copied from this app.\n" +
        "• Restart Minecraft and verify BRIDGE connected / android=connected.\n\n" +
        "4) Start VoiceCraft Server\n" +
        "• Tap Start on Home or the center bottom button.\n" +
        "• The app checks Render URL, generated WebSocket, Server ID, Secret and Port before launching anything.\n" +
        "• Missing setup triggers a popup explaining what is missing, where to fix it, and a shortcut to that field.\n\n" +
        "5) Connect VoiceCraft Client\n" +
        "• For current testing, keep the client on the same LAN as Android.\n" +
        "• Use a compatible VoiceCraft 1.7.x client, connect to Home IP:Port, and set Positioning Type = Server.\n\n" +
        "6) Bind in Minecraft\n" +
        "• Run /vc, choose Bind Microphone, and enter your actual 5-character binding key.\n\n" +
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
        if (!System.Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("ws" or "wss"))
            return false;
        return string.Equals(uri.AbsolutePath.TrimEnd('/'), "/bridge", StringComparison.OrdinalIgnoreCase);
    }

    private void StartRefreshLoop()
    {
        _handler = new Handler(Looper.MainLooper!);
        _refreshRunnable = new JavaRunnable(() =>
        {
            RefreshUi();
            if (_handler != null && _refreshRunnable != null)
                _handler.PostDelayed(_refreshRunnable, 750);
        });
        _handler.Post(_refreshRunnable);
    }

    protected override void OnResume()
    {
        base.OnResume();
        RefreshBridgePreview();
        RefreshUi();
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
