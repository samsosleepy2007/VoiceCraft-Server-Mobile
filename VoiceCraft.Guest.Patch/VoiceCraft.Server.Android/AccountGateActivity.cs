using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace VoiceCraft.Server.Android;

[Activity(
    Name = "chat.voicecraft.server.AccountGateActivity",
    Label = "VoiceCraft Server",
    MainLauncher = true,
    Exported = true)]
public sealed class AccountGateActivity : Activity
{
    private static readonly Color Primary = Color.Rgb(73, 116, 255);
    private static readonly Color Primary2 = Color.Rgb(105, 86, 255);
    private static readonly Color Amber = Color.Rgb(245, 158, 11);

    private Button? _guestButton;
    private Button? _loginButton;
    private LinearLayout? _statusCard;
    private TextView? _status;
    private bool _thai;
    private bool _dark;
    private float _density = 1f;

    private Color Page => _dark ? Color.Rgb(12, 18, 32) : Color.Rgb(247, 248, 255);
    private Color Surface => _dark ? Color.Rgb(22, 30, 48) : Color.White;
    private Color SurfaceSoft => _dark ? Color.Rgb(28, 38, 60) : Color.Rgb(248, 250, 255);
    private Color Tint => _dark ? Color.Rgb(35, 48, 82) : Color.Rgb(237, 242, 255);
    private Color Tint2 => _dark ? Color.Rgb(45, 38, 80) : Color.Rgb(245, 241, 255);
    private Color Ink => _dark ? Color.Rgb(246, 248, 255) : Color.Rgb(35, 39, 54);
    private Color Muted => _dark ? Color.Rgb(160, 170, 190) : Color.Rgb(116, 124, 145);
    private Color Border => _dark ? Color.Rgb(51, 63, 87) : Color.Rgb(228, 232, 244);
    private Color WarningFill => _dark ? Color.Rgb(66, 49, 22) : Color.Rgb(255, 249, 235);

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ActionBar?.Hide();

        _density = Resources?.DisplayMetrics?.Density ?? 1f;
        _thai = ServerPreferences.GetLanguage(this) == "th";
        _dark = ServerPreferences.GetDarkTheme(this);
        ApplySystemBars();

        if (RegisteredSessionStore.TryLoad(this, out _))
        {
            OpenVoiceCraft();
            return;
        }

        if (GuestAccountStore.TryLoad(this, out var guest) && guest != null)
        {
            if (VoiceCraftGuestClient.NeedsRefresh(guest))
                _ = RefreshGuestInBackground(guest);

            OpenVoiceCraft();
            return;
        }

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
        root.SetPadding(Dp(16), Dp(18), Dp(16), Dp(30));

        var column = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };

        var screenWidthDp = (Resources?.DisplayMetrics?.WidthPixels ?? Dp(400)) / _density;
        var contentWidth = Dp((int)System.Math.Min(520f, System.Math.Max(288f, screenWidthDp - 32f)));
        root.AddView(column, new LinearLayout.LayoutParams(contentWidth, ViewGroup.LayoutParams.WrapContent));

        column.AddView(BuildHeroCard(), FullWrap());
        column.AddView(BuildAccessCard(), FullWrap(Dp(14)));
        column.AddView(BuildGuestNotice(), FullWrap(Dp(12)));

        _statusCard = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            Background = Round(Tint, 16, Border)
        };
        _statusCard.SetGravity(GravityFlags.CenterVertical);
        _statusCard.SetPadding(Dp(14), Dp(12), Dp(14), Dp(12));
        _statusCard.Visibility = ViewStates.Gone;

        var statusDot = Text("●", 13, true, Primary);
        _statusCard.AddView(statusDot, new LinearLayout.LayoutParams(Dp(24), ViewGroup.LayoutParams.WrapContent));

        _status = Text(string.Empty, 12, true, Ink);
        _statusCard.AddView(_status, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        column.AddView(_statusCard, FullWrap(Dp(12)));

        var footer = Text("VoiceCraft Server Mobile • Account V2", 10, false, Muted);
        footer.Gravity = GravityFlags.Center;
        footer.SetPadding(0, Dp(20), 0, Dp(4));
        column.AddView(footer);

        scroll.AddView(root);
        SetContentView(scroll);
    }

    private View BuildHeroCard()
    {
        var card = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Background = Round(Surface, 24, Border)
        };
        card.Elevation = Dp(3);
        card.SetPadding(Dp(20), Dp(18), Dp(20), Dp(20));

        var top = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal
        };
        top.SetGravity(GravityFlags.CenterVertical);

        var mark = Text("VC", 14, true, Color.White);
        mark.Gravity = GravityFlags.Center;
        mark.Background = Round(Primary, 15, Primary);
        mark.Elevation = Dp(2);
        top.AddView(mark, new LinearLayout.LayoutParams(Dp(46), Dp(46)));

        var brand = new LinearLayout(this) { Orientation = Orientation.Vertical };
        brand.SetPadding(Dp(12), 0, 0, 0);
        brand.AddView(Text("VoiceCraft Server", 20, true, Ink));
        brand.AddView(Text(T("ศูนย์ควบคุม VoiceCraft บน Android", "Android VoiceCraft control center"), 11, false, Muted));
        top.AddView(brand, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        card.AddView(top);

        var badge = Text("ACCOUNT V2", 10, true, Primary);
        badge.Gravity = GravityFlags.Center;
        badge.Background = Round(Tint, 18, Primary);
        badge.SetPadding(Dp(12), Dp(6), Dp(12), Dp(6));
        card.AddView(badge, Wrap(Dp(18), Dp(112)));

        var title = Text(T("เลือกวิธีเข้าใช้งาน", "Choose how to continue"), 24, true, Ink);
        title.SetPadding(0, Dp(14), 0, 0);
        card.AddView(title);

        var subtitle = Text(
            T(
                "เข้าสู่ระบบด้วยบัญชี VoiceCraft หรือใช้ Free Account ที่เก็บเฉพาะในอุปกรณ์นี้",
                "Sign in with your VoiceCraft account or use a device-local Free Account."),
            13,
            false,
            Muted);
        subtitle.SetLineSpacing(0, 1.12f);
        subtitle.SetPadding(0, Dp(8), 0, 0);
        card.AddView(subtitle);

        var featureRow = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal
        };
        featureRow.SetGravity(GravityFlags.CenterVertical);
        featureRow.SetPadding(0, Dp(18), 0, 0);
        featureRow.AddView(FeaturePill(T("ปลอดภัย", "SECURE"), Tint, Primary), new LinearLayout.LayoutParams(0, Dp(36), 1f));
        featureRow.AddView(FeaturePill(T("Guest ในเครื่อง", "LOCAL GUEST"), Tint2, Primary2), new LinearLayout.LayoutParams(0, Dp(36), 1f)
        {
            LeftMargin = Dp(8)
        });
        card.AddView(featureRow);

        return card;
    }

    private View BuildAccessCard()
    {
        var card = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Background = Round(Surface, 22, Border)
        };
        card.Elevation = Dp(2);
        card.SetPadding(Dp(18), Dp(18), Dp(18), Dp(18));

        card.AddView(Text(T("บัญชี VoiceCraft", "VOICECRAFT ACCOUNT"), 12, true, Primary));

        var accountText = Text(
            T(
                "สำหรับบัญชีที่ได้รับจากผู้ดูแลระบบ รวมถึงบัญชี ADMIN ที่ใช้ OTP",
                "For administrator-issued accounts, including ADMIN accounts that use OTP."),
            12,
            false,
            Muted);
        accountText.SetLineSpacing(0, 1.1f);
        accountText.SetPadding(0, Dp(5), 0, 0);
        card.AddView(accountText);

        _loginButton = ActionButton(T("เข้าสู่ระบบด้วยบัญชี", "LOGIN WITH ACCOUNT"), true);
        _loginButton.Click += (_, _) =>
            StartActivity(new Intent(this, typeof(RegisteredLoginActivity)));
        card.AddView(_loginButton, Full(Dp(56), Dp(16)));

        var divider = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal
        };
        divider.SetGravity(GravityFlags.CenterVertical);
        divider.SetPadding(0, Dp(13), 0, Dp(5));

        var left = new View(this) { Background = Solid(Border) };
        var right = new View(this) { Background = Solid(Border) };
        divider.AddView(left, new LinearLayout.LayoutParams(0, Dp(1), 1f));
        var or = Text(T("หรือ", "OR"), 10, true, Muted);
        or.Gravity = GravityFlags.Center;
        divider.AddView(or, new LinearLayout.LayoutParams(Dp(54), ViewGroup.LayoutParams.WrapContent));
        divider.AddView(right, new LinearLayout.LayoutParams(0, Dp(1), 1f));
        card.AddView(divider);

        _guestButton = ActionButton(T("สร้าง Free Account", "CREATE FREE ACCOUNT"), false);
        _guestButton.Click += async (_, _) => await CreateGuestAsync();
        card.AddView(_guestButton, Full(Dp(56), Dp(6)));

        return card;
    }

    private View BuildGuestNotice()
    {
        var card = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            Background = Round(WarningFill, 18, _dark ? Color.Rgb(103, 78, 35) : Color.Rgb(247, 213, 139))
        };
        card.SetGravity(GravityFlags.Top);
        card.SetPadding(Dp(14), Dp(13), Dp(14), Dp(13));

        var icon = Text("i", 12, true, Amber);
        icon.Gravity = GravityFlags.Center;
        icon.Background = Round(_dark ? Color.Rgb(82, 61, 27) : Color.Rgb(255, 244, 214), 13, Amber);
        card.AddView(icon, new LinearLayout.LayoutParams(Dp(28), Dp(28)));

        var textWrap = new LinearLayout(this) { Orientation = Orientation.Vertical };
        textWrap.SetPadding(Dp(10), 0, 0, 0);
        textWrap.AddView(Text(T("FREE ACCOUNT • เฉพาะอุปกรณ์นี้", "FREE ACCOUNT • THIS DEVICE ONLY"), 11, true, Ink));
        var note = Text(
            T(
                "ข้อมูลจะไม่ถูกเพิ่มในฐานข้อมูลบัญชี VoiceCraft และจะหายถาวรเมื่อ Clear App Data หรือถอนการติดตั้งแอป",
                "It is not added to the VoiceCraft account database. Clearing app data or uninstalling the app removes it permanently."),
            11,
            false,
            Muted);
        note.SetPadding(0, Dp(4), 0, 0);
        note.SetLineSpacing(0, 1.08f);
        textWrap.AddView(note);
        card.AddView(textWrap, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        return card;
    }

    private async Task CreateGuestAsync()
    {
        SetBusy(true, T("กำลังสร้าง Guest ID ในเครื่อง…", "Creating local Guest ID…"));

        try
        {
            var guest = GuestAccountStore.GetOrCreate(this);
            SetBusy(true, T("สร้าง Guest แล้ว กำลังรักษาความปลอดภัย Session…", "Guest created. Securing local session…"));

            try
            {
                await VoiceCraftGuestClient.RefreshSessionAsync(this, guest);
            }
            catch
            {
                // Guest is intentionally local-first and can work offline.
            }

            OpenVoiceCraft();
        }
        catch (Exception ex)
        {
            SetBusy(false, T("สร้าง Guest ไม่สำเร็จ: ", "Could not create Guest: ") + ex.Message);
        }
    }

    private async Task RefreshGuestInBackground(GuestProfile profile)
    {
        try
        {
            await VoiceCraftGuestClient.RefreshSessionAsync(this, profile);
        }
        catch { }
    }

    private void OpenVoiceCraft()
    {
        StartActivity(new Intent(this, typeof(ModernMainActivity)));
        Finish();
    }

    private void SetBusy(bool busy, string message)
    {
        if (_guestButton != null)
        {
            _guestButton.Enabled = !busy;
            _guestButton.Alpha = busy ? 0.55f : 1f;
        }

        if (_loginButton != null)
        {
            _loginButton.Enabled = !busy;
            _loginButton.Alpha = busy ? 0.55f : 1f;
        }

        if (_statusCard != null)
            _statusCard.Visibility = string.IsNullOrWhiteSpace(message) ? ViewStates.Gone : ViewStates.Visible;

        if (_status != null)
            _status.Text = message;
    }

    private TextView FeaturePill(string value, Color fill, Color textColor)
    {
        var pill = Text(value, 10, true, textColor);
        pill.Gravity = GravityFlags.Center;
        pill.Background = Round(fill, 17, fill);
        return pill;
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

    private Button ActionButton(string value, bool primary)
    {
        var button = new Button(this)
        {
            Text = value,
            TextSize = 12,
            MinHeight = Dp(54)
        };

        button.SetTypeface(Typeface.Default, TypefaceStyle.Bold);
        button.SetTextColor(primary ? Color.White : Primary);
        button.Background = primary
            ? Round(Primary, 16, Primary)
            : Round(SurfaceSoft, 16, Primary);
        button.Elevation = primary ? Dp(3) : 0;
        return button;
    }

    private GradientDrawable Round(Color fill, int radiusDp, Color stroke, int strokeWidthDp = 1)
    {
        var drawable = new GradientDrawable();
        drawable.SetColor(fill);
        drawable.SetCornerRadius(Dp(radiusDp));
        drawable.SetStroke(Dp(strokeWidthDp), stroke);
        return drawable;
    }

    private GradientDrawable Solid(Color fill)
    {
        var drawable = new GradientDrawable();
        drawable.SetColor(fill);
        return drawable;
    }

    private LinearLayout.LayoutParams Full(int height, int top = 0) =>
        new(ViewGroup.LayoutParams.MatchParent, height)
        {
            TopMargin = top
        };

    private LinearLayout.LayoutParams FullWrap(int top = 0) =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = top
        };

    private LinearLayout.LayoutParams Wrap(int top = 0, int width = ViewGroup.LayoutParams.WrapContent) =>
        new(width, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = top
        };

    private int Dp(int value) => (int)(value * _density + 0.5f);
}
