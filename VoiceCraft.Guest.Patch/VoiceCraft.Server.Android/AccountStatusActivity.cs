using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace VoiceCraft.Server.Android;

[Activity(
    Name = "chat.voicecraft.server.AccountStatusActivity",
    Label = "VoiceCraft Account",
    Exported = false)]
public sealed class AccountStatusActivity : Activity
{
    private static readonly Color Primary = Color.Rgb(73, 116, 255);
    private static readonly Color Green = Color.Rgb(34, 197, 94);
    private static readonly Color Amber = Color.Rgb(245, 158, 11);
    private static readonly Color Red = Color.Rgb(239, 68, 68);

    private bool _thai;
    private bool _dark;
    private float _density = 1f;

    private Color Page => _dark ? Color.Rgb(12, 18, 32) : Color.Rgb(247, 248, 255);
    private Color Surface => _dark ? Color.Rgb(22, 30, 48) : Color.White;
    private Color SurfaceSoft => _dark ? Color.Rgb(28, 38, 60) : Color.Rgb(248, 250, 255);
    private Color Tint => _dark ? Color.Rgb(35, 48, 82) : Color.Rgb(237, 242, 255);
    private Color Ink => _dark ? Color.Rgb(246, 248, 255) : Color.Rgb(35, 39, 54);
    private Color Muted => _dark ? Color.Rgb(160, 170, 190) : Color.Rgb(116, 124, 145);
    private Color Border => _dark ? Color.Rgb(51, 63, 87) : Color.Rgb(228, 232, 244);
    private Color WarningFill => _dark ? Color.Rgb(66, 49, 22) : Color.Rgb(255, 249, 235);
    private Color DangerFill => _dark ? Color.Rgb(64, 29, 36) : Color.Rgb(255, 242, 244);

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ActionBar?.Hide();
        _density = Resources?.DisplayMetrics?.Density ?? 1f;
        _thai = ServerPreferences.GetLanguage(this) == "th";
        _dark = ServerPreferences.GetDarkTheme(this);
        ApplySystemBars();
        BuildUi();
    }

    private string T(string thai, string english) => _thai ? thai : english;

    private void ApplySystemBars()
    {
        Window?.SetStatusBarColor(_dark ? Color.Rgb(8, 12, 23) : Color.Rgb(240, 244, 255));
        Window?.SetNavigationBarColor(_dark ? Page : Color.White);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.M && Window?.DecorView is View decor)
        {
#pragma warning disable CA1416
            decor.SystemUiVisibility = _dark
                ? (StatusBarVisibility)0
                : (StatusBarVisibility)SystemUiFlags.LightStatusBar;
#pragma warning restore CA1416
        }
    }

    private void BuildUi()
    {
        var scroll = new ScrollView(this)
        {
            FillViewport = true,
            Background = Solid(Page)
        };

        var root = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Background = Solid(Page)
        };
        root.SetGravity(GravityFlags.CenterHorizontal);
        root.SetPadding(Dp(16), Dp(18), Dp(16), Dp(36));

        var widthDp = (Resources?.DisplayMetrics?.WidthPixels ?? Dp(400)) / _density;
        var contentWidth = Dp((int)System.Math.Min(560f, System.Math.Max(288f, widthDp - 32f)));
        var column = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.AddView(column, new LinearLayout.LayoutParams(contentWidth, ViewGroup.LayoutParams.WrapContent));

        var top = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        top.SetGravity(GravityFlags.CenterVertical);
        var back = ActionButton(T("‹ กลับ", "‹ BACK"), Finish, false, compact: true);
        top.AddView(back, new LinearLayout.LayoutParams(Dp(96), Dp(42)));
        top.AddView(new Space(this), new LinearLayout.LayoutParams(0, 1, 1f));
        var title = Text(T("บัญชี", "Account"), 22, true, Ink);
        top.AddView(title);
        column.AddView(top);

        var intro = Card();
        intro.SetPadding(Dp(18), Dp(18), Dp(18), Dp(18));
        var logo = new ImageView(this);
        logo.SetImageResource(Resource.Drawable.voicecraft_logo);
        logo.SetAdjustViewBounds(true);
        logo.SetScaleType(ImageView.ScaleType.FitCenter);
        intro.AddView(logo, new LinearLayout.LayoutParams(Dp(62), Dp(62)));
        var heading = Text(T("บัญชี VoiceCraft ที่กำลังใช้งาน", "Active VoiceCraft Account"), 20, true, Ink);
        heading.SetPadding(0, Dp(12), 0, 0);
        intro.AddView(heading);
        intro.AddView(Text(T("จัดการการเข้าสู่ระบบหรือสลับไปใช้ Free Account ได้จากหน้านี้", "Manage your sign-in or switch to a Free Account from here"), 12, false, Muted), Wrap(Dp(6)));
        column.AddView(intro, CardLayout(Dp(14)));

        if (RegisteredSessionStore.TryLoad(this, out var session) && session != null)
        {
            var card = Card();
            card.AddView(StatusPill(T("● บัญชีที่ลงทะเบียน", "● REGISTERED ACCOUNT"), Green));
            card.AddView(Text(session.LoginName, 22, true, Ink), Wrap(Dp(14)));
            card.AddView(Text(
                T(
                    $"Session ใช้งานได้ถึง {session.ExpiresAt.LocalDateTime:g}",
                    $"Session expires {session.ExpiresAt.LocalDateTime:g}"),
                12,
                false,
                Muted),
                Wrap(Dp(5)));
            column.AddView(card, CardLayout(Dp(12)));

            var actions = Card();
            actions.AddView(Text(T("การจัดการบัญชี", "Account Actions"), 15, true, Ink));
            actions.AddView(ActionButton(T("กลับไป VoiceCraft", "CONTINUE TO VOICECRAFT"), OpenVoiceCraft, true), Full(Dp(54), Dp(14)));
            actions.AddView(ActionButton(T("สลับเป็น Free Account", "SWITCH TO FREE ACCOUNT"), SwitchToFree, false), Full(Dp(54), Dp(8)));
            actions.AddView(ActionButton(T("ออกจากระบบ", "LOG OUT"), () => _ = LogoutAsync(session), false, danger: true), Full(Dp(54), Dp(8)));
            column.AddView(actions, CardLayout());
        }
        else if (GuestAccountStore.TryLoad(this, out var guest) && guest != null)
        {
            var card = Card();
            card.AddView(StatusPill(T("● FREE ACCOUNT", "● FREE ACCOUNT"), Primary));
            card.AddView(Text(guest.GuestId, 18, true, Ink), Wrap(Dp(14)));
            card.AddView(Text(T("เก็บเฉพาะในอุปกรณ์นี้", "Stored only on this device"), 12, false, Muted), Wrap(Dp(5)));
            column.AddView(card, CardLayout(Dp(12)));

            var actions = Card();
            actions.AddView(Text(T("ตัวเลือก", "Options"), 15, true, Ink));
            actions.AddView(ActionButton(T("กลับไป VoiceCraft", "CONTINUE TO VOICECRAFT"), OpenVoiceCraft, true), Full(Dp(54), Dp(14)));
            actions.AddView(ActionButton(
                T("เข้าสู่ระบบด้วยบัญชี VoiceCraft", "LOGIN WITH VOICECRAFT ACCOUNT"),
                () => StartActivity(new Intent(this, typeof(RegisteredLoginActivity))),
                false),
                Full(Dp(54), Dp(8)));
            column.AddView(actions, CardLayout(Dp(12)));

            var warning = Card(WarningFill, Amber);
            warning.AddView(Text(T("ข้อมูล Free Account อยู่ในเครื่องนี้เท่านั้น", "Free Account data stays on this device only"), 13, true, Ink));
            warning.AddView(Text(T("Clear App Data หรือถอนการติดตั้งแอปจะลบ Guest ID นี้ถาวร", "Clearing app data or uninstalling the app permanently removes this Guest ID"), 11, false, Muted), Wrap(Dp(5)));
            column.AddView(warning, CardLayout());
        }
        else
        {
            var empty = Card(DangerFill, Red);
            empty.AddView(Text(T("ยังไม่มีบัญชีที่ใช้งาน", "No active account"), 16, true, Ink));
            empty.AddView(Text(T("เปิดหน้าตั้งค่าบัญชีเพื่อเข้าสู่ระบบหรือสร้าง Free Account", "Open account setup to sign in or create a Free Account"), 12, false, Muted), Wrap(Dp(6)));
            empty.AddView(ActionButton(T("เปิดหน้าบัญชี", "OPEN ACCOUNT SETUP"), OpenGate, true), Full(Dp(54), Dp(14)));
            column.AddView(empty, CardLayout());
        }

        scroll.AddView(root);
        SetContentView(scroll);
    }

    private async Task LogoutAsync(RegisteredSession session)
    {
        await VoiceCraftAccountClient.LogoutAsync(session);
        RegisteredSessionStore.Delete(this);
        OpenGate();
    }

    private void SwitchToFree()
    {
        RegisteredSessionStore.Delete(this);
        GuestAccountStore.GetOrCreate(this);
        OpenVoiceCraft();
    }

    private void OpenGate()
    {
        var intent = new Intent(this, typeof(AccountGateActivity));
        intent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTask);
        StartActivity(intent);
        Finish();
    }

    private void OpenVoiceCraft()
    {
        var intent = new Intent(this, typeof(ModernMainActivity));
        intent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTask);
        StartActivity(intent);
        Finish();
    }

    private LinearLayout Card(Color? fill = null, Color? stroke = null)
    {
        var card = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Background = Round(fill ?? Surface, 20, stroke ?? Border)
        };
        card.SetPadding(Dp(18), Dp(18), Dp(18), Dp(18));
        card.Elevation = Dp(2);
        return card;
    }

    private TextView StatusPill(string value, Color accent)
    {
        var text = Text(value, 10, true, accent);
        text.Gravity = GravityFlags.Center;
        text.Background = Round(Tint, 14, accent);
        text.SetPadding(Dp(10), 0, Dp(10), 0);
        text.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, Dp(30));
        return text;
    }

    private Button ActionButton(string text, Action action, bool primary, bool compact = false, bool danger = false)
    {
        var button = new Button(this)
        {
            Text = text,
            TextSize = compact ? 10.5f : 11f,
            Gravity = GravityFlags.Center,
            MinHeight = Dp(compact ? 40 : 50)
        };
        button.SetTypeface(Typeface.Default, TypefaceStyle.Bold);
        button.SetTextColor(primary ? Color.White : danger ? Red : Primary);
        button.Background = primary
            ? Round(Primary, 15, Primary)
            : danger
                ? Round(DangerFill, 15, Red)
                : Round(SurfaceSoft, 15, Border);
        button.Click += (_, _) => action();
        return button;
    }

    private TextView Text(string value, float size, bool bold, Color color)
    {
        var view = new TextView(this)
        {
            Text = value,
            TextSize = size
        };
        view.SetTextColor(color);
        if (bold)
            view.SetTypeface(Typeface.Default, TypefaceStyle.Bold);
        return view;
    }

    private GradientDrawable Round(Color fill, int radiusDp, Color stroke, int strokeWidthDp = 1)
    {
        var drawable = new GradientDrawable();
        drawable.SetColor(fill);
        drawable.SetCornerRadius(Dp(radiusDp));
        drawable.SetStroke(Dp(strokeWidthDp), stroke);
        return drawable;
    }

    private static ColorDrawable Solid(Color color) => new(color);

    private LinearLayout.LayoutParams Full(int height, int top = 0) =>
        new(ViewGroup.LayoutParams.MatchParent, height)
        {
            TopMargin = top
        };

    private LinearLayout.LayoutParams Wrap(int top = 0) =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = top
        };

    private LinearLayout.LayoutParams CardLayout(int top = 0) =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = top,
            BottomMargin = Dp(4)
        };

    private int Dp(int value) => (int)(value * _density + 0.5f);
}
