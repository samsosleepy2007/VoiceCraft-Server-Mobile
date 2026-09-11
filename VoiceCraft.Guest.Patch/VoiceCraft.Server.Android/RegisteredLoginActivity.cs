using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;

namespace VoiceCraft.Server.Android;

[Activity(
    Name = "chat.voicecraft.server.RegisteredLoginActivity",
    Label = "VoiceCraft Login",
    Exported = false)]
public sealed class RegisteredLoginActivity : Activity
{
    private static readonly Color Primary = Color.Rgb(73, 116, 255);
    private static readonly Color Primary2 = Color.Rgb(105, 86, 255);
    private static readonly Color Red = Color.Rgb(239, 68, 68);
    private static readonly Color Amber = Color.Rgb(245, 158, 11);

    private EditText? _login;
    private EditText? _password;
    private EditText? _otp;
    private Button? _submit;
    private Button? _free;
    private LinearLayout? _statusCard;
    private TextView? _status;
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
    private Color DangerFill => _dark ? Color.Rgb(64, 29, 36) : Color.Rgb(255, 242, 244);
    private Color WarningFill => _dark ? Color.Rgb(66, 49, 22) : Color.Rgb(255, 249, 235);

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
            Gravity = GravityFlags.CenterHorizontal,
            Background = Solid(Page)
        };
        root.SetPadding(Dp(16), Dp(16), Dp(16), Dp(30));

        var column = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };

        var screenWidthDp = (Resources?.DisplayMetrics?.WidthPixels ?? Dp(400)) / _density;
        var contentWidth = Dp((int)System.Math.Min(520f, System.Math.Max(288f, screenWidthDp - 32f)));
        root.AddView(column, new LinearLayout.LayoutParams(contentWidth, ViewGroup.LayoutParams.WrapContent));

        column.AddView(BuildTopBar(), FullWrap());
        column.AddView(BuildHeroCard(), FullWrap(Dp(12)));
        column.AddView(BuildFormCard(), FullWrap(Dp(14)));
        column.AddView(BuildAdminNotice(), FullWrap(Dp(12)));

        _statusCard = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            Gravity = GravityFlags.CenterVertical,
            Background = Round(Tint, 16, Primary)
        };
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

    private View BuildTopBar()
    {
        var row = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            Gravity = GravityFlags.CenterVertical
        };

        var back = SmallButton(T("‹ กลับ", "‹ BACK"));
        back.Click += (_, _) => Finish();
        row.AddView(back, new LinearLayout.LayoutParams(Dp(92), Dp(40)));

        var spacer = new Space(this);
        row.AddView(spacer, new LinearLayout.LayoutParams(0, 1, 1f));

        var badge = Text("ACCOUNT V2", 10, true, Primary);
        badge.Gravity = GravityFlags.Center;
        badge.Background = Round(Tint, 18, Primary);
        badge.SetPadding(Dp(12), 0, Dp(12), 0);
        row.AddView(badge, new LinearLayout.LayoutParams(Dp(112), Dp(36)));

        return row;
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

        var mark = Text("VC", 14, true, Color.White);
        mark.Gravity = GravityFlags.Center;
        mark.Background = Round(Primary2, 15, Primary2);
        mark.Elevation = Dp(2);
        card.AddView(mark, new LinearLayout.LayoutParams(Dp(46), Dp(46)));

        var title = Text(T("เข้าสู่ระบบ VoiceCraft", "Sign in to VoiceCraft"), 25, true, Ink);
        title.SetPadding(0, Dp(16), 0, 0);
        card.AddView(title);

        var subtitle = Text(
            T(
                "ใช้บัญชีที่ได้รับจากผู้ดูแลระบบเพื่อเข้าสู่ VoiceCraft Server Mobile",
                "Use the account issued by your administrator to access VoiceCraft Server Mobile."),
            13,
            false,
            Muted);
        subtitle.SetPadding(0, Dp(7), 0, 0);
        subtitle.SetLineSpacing(0, 1.12f);
        card.AddView(subtitle);

        return card;
    }

    private View BuildFormCard()
    {
        var card = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Background = Round(Surface, 22, Border)
        };
        card.Elevation = Dp(2);
        card.SetPadding(Dp(18), Dp(18), Dp(18), Dp(18));

        card.AddView(Text(T("ข้อมูลบัญชี", "ACCOUNT DETAILS"), 12, true, Primary));
        card.AddView(FieldLabel(T("ชื่อบัญชี", "Account")), Wrap(Dp(18)));

        _login = Input(InputTypes.ClassText, T("ชื่อบัญชี", "Account name"));
        _login.ImeOptions = ImeAction.Next;
        card.AddView(_login, Full(Dp(56), Dp(7)));

        card.AddView(FieldLabel(T("รหัสผ่าน", "Password")), Wrap(Dp(14)));
        _password = Input(
            InputTypes.ClassText | InputTypes.TextVariationPassword,
            T("รหัสผ่านจากผู้ดูแลระบบ", "Password from administrator"));
        _password.ImeOptions = ImeAction.Next;
        card.AddView(_password, Full(Dp(56), Dp(7)));

        var otpRow = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            Gravity = GravityFlags.CenterVertical
        };
        otpRow.SetPadding(0, Dp(14), 0, 0);
        otpRow.AddView(FieldLabel(T("ADMIN OTP", "ADMIN OTP")), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        var optional = Text(T("ไม่บังคับ", "OPTIONAL"), 9, true, Muted);
        optional.Gravity = GravityFlags.Center;
        optional.Background = Round(SurfaceSoft, 12, Border);
        otpRow.AddView(optional, new LinearLayout.LayoutParams(Dp(76), Dp(28)));
        card.AddView(otpRow);

        _otp = Input(InputTypes.ClassNumber, T("OTP 6 หลัก เมื่อระบบร้องขอ", "6-digit OTP if required"));
        _otp.ImeOptions = ImeAction.Done;
        _otp.EditorAction += async (_, e) =>
        {
            if (e.ActionId == ImeAction.Done && _submit?.Enabled == true)
                await LoginAsync();
        };
        card.AddView(_otp, Full(Dp(56), Dp(7)));

        _submit = ActionButton(T("เข้าสู่ระบบ", "LOGIN"), true);
        _submit.Click += async (_, _) => await LoginAsync();
        card.AddView(_submit, Full(Dp(56), Dp(22)));

        _free = ActionButton(T("ใช้ Free Account แทน", "USE FREE ACCOUNT INSTEAD"), false);
        _free.Click += (_, _) => Finish();
        card.AddView(_free, Full(Dp(54), Dp(8)));

        return card;
    }

    private View BuildAdminNotice()
    {
        var card = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            Gravity = GravityFlags.Top,
            Background = Round(WarningFill, 18, _dark ? Color.Rgb(103, 78, 35) : Color.Rgb(247, 213, 139))
        };
        card.SetPadding(Dp(14), Dp(13), Dp(14), Dp(13));

        var icon = Text("i", 12, true, Amber);
        icon.Gravity = GravityFlags.Center;
        icon.Background = Round(_dark ? Color.Rgb(82, 61, 27) : Color.Rgb(255, 244, 214), 13, Amber);
        card.AddView(icon, new LinearLayout.LayoutParams(Dp(28), Dp(28)));

        var wrap = new LinearLayout(this) { Orientation = Orientation.Vertical };
        wrap.SetPadding(Dp(10), 0, 0, 0);
        wrap.AddView(Text(T("บัญชีถูกสร้างโดยผู้ดูแลระบบ", "ADMINISTRATOR-ISSUED ACCOUNTS"), 11, true, Ink));

        var note = Text(
            T(
                "ไม่มีการสมัครบัญชีแบบสาธารณะ บัญชี ADMIN อาจต้องใช้ OTP 6 หลัก และบางบัญชีอาจต้องตั้งค่า MFA ในแอป Admin ก่อน",
                "There is no public registration. ADMIN accounts may require a 6-digit OTP, and some accounts must finish MFA setup in the Admin app first."),
            11,
            false,
            Muted);
        note.SetPadding(0, Dp(4), 0, 0);
        note.SetLineSpacing(0, 1.08f);
        wrap.AddView(note);
        card.AddView(wrap, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        return card;
    }

    private async Task LoginAsync()
    {
        var login = _login?.Text?.Trim() ?? string.Empty;
        var password = _password?.Text ?? string.Empty;
        var otp = _otp?.Text?.Trim();

        if (login.Length < 3 || password.Length < 12)
        {
            SetStatus(T("ตรวจสอบชื่อบัญชีและรหัสผ่านอีกครั้ง", "Check account name and password."), false);
            return;
        }

        SetBusy(true);
        SetStatus(T("กำลังเข้าสู่ระบบ…", "Logging in…"), true);

        try
        {
            var result = await VoiceCraftAccountClient.LoginAsync(this, login, password, otp);

            if (result.AdminMfaSetupRequired)
            {
                SetStatus(
                    T(
                        "บัญชี ADMIN นี้ต้องตั้งค่า MFA ให้เสร็จในแอป VoiceCraft Admin ก่อน",
                        "This ADMIN account must finish MFA setup in the separate VoiceCraft Admin app."),
                    false);
                RegisteredSessionStore.Delete(this);
                return;
            }

            var intent = new Intent(this, typeof(ModernMainActivity));
            intent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTask);
            StartActivity(intent);
            Finish();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, false);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        if (_submit != null)
        {
            _submit.Enabled = !busy;
            _submit.Alpha = busy ? 0.58f : 1f;
            _submit.Text = busy ? T("กำลังเข้าสู่ระบบ…", "SIGNING IN…") : T("เข้าสู่ระบบ", "LOGIN");
        }

        if (_free != null)
        {
            _free.Enabled = !busy;
            _free.Alpha = busy ? 0.58f : 1f;
        }

        if (_login != null)
            _login.Enabled = !busy;
        if (_password != null)
            _password.Enabled = !busy;
        if (_otp != null)
            _otp.Enabled = !busy;
    }

    private void SetStatus(string message, bool info)
    {
        if (_status == null || _statusCard == null)
            return;

        _status.Text = message;
        _status.SetTextColor(info ? Ink : Red);
        _statusCard.Background = info
            ? Round(Tint, 16, Primary)
            : Round(DangerFill, 16, Red);
        _statusCard.Visibility = ViewStates.Visible;
    }

    private TextView FieldLabel(string value)
    {
        var label = Text(value, 11, true, Muted);
        label.SetPadding(Dp(2), 0, 0, 0);
        return label;
    }

    private EditText Input(InputTypes type, string hint)
    {
        var input = new EditText(this)
        {
            TextSize = 14,
            InputType = type,
            Hint = hint,
            Background = Round(SurfaceSoft, 15, Border)
        };

        input.SetSingleLine(true);
        input.SetTextColor(Ink);
        input.SetHintTextColor(Muted);
        input.SetPadding(Dp(14), 0, Dp(14), 0);
        return input;
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
            MinHeight = Dp(52),
            AllCaps = false
        };
        button.SetTypeface(Typeface.Default, TypefaceStyle.Bold);
        button.SetTextColor(primary ? Color.White : Primary);
        button.Background = primary
            ? Round(Primary, 16, Primary)
            : Round(SurfaceSoft, 16, Primary);
        button.Elevation = primary ? Dp(3) : 0;
        return button;
    }

    private Button SmallButton(string value)
    {
        var button = new Button(this)
        {
            Text = value,
            TextSize = 10,
            AllCaps = false,
            Background = Round(Surface, 14, Border)
        };
        button.SetTypeface(Typeface.Default, TypefaceStyle.Bold);
        button.SetTextColor(Ink);
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

    private LinearLayout.LayoutParams Wrap(int top = 0) =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = top
        };

    private int Dp(int value) => (int)(value * _density + 0.5f);
}
