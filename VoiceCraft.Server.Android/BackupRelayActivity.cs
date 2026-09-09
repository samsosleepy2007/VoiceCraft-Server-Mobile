using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;

namespace VoiceCraft.Server.Android;

[Activity(
    Name = "chat.voicecraft.server.BackupRelayActivity",
    Label = "VoiceCraft Backup Relays",
    Exported = false)]
public sealed class BackupRelayActivity : Activity
{
    private readonly List<EditText> _backupInputs = new();
    private LinearLayout? _list;
    private TextView? _summary;
    private float _density = 1f;
    private bool _dark;

    private Color Page => _dark ? Color.Rgb(12, 18, 32) : Color.Rgb(247, 248, 255);
    private Color Surface => _dark ? Color.Rgb(22, 30, 48) : Color.White;
    private Color SurfaceSoft => _dark ? Color.Rgb(28, 38, 60) : Color.Rgb(248, 250, 255);
    private Color Ink => _dark ? Color.Rgb(246, 248, 255) : Color.Rgb(35, 39, 54);
    private Color Muted => _dark ? Color.Rgb(160, 170, 190) : Color.Rgb(116, 124, 145);
    private Color Border => _dark ? Color.Rgb(51, 63, 87) : Color.Rgb(228, 232, 244);
    private static readonly Color Primary = Color.Rgb(73, 116, 255);
    private static readonly Color Red = Color.Rgb(239, 68, 68);

    private int Dp(int value) => (int)(value * _density + 0.5f);
    private bool Thai => ServerPreferences.GetLanguage(this) == "th";
    private string T(string thai, string english) => Thai ? thai : english;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ActionBar?.Hide();
        _density = Resources?.DisplayMetrics?.Density ?? 1f;
        _dark = ServerPreferences.GetDarkTheme(this);
        BuildUi();
    }

    private void BuildUi()
    {
        var scroll = new ScrollView(this)
        {
            FillViewport = true,
            Background = Solid(Page)
        };
        var body = new LinearLayout(this) { Orientation = Orientation.Vertical };
        body.SetPadding(Dp(18), Dp(24), Dp(18), Dp(32));

        body.AddView(Label(T("ลิงก์ Render สำรอง", "Backup Render Relays"), 25, Ink, true));
        body.AddView(Label(
            T(
                "Primary ยังอยู่ในหน้า Bridge เดิม ส่วนหน้านี้เพิ่ม Backup ได้ตามต้องการ และไม่บังคับต้องมี Backup",
                "Primary stays on the normal Bridge page. Add as many optional backups here as needed."),
            13,
            Muted));

        var primaryCard = Card();
        primaryCard.AddView(Label("Primary Relay", 16, Ink, true));
        var primary = ServerPreferences.GetBridgeUrl(this);
        primaryCard.AddView(Label(
            string.IsNullOrWhiteSpace(primary) ? T("ยังไม่ได้ตั้งค่า Primary", "Primary is not configured") : primary,
            12,
            string.IsNullOrWhiteSpace(primary) ? Red : Ink));
        primaryCard.AddView(Label(
            T(
                "Backup ทุกตัวใช้ Server ID และ Bridge Secret ชุดเดียวกับ Primary",
                "Every backup uses the same Server ID and Bridge Secret as Primary."),
            12,
            Muted));
        body.AddView(primaryCard, LayoutWithTop(16));

        var failoverCard = Card();
        failoverCard.AddView(Label(T("ลำดับ Failover", "Failover order"), 16, Ink, true));
        failoverCard.AddView(Label(
            T(
                "แต่ละ Relay จะลองสูงสุด 5 ครั้ง ก่อนเลื่อนไปตัวถัดไป เมื่อถึงตัวสุดท้ายจะวนกลับ Primary โดยไม่ restart VoiceCraft UDP Server",
                "Each relay is tried up to 5 times before moving to the next. After the last backup it wraps to Primary without restarting the VoiceCraft UDP server."),
            12,
            Muted));
        body.AddView(failoverCard, LayoutWithTop(12));

        var backupsCard = Card();
        backupsCard.AddView(Label(T("Backup Relays (ไม่บังคับ)", "Backup Relays (Optional)"), 16, Ink, true));
        _summary = Label(string.Empty, 12, Muted);
        backupsCard.AddView(_summary);

        _list = new LinearLayout(this) { Orientation = Orientation.Vertical };
        backupsCard.AddView(_list, LayoutWithTop(8));

        var add = Button(T("+ เพิ่มลิงก์สำรอง", "+ ADD BACKUP RELAY"), true);
        add.Click += (_, _) => AddBackupRow(string.Empty);
        backupsCard.AddView(add, LayoutWithTop(12, 52));
        body.AddView(backupsCard, LayoutWithTop(12));

        var actions = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var cancel = Button(T("ยกเลิก", "CANCEL"), false);
        cancel.Click += (_, _) => Finish();
        var save = Button(T("บันทึก", "SAVE BACKUPS"), true);
        save.Click += (_, _) => SaveBackups();
        actions.AddView(cancel, new LinearLayout.LayoutParams(0, Dp(52), 1f) { RightMargin = Dp(4) });
        actions.AddView(save, new LinearLayout.LayoutParams(0, Dp(52), 1f) { LeftMargin = Dp(4) });
        body.AddView(actions, LayoutWithTop(16));

        scroll.AddView(body);
        SetContentView(scroll);

        foreach (var url in ServerPreferences.GetBridgeBackupUrls(this))
            AddBackupRow(ToServiceUrl(url));
        RefreshSummary();
    }

    private void AddBackupRow(string value)
    {
        if (_list == null)
            return;

        var wrapper = new LinearLayout(this) { Orientation = Orientation.Vertical };
        wrapper.SetPadding(0, Dp(8), 0, Dp(8));

        var titleRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var title = Label($"Backup #{_backupInputs.Count + 1}", 13, Ink, true);
        titleRow.AddView(title, new LinearLayout.LayoutParams(0, Dp(42), 1f));
        var remove = Button(T("ลบ", "REMOVE"), false, danger: true);
        titleRow.AddView(remove, new LinearLayout.LayoutParams(Dp(112), Dp(42)));
        wrapper.AddView(titleRow);

        var input = new EditText(this)
        {
            Text = value,
            Hint = "https://backup.onrender.com",
            InputType = InputTypes.ClassText | InputTypes.TextVariationUri,
            TextSize = 13,
            Background = Round(SurfaceSoft, 14, Border)
        };
        input.SetSingleLine(true);
        input.SetTextColor(Ink);
        input.SetHintTextColor(Muted);
        input.SetPadding(Dp(12), Dp(7), Dp(12), Dp(7));
        wrapper.AddView(input, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(52)));

        _backupInputs.Add(input);
        _list.AddView(wrapper);
        remove.Click += (_, _) =>
        {
            _backupInputs.Remove(input);
            _list.RemoveView(wrapper);
            RenumberRows();
            RefreshSummary();
        };
        input.TextChanged += (_, _) => RefreshSummary();
        RefreshSummary();
    }

    private void RenumberRows()
    {
        if (_list == null)
            return;
        for (var i = 0; i < _list.ChildCount; i++)
        {
            if (_list.GetChildAt(i) is LinearLayout wrapper &&
                wrapper.GetChildAt(0) is LinearLayout titleRow &&
                titleRow.GetChildAt(0) is TextView title)
                title.Text = $"Backup #{i + 1}";
        }
    }

    private void RefreshSummary()
    {
        if (_summary == null)
            return;
        var filled = _backupInputs.Count(x => !string.IsNullOrWhiteSpace(x.Text));
        _summary.Text = filled == 0
            ? T("ยังไม่มี Backup — ระบบยังเปิด Server ด้วย Primary อย่างเดียวได้", "No backups configured — Primary-only startup remains valid.")
            : T($"ตั้งค่า Backup อยู่ {filled} ตัว", $"{filled} backup relay(s) configured");
    }

    private void SaveBackups()
    {
        var primary = ServerPreferences.GetBridgeUrl(this);
        var normalized = new List<string>();
        var invalid = new List<int>();

        for (var i = 0; i < _backupInputs.Count; i++)
        {
            var raw = _backupInputs[i].Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var websocket = MakeWebSocketUrl(raw);
            if (string.IsNullOrWhiteSpace(websocket))
            {
                invalid.Add(i + 1);
                continue;
            }
            if (string.Equals(websocket, primary, StringComparison.OrdinalIgnoreCase))
            {
                invalid.Add(i + 1);
                continue;
            }
            if (!normalized.Contains(websocket, StringComparer.OrdinalIgnoreCase))
                normalized.Add(websocket);
        }

        if (invalid.Count > 0)
        {
            new AlertDialog.Builder(this)
                .SetTitle(T("ลิงก์สำรองไม่ถูกต้อง", "Invalid backup relay"))
                .SetMessage(T(
                    $"ตรวจ Backup #{string.Join(", #", invalid)} — ต้องเป็น URL ที่ถูกต้องและห้ามซ้ำกับ Primary",
                    $"Check Backup #{string.Join(", #", invalid)} — each URL must be valid and different from Primary."))
                .SetPositiveButton("OK", (_, _) => { })
                .Show();
            return;
        }

        ServerPreferences.SaveBridgeBackups(this, normalized);
        AndroidRuntimeLog.Append("BRIDGE", $"Saved optional backup relay list count={normalized.Count}");
        Toast.MakeText(
            this,
            normalized.Count == 0
                ? T("บันทึกแล้ว — ใช้ Primary อย่างเดียว", "Saved — Primary only")
                : T($"บันทึก Backup {normalized.Count} ตัวแล้ว", $"Saved {normalized.Count} backup relay(s)"),
            ToastLength.Short)?.Show();
        Finish();
    }

    private LinearLayout Card()
    {
        var card = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Background = Round(Surface, 20, Border)
        };
        card.SetPadding(Dp(15), Dp(15), Dp(15), Dp(15));
        return card;
    }

    private TextView Label(string text, float size, Color color, bool bold = false)
    {
        var view = new TextView(this) { Text = text, TextSize = size };
        view.SetTextColor(color);
        if (bold)
            view.SetTypeface(global::Android.Graphics.Typeface.Default, global::Android.Graphics.TypefaceStyle.Bold);
        return view;
    }

    private Button Button(string text, bool primary, bool danger = false)
    {
        var button = new Button(this) { Text = text, TextSize = 11f };
        button.SetTextColor(primary ? Color.White : danger ? Red : Primary);
        button.Background = Round(primary ? Primary : SurfaceSoft, 14, danger ? Red : primary ? Primary : Border);
        return button;
    }

    private LinearLayout.LayoutParams LayoutWithTop(int top, int height = -2) =>
        new(ViewGroup.LayoutParams.MatchParent, height) { TopMargin = Dp(top) };

    private GradientDrawable Round(Color fill, int radius, Color stroke)
    {
        var drawable = new GradientDrawable();
        drawable.SetColor(fill);
        drawable.SetCornerRadius(Dp(radius));
        drawable.SetStroke(Dp(1), stroke);
        return drawable;
    }

    private ColorDrawable Solid(Color color) => new(color);

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
        if (string.IsNullOrWhiteSpace(scheme))
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
}
