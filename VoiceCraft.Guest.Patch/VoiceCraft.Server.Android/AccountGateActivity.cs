using Android.App;
using Android.Content;
using Android.Graphics;
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
    private Button? _guestButton;
    private Button? _loginButton;
    private TextView? _status;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ActionBar?.Hide();

        if (RegisteredSessionStore.TryLoad(this, out _))
        {
            OpenVoiceCraft();
            return;
        }

        if (GuestAccountStore.TryLoad(
                this,
                out var guest)
            && guest != null)
        {
            if (VoiceCraftGuestClient.NeedsRefresh(guest))
                _ = RefreshGuestInBackground(guest);

            OpenVoiceCraft();
            return;
        }

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
            Dp(48),
            Dp(24),
            Dp(32));

        root.SetGravity(GravityFlags.CenterHorizontal);
        root.SetBackgroundColor(Color.Rgb(247, 248, 255));

        var title = Text(
            "VOICECRAFT SERVER",
            26,
            true,
            Color.Rgb(35, 39, 54));

        title.Gravity = GravityFlags.Center;
        root.AddView(title);

        var subtitle = Text(
            "Choose how you want to use VoiceCraft",
            14,
            false,
            Color.Rgb(116, 124, 145));

        subtitle.Gravity = GravityFlags.Center;
        subtitle.SetPadding(0, Dp(8), 0, Dp(30));
        root.AddView(subtitle);

        _loginButton = ActionButton(
            "LOGIN WITH ACCOUNT",
            Color.Rgb(73, 116, 255),
            Color.White);

        _loginButton.Click += (_, _) =>
            StartActivity(
                new Intent(
                    this,
                    typeof(RegisteredLoginActivity)));

        root.AddView(
            _loginButton,
            Full(Dp(54), Dp(8)));

        _guestButton = ActionButton(
            "CREATE FREE ACCOUNT",
            Color.White,
            Color.Rgb(73, 116, 255));

        _guestButton.Click += async (_, _) =>
            await CreateGuestAsync();

        root.AddView(
            _guestButton,
            Full(Dp(54), Dp(8)));

        var note = Text(
            "Free Account is stored only on this device.\n" +
            "It is not added to the VoiceCraft account database.\n" +
            "Clearing app data or uninstalling the app removes it permanently.",
            12,
            false,
            Color.Rgb(116, 124, 145));

        note.Gravity = GravityFlags.Center;
        note.SetPadding(
            Dp(8),
            Dp(24),
            Dp(8),
            Dp(18));

        root.AddView(note);

        _status = Text(
            string.Empty,
            12,
            true,
            Color.Rgb(73, 116, 255));

        _status.Gravity = GravityFlags.Center;
        root.AddView(_status);

        scroll.AddView(root);
        SetContentView(scroll);
    }

    private async Task CreateGuestAsync()
    {
        SetBusy(true, "Creating local Guest ID…");

        try
        {
            var guest =
                GuestAccountStore.GetOrCreate(this);

            SetBusy(
                true,
                "Guest created. Securing local session…");

            try
            {
                await VoiceCraftGuestClient
                    .RefreshSessionAsync(this, guest);
            }
            catch
            {
                // Guest is intentionally local-first and can work offline.
            }

            OpenVoiceCraft();
        }
        catch (Exception ex)
        {
            SetBusy(
                false,
                "Could not create Guest: " + ex.Message);
        }
    }

    private async Task RefreshGuestInBackground(
        GuestProfile profile)
    {
        try
        {
            await VoiceCraftGuestClient
                .RefreshSessionAsync(this, profile);
        }
        catch { }
    }

    private void OpenVoiceCraft()
    {
        StartActivity(
            new Intent(
                this,
                typeof(ModernMainActivity)));

        Finish();
    }

    private void SetBusy(bool busy, string message)
    {
        if (_guestButton != null)
            _guestButton.Enabled = !busy;

        if (_loginButton != null)
            _loginButton.Enabled = !busy;

        if (_status != null)
            _status.Text = message;
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

    private Button ActionButton(
        string value,
        Color fill,
        Color text)
    {
        var button = new Button(this)
        {
            Text = value,
            TextSize = 12,
            MinHeight = Dp(52)
        };

        button.SetTextColor(text);
        button.SetBackgroundColor(fill);
        return button;
    }

    private LinearLayout.LayoutParams Full(
        int height,
        int marginTop) =>
        new(
            ViewGroup.LayoutParams.MatchParent,
            height)
        {
            TopMargin = marginTop
        };

    private int Dp(int value) =>
        (int)(
            value *
            (Resources?.DisplayMetrics?.Density ?? 1f)
            + 0.5f);
}
