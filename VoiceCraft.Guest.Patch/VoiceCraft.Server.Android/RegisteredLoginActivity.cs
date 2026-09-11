using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;

namespace VoiceCraft.Server.Android;

[Activity(
    Name = "chat.voicecraft.server.RegisteredLoginActivity",
    Label = "VoiceCraft Login",
    Exported = false)]
public sealed class RegisteredLoginActivity : Activity
{
    private EditText? _login;
    private EditText? _password;
    private EditText? _otp;
    private Button? _submit;
    private TextView? _status;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ActionBar?.Hide();
        BuildUi();
    }

    private void BuildUi()
    {
        var scroll = new ScrollView(this)
        {
            FillViewport = true
        };

        var root = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };

        root.SetPadding(
            Dp(24),
            Dp(42),
            Dp(24),
            Dp(32));

        root.SetBackgroundColor(
            Color.Rgb(247, 248, 255));

        root.AddView(
            Text(
                "VOICECRAFT ACCOUNT",
                24,
                true,
                Color.Rgb(35, 39, 54)));

        root.AddView(
            Text(
                "Accounts are issued by an administrator. " +
                "There is no public registration.",
                12,
                false,
                Color.Rgb(116, 124, 145)),
            Wrap(Dp(8)));

        root.AddView(
            Text(
                "Account",
                12,
                true,
                Color.Rgb(116, 124, 145)),
            Wrap(Dp(20)));

        _login = Input(InputTypes.ClassText);
        _login.Hint = "Account name";
        root.AddView(_login);

        root.AddView(
            Text(
                "Password",
                12,
                true,
                Color.Rgb(116, 124, 145)),
            Wrap(Dp(14)));

        _password = Input(
            InputTypes.ClassText |
            InputTypes.TextVariationPassword);

        _password.Hint =
            "Password from administrator";

        root.AddView(_password);

        root.AddView(
            Text(
                "Admin OTP (only for ADMIN)",
                12,
                true,
                Color.Rgb(116, 124, 145)),
            Wrap(Dp(14)));

        _otp = Input(InputTypes.ClassNumber);
        _otp.Hint = "6-digit OTP if required";
        root.AddView(_otp);

        _submit = new Button(this)
        {
            Text = "LOGIN",
            TextSize = 12,
            MinHeight = Dp(52)
        };

        _submit.SetTextColor(Color.White);
        _submit.SetBackgroundColor(
            Color.Rgb(73, 116, 255));

        _submit.Click += async (_, _) =>
            await LoginAsync();

        root.AddView(
            _submit,
            Full(Dp(54), Dp(24)));

        var free = new Button(this)
        {
            Text = "USE FREE ACCOUNT INSTEAD",
            TextSize = 11
        };

        free.Click += (_, _) => Finish();

        root.AddView(
            free,
            Full(Dp(50), Dp(8)));

        _status = Text(
            string.Empty,
            12,
            true,
            Color.Rgb(239, 68, 68));

        _status.SetPadding(
            0,
            Dp(18),
            0,
            0);

        root.AddView(_status);

        scroll.AddView(root);
        SetContentView(scroll);
    }

    private async Task LoginAsync()
    {
        var login =
            _login?.Text?.Trim()
            ?? string.Empty;

        var password =
            _password?.Text
            ?? string.Empty;

        var otp =
            _otp?.Text?.Trim();

        if (login.Length < 3
            || password.Length < 12)
        {
            SetStatus(
                "Check account name and password.",
                false);

            return;
        }

        SetBusy(true);
        SetStatus("Logging in…", true);

        try
        {
            var result =
                await VoiceCraftAccountClient.LoginAsync(
                    this,
                    login,
                    password,
                    otp);

            if (result.AdminMfaSetupRequired)
            {
                SetStatus(
                    "This ADMIN account must finish MFA setup " +
                    "in the separate VoiceCraft Admin app.",
                    false);

                RegisteredSessionStore.Delete(this);
                return;
            }

            var intent = new Intent(
                this,
                typeof(ModernMainActivity));

            intent.SetFlags(
                ActivityFlags.NewTask |
                ActivityFlags.ClearTask);

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
            _submit.Enabled = !busy;
    }

    private void SetStatus(
        string message,
        bool info)
    {
        if (_status == null)
            return;

        _status.Text = message;

        _status.SetTextColor(
            info
                ? Color.Rgb(73, 116, 255)
                : Color.Rgb(239, 68, 68));
    }

    private EditText Input(InputTypes type)
    {
        var input = new EditText(this)
        {
            TextSize = 14,
            InputType = type
        };

        input.SetSingleLine(true);
        input.SetPadding(
            Dp(12),
            Dp(8),
            Dp(12),
            Dp(8));

        return input;
    }

    private TextView Text(
        string value,
        float size,
        bool bold,
        Color color)
    {
        var view = new TextView(this)
        {
            Text = value,
            TextSize = size
        };

        view.SetTextColor(color);

        if (bold)
            view.SetTypeface(
                Typeface.Default,
                TypefaceStyle.Bold);

        return view;
    }

    private LinearLayout.LayoutParams Full(
        int height,
        int top) =>
        new(
            ViewGroup.LayoutParams.MatchParent,
            height)
        {
            TopMargin = top
        };

    private LinearLayout.LayoutParams Wrap(
        int top) =>
        new(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = top
        };

    private int Dp(int value) =>
        (int)(
            value *
            (Resources?.DisplayMetrics?.Density ?? 1f)
            + 0.5f);
}
