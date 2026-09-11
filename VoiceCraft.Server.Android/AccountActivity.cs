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
    Name = "chat.voicecraft.server.AccountActivity",
    Label = "VoiceCraft Server",
    MainLauncher = true,
    Exported = true)]
public sealed class AccountActivity : Activity
{
    private readonly SupabaseAuthClient _auth = new();
    private AccountSessionStore? _store;
    private SupabaseAuthClient.AuthSession? _session;
    private bool _busy;
    private bool _registerMode;
    private bool _manageMode;
    private float _density = 1f;

    private EditText? _email;
    private EditText? _password;
    private EditText? _username;
    private TextView? _message;
    private Button? _primaryButton;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ActionBar?.Hide();
        _density = Resources?.DisplayMetrics?.Density ?? 1f;
        _manageMode = Intent?.GetBooleanExtra("manage_account", false) == true;
        _store = new AccountSessionStore(this);
        Window?.SetStatusBarColor(Color.Rgb(16, 24, 45));
        Window?.SetNavigationBarColor(Color.Rgb(10, 16, 31));
        BuildLoadingUi();
        _ = RestoreSessionAsync();
    }

    internal static void LaunchManage(Context context)
    {
        var intent = new Intent(context, typeof(AccountActivity));
        intent.PutExtra("manage_account", true);
        context.StartActivity(intent);
    }

    private async Task RestoreSessionAsync()
    {
        var saved = _store?.Load();
        if (saved?.RefreshToken is not { Length: > 0 })
        {
            RunOnUiThread(BuildAuthUi);
            return;
        }

        var refreshed = await _auth.RefreshAsync(saved.RefreshToken);
        if (!refreshed.Ok || refreshed.Session is null)
        {
            _store?.Clear();
            RunOnUiThread(BuildAuthUi);
            return;
        }

        await AcceptSessionAsync(refreshed.Session, openDashboard: !_manageMode);
        if (_manageMode)
            RunOnUiThread(BuildAccountUi);
    }

    private void BuildLoadingUi()
    {
        var root = Root();
        var card = Card();
        card.AddView(Heading("VoiceCraft Server"));
        card.AddView(Text("กำลังตรวจสอบบัญชี…\nChecking account session…", 14, Color.Rgb(163, 174, 199)));
        var progress = new ProgressBar(this) { Indeterminate = true };
        card.AddView(progress, new LinearLayout.LayoutParams(Dp(48), Dp(48)) { Gravity = GravityFlags.CenterHorizontal, TopMargin = Dp(20) });
        root.AddView(card, CardParams());
        SetContentView(root);
    }

    private void BuildAuthUi()
    {
        var root = Root();
        var card = Card();
        card.AddView(Heading("VoiceCraft Account"));
        card.AddView(Text(
            _registerMode ? "สร้างบัญชีเพื่อใช้งาน VoiceCraft Server Mobile" : "เข้าสู่ระบบเพื่อเปิด VoiceCraft Server Mobile",
            13,
            Color.Rgb(163, 174, 199)));

        if (_registerMode)
        {
            _username = Input("Username", false);
            card.AddView(_username, FieldParams());
        }
        else
        {
            _username = null;
        }

        _email = Input("Email", false);
        _email.InputType = InputTypes.ClassText | InputTypes.TextVariationEmailAddress;
        card.AddView(_email, FieldParams());

        _password = Input("Password", true);
        card.AddView(_password, FieldParams());

        _message = Text(string.Empty, 12, Color.Rgb(248, 113, 113));
        _message.SetPadding(0, Dp(6), 0, Dp(6));
        card.AddView(_message);

        _primaryButton = Button(_registerMode ? "CREATE ACCOUNT" : "LOGIN", true);
        _primaryButton.Click += async (_, _) =>
        {
            if (_registerMode) await RegisterAsync();
            else await LoginAsync();
        };
        card.AddView(_primaryButton, ButtonParams());

        var switchMode = Button(_registerMode ? "I ALREADY HAVE AN ACCOUNT" : "CREATE ACCOUNT", false);
        switchMode.Click += (_, _) =>
        {
            if (_busy) return;
            _registerMode = !_registerMode;
            BuildAuthUi();
        };
        card.AddView(switchMode, ButtonParams());

        if (!_registerMode)
        {
            var reset = Button("FORGOT PASSWORD", false);
            reset.Click += async (_, _) => await ResetPasswordAsync();
            card.AddView(reset, ButtonParams());
        }

        card.AddView(Text("Account data is protected by Supabase Auth and Row Level Security. Session tokens are encrypted with Android Keystore.", 11, Color.Rgb(115, 128, 158)));
        root.AddView(card, CardParams());
        SetContentView(root);
    }

    private void BuildAccountUi()
    {
        var root = Root();
        var card = Card();
        card.AddView(Heading("VoiceCraft Account"));
        card.AddView(Text("SIGNED IN", 12, Color.Rgb(74, 222, 128), true));
        card.AddView(Text(_session?.User?.Email ?? "Unknown account", 18, Color.White, true));
        card.AddView(Text("Your VoiceCraft server settings remain local in Phase 1. Cloud sync will be enabled in the next account phase.", 12, Color.Rgb(163, 174, 199)));

        var back = Button("BACK TO VOICECRAFT", true);
        back.Click += (_, _) => OpenDashboard();
        card.AddView(back, ButtonParams());

        var logout = Button("LOGOUT", false);
        logout.Click += async (_, _) => await LogoutAsync();
        card.AddView(logout, ButtonParams());
        root.AddView(card, CardParams());
        SetContentView(root);
    }

    private async Task LoginAsync()
    {
        var email = _email?.Text?.Trim() ?? string.Empty;
        var password = _password?.Text ?? string.Empty;
        if (!ValidateCredentials(email, password)) return;

        SetBusy(true, "Signing in…");
        var result = await _auth.SignInAsync(email, password);
        if (!result.Ok || result.Session is null)
        {
            SetBusy(false, result.MessageText);
            return;
        }

        await AcceptSessionAsync(result.Session, openDashboard: true);
    }

    private async Task RegisterAsync()
    {
        var email = _email?.Text?.Trim() ?? string.Empty;
        var password = _password?.Text ?? string.Empty;
        var username = _username?.Text?.Trim() ?? string.Empty;
        if (!ValidateCredentials(email, password)) return;
        if (username.Length is > 0 and < 3)
        {
            ShowMessage("Username must be at least 3 characters.");
            return;
        }

        SetBusy(true, "Creating account…");
        var result = await _auth.SignUpAsync(email, password, username);
        if (!result.Ok)
        {
            SetBusy(false, result.MessageText);
            return;
        }

        if (result.Session is not null)
        {
            await AcceptSessionAsync(result.Session, openDashboard: true);
            return;
        }

        SetBusy(false, result.MessageText);
        _registerMode = false;
        Toast.MakeText(this, result.MessageText, ToastLength.Long)?.Show();
        BuildAuthUi();
        ShowMessage("Check your email, verify the account, then sign in.", success: true);
    }

    private async Task ResetPasswordAsync()
    {
        var email = _email?.Text?.Trim() ?? string.Empty;
        if (!email.Contains('@'))
        {
            ShowMessage("Enter your email first.");
            return;
        }

        SetBusy(true, "Sending reset email…");
        var result = await _auth.SendPasswordResetAsync(email);
        SetBusy(false, result.MessageText, result.Ok);
    }

    private async Task LogoutAsync()
    {
        if (_busy) return;
        _busy = true;
        if (_session is not null)
            await _auth.SignOutAsync(_session.AccessToken);
        _store?.Clear();
        _session = null;
        _busy = false;
        _registerMode = false;
        RunOnUiThread(BuildAuthUi);
    }

    private async Task AcceptSessionAsync(SupabaseAuthClient.AuthSession session, bool openDashboard)
    {
        _session = session;
        _store?.Save(session);

        try
        {
            var installationId = _store?.GetOrCreateInstallationId() ?? Guid.NewGuid().ToString("N");
            await _auth.UpsertDeviceAsync(
                session,
                installationId,
                (Build.Manufacturer ?? "Android") + " " + (Build.Model ?? "Device"),
                Build.Model ?? "Android",
                PackageManager?.GetPackageInfo(PackageName!, 0)?.VersionName ?? "unknown");
        }
        catch
        {
            // Device registration is non-critical and must never block login.
        }

        if (openDashboard)
            RunOnUiThread(OpenDashboard);
    }

    private void OpenDashboard()
    {
        StartActivity(new Intent(this, typeof(ModernMainActivity)));
        Finish();
    }

    private bool ValidateCredentials(string email, string password)
    {
        if (!email.Contains('@'))
        {
            ShowMessage("Enter a valid email address.");
            return false;
        }
        if (password.Length < 8)
        {
            ShowMessage("Password must be at least 8 characters.");
            return false;
        }
        return true;
    }

    private void SetBusy(bool busy, string message, bool success = false)
    {
        _busy = busy;
        RunOnUiThread(() =>
        {
            if (_primaryButton is not null) _primaryButton.Enabled = !busy;
            ShowMessage(message, success);
        });
    }

    private void ShowMessage(string message, bool success = false)
    {
        if (_message is null) return;
        _message.SetTextColor(success ? Color.Rgb(74, 222, 128) : Color.Rgb(248, 113, 113));
        _message.Text = message;
    }

    private LinearLayout Root()
    {
        var root = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Background = new ColorDrawable(Color.Rgb(10, 16, 31))
        };
        root.SetGravity(GravityFlags.Center);
        root.SetPadding(Dp(20), Dp(28), Dp(20), Dp(28));
        return root;
    }

    private LinearLayout Card()
    {
        var card = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Background = Rounded(Color.Rgb(22, 31, 53), 22)
        };
        card.SetPadding(Dp(22), Dp(24), Dp(22), Dp(24));
        card.Elevation = Dp(8);
        return card;
    }

    private TextView Heading(string value) => Text(value, 27, Color.White, true);

    private TextView Text(string value, int sp, Color color, bool bold = false)
    {
        var view = new TextView(this) { Text = value };
        view.SetTextColor(color);
        view.SetTextSize(global::Android.Util.ComplexUnitType.Sp, sp);
        if (bold) view.SetTypeface(null, global::Android.Graphics.TypefaceStyle.Bold);
        view.SetPadding(0, Dp(5), 0, Dp(5));
        return view;
    }

    private EditText Input(string hint, bool password)
    {
        var input = new EditText(this)
        {
            Hint = hint,
            Background = Rounded(Color.Rgb(31, 43, 70), 14)
        };
        input.SetSingleLine(true);
        input.SetTextColor(Color.White);
        input.SetHintTextColor(Color.Rgb(125, 140, 174));
        input.SetPadding(Dp(14), 0, Dp(14), 0);
        if (password)
            input.InputType = InputTypes.ClassText | InputTypes.TextVariationPassword;
        return input;
    }

    private Button Button(string text, bool primary)
    {
        var button = new Button(this)
        {
            Text = text,
            Background = Rounded(primary ? Color.Rgb(73, 116, 255) : Color.Rgb(38, 51, 82), 14)
        };
        button.SetAllCaps(false);
        button.SetTextColor(Color.White);
        return button;
    }

    private LinearLayout.LayoutParams CardParams() => new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
    {
        LeftMargin = Dp(4), RightMargin = Dp(4)
    };

    private LinearLayout.LayoutParams FieldParams() => new(ViewGroup.LayoutParams.MatchParent, Dp(56))
    {
        TopMargin = Dp(12)
    };

    private LinearLayout.LayoutParams ButtonParams() => new(ViewGroup.LayoutParams.MatchParent, Dp(52))
    {
        TopMargin = Dp(10)
    };

    private int Dp(int value) => (int)(value * _density + 0.5f);

    private static GradientDrawable Rounded(Color color, int radiusDp)
    {
        var shape = new GradientDrawable();
        shape.SetColor(color);
        shape.SetCornerRadius(radiusDp * 3f);
        return shape;
    }
}
