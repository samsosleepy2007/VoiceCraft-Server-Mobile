using Android.App;
using Android.Content;
using Android.Graphics;
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
                "ACCOUNT",
                24,
                true,
                Color.Rgb(35, 39, 54)));

        if (RegisteredSessionStore.TryLoad(
                this,
                out var session)
            && session != null)
        {
            root.AddView(
                Text(
                    $"{session.LoginName}\n" +
                    $"Session expires: " +
                    $"{session.ExpiresAt.LocalDateTime:g}",
                    14,
                    false,
                    Color.Rgb(35, 39, 54)),
                Wrap(Dp(18)));

            root.AddView(
                ActionButton(
                    "CONTINUE VOICECRAFT",
                    OpenVoiceCraft,
                    true),
                Full(Dp(52), Dp(20)));

            root.AddView(
                ActionButton(
                    "SWITCH TO FREE ACCOUNT",
                    SwitchToFree,
                    false),
                Full(Dp(52), Dp(8)));

            root.AddView(
                ActionButton(
                    "LOG OUT ACCOUNT",
                    () => _ = LogoutAsync(session),
                    false),
                Full(Dp(52), Dp(8)));
        }
        else if (GuestAccountStore.TryLoad(
                    this,
                    out var guest)
                 && guest != null)
        {
            root.AddView(
                Text(
                    $"Free Account\n" +
                    $"{guest.GuestId}\n" +
                    "Stored only on this device",
                    14,
                    false,
                    Color.Rgb(35, 39, 54)),
                Wrap(Dp(18)));

            root.AddView(
                ActionButton(
                    "CONTINUE VOICECRAFT",
                    OpenVoiceCraft,
                    true),
                Full(Dp(52), Dp(20)));

            root.AddView(
                ActionButton(
                    "LOGIN WITH ACCOUNT",
                    () => StartActivity(
                        new Intent(
                            this,
                            typeof(RegisteredLoginActivity))),
                    false),
                Full(Dp(52), Dp(8)));

            root.AddView(
                Text(
                    "Clearing app data or uninstalling VoiceCraft " +
                    "permanently removes this Guest ID.",
                    12,
                    false,
                    Color.Rgb(116, 124, 145)),
                Wrap(Dp(20)));
        }
        else
        {
            root.AddView(
                Text(
                    "No local account is active.",
                    14,
                    false,
                    Color.Rgb(116, 124, 145)),
                Wrap(Dp(18)));

            root.AddView(
                ActionButton(
                    "OPEN ACCOUNT SETUP",
                    OpenGate,
                    true),
                Full(Dp(52), Dp(20)));
        }

        scroll.AddView(root);
        SetContentView(scroll);
    }

    private async Task LogoutAsync(
        RegisteredSession session)
    {
        await VoiceCraftAccountClient
            .LogoutAsync(session);

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
        var intent = new Intent(
            this,
            typeof(AccountGateActivity));

        intent.SetFlags(
            ActivityFlags.NewTask |
            ActivityFlags.ClearTask);

        StartActivity(intent);
        Finish();
    }

    private void OpenVoiceCraft()
    {
        var intent = new Intent(
            this,
            typeof(ModernMainActivity));

        intent.SetFlags(
            ActivityFlags.NewTask |
            ActivityFlags.ClearTask);

        StartActivity(intent);
        Finish();
    }

    private Button ActionButton(
        string text,
        Action action,
        bool primary)
    {
        var button = new Button(this)
        {
            Text = text,
            TextSize = 11
        };

        button.SetTextColor(
            primary
                ? Color.White
                : Color.Rgb(73, 116, 255));

        button.SetBackgroundColor(
            primary
                ? Color.Rgb(73, 116, 255)
                : Color.White);

        button.Click += (_, _) => action();
        return button;
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
