using System.Net.Http;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using AndroidUri = Android.Net.Uri;

namespace VoiceCraft.Server.Android;

[Application]
public sealed class VoiceCraftCreditsApplication : Application, Application.IActivityLifecycleCallbacks
{
    private readonly Dictionary<Activity, ViewTreeObserver.IOnGlobalLayoutListener> _listeners = new();

    public VoiceCraftCreditsApplication(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer)
    {
    }

    public override void OnCreate()
    {
        base.OnCreate();
        RegisterActivityLifecycleCallbacks(this);
    }

    public void OnActivityCreated(Activity activity, Bundle? savedInstanceState)
    {
    }

    public void OnActivityStarted(Activity activity)
    {
    }

    public void OnActivityResumed(Activity activity)
    {
        if (activity is not ModernMainActivity)
            return;

        if (_listeners.ContainsKey(activity))
        {
            UpstreamCreditsInjector.TryInject(activity);
            return;
        }

        var root = activity.Window?.DecorView;
        var observer = root?.ViewTreeObserver;
        if (root == null || observer == null)
            return;

        var listener = new CreditsLayoutListener(() => UpstreamCreditsInjector.TryInject(activity));
        observer.AddOnGlobalLayoutListener(listener);
        _listeners[activity] = listener;
        UpstreamCreditsInjector.TryInject(activity);
    }

    public void OnActivityPaused(Activity activity)
    {
    }

    public void OnActivityStopped(Activity activity)
    {
    }

    public void OnActivitySaveInstanceState(Activity activity, Bundle outState)
    {
    }

    public void OnActivityDestroyed(Activity activity)
    {
        if (!_listeners.Remove(activity, out var listener))
            return;

        var observer = activity.Window?.DecorView?.ViewTreeObserver;
        if (observer?.IsAlive == true)
            observer.RemoveOnGlobalLayoutListener(listener);
    }

    private sealed class CreditsLayoutListener : Java.Lang.Object, ViewTreeObserver.IOnGlobalLayoutListener
    {
        private readonly Action _callback;

        public CreditsLayoutListener(Action callback)
        {
            _callback = callback;
        }

        public void OnGlobalLayout()
        {
            _callback();
        }
    }
}

internal static class UpstreamCreditsInjector
{
    private const string Marker = "voicecraft-upstream-credits";
    private static readonly HttpClient AvatarClient = new();
    private static readonly Dictionary<string, Bitmap> AvatarCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object AvatarLock = new();

    private sealed record CreditProfile(string Login, string RoleThai, string RoleEnglish, string Initials);

    private static readonly CreditProfile[] Profiles =
    {
        new("AvionBlock", "องค์กรผู้พัฒนา VoiceCraft ต้นฉบับ", "Original VoiceCraft organization", "AB"),
        new("SineVector241", "ผู้สร้างและผู้ดูแล VoiceCraft ต้นฉบับ", "VoiceCraft creator & upstream maintainer", "SV"),
        new("Miniontoby", "ผู้ร่วมพัฒนา VoiceCraft ต้นฉบับ", "Upstream VoiceCraft contributor", "M"),
        new("Unny984", "ผู้ร่วมพัฒนา VoiceCraft ต้นฉบับ", "Upstream VoiceCraft contributor", "U"),
        new("lil-jon-crunk", "ผู้ร่วมพัฒนา VoiceCraft ต้นฉบับ", "Upstream VoiceCraft contributor", "LJ")
    };

    public static void TryInject(Activity activity)
    {
        try
        {
            var root = activity.Window?.DecorView;
            if (root == null)
                return;

            var settings = FindSettingsScroll(root);
            if (settings == null || ContainsMarker(settings))
                return;

            if (settings.GetChildAt(0) is not LinearLayout body)
                return;

            body.AddView(BuildCredits(activity), new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent)
            {
                TopMargin = Dp(activity, 4),
                BottomMargin = Dp(activity, 24)
            });
        }
        catch
        {
            // Credits are optional UI and must never interrupt the server controls.
        }
    }

    private static View BuildCredits(Activity activity)
    {
        var thai = ServerPreferences.GetLanguage(activity) == "th";
        var dark = ServerPreferences.GetDarkTheme(activity);

        var ink = dark ? Color.Rgb(246, 248, 255) : Color.Rgb(35, 39, 54);
        var muted = dark ? Color.Rgb(160, 170, 190) : Color.Rgb(116, 124, 145);
        var surface = dark ? Color.Rgb(22, 30, 48) : Color.White;
        var surfaceSoft = dark ? Color.Rgb(28, 38, 60) : Color.Rgb(248, 250, 255);
        var border = dark ? Color.Rgb(51, 63, 87) : Color.Rgb(228, 232, 244);
        var primary = Color.Rgb(73, 116, 255);

        var section = new LinearLayout(activity)
        {
            Orientation = Orientation.Vertical,
            ContentDescription = Marker
        };

        var title = Text(activity, thai ? "เครดิต" : "CREDITS", 29, ink, true);
        title.SetPadding(0, Dp(activity, 8), 0, 0);
        section.AddView(title);

        var subtitle = Text(
            activity,
            thai
                ? "ขอขอบคุณทีม VoiceCraft ต้นฉบับและผู้ร่วมพัฒนาที่ทำให้โปรเจกต์นี้เกิดขึ้นได้"
                : "Thanks to the original VoiceCraft team and upstream contributors who made this project possible.",
            12,
            muted,
            false);
        subtitle.SetPadding(0, Dp(activity, 4), 0, Dp(activity, 14));
        section.AddView(subtitle);

        foreach (var profile in Profiles)
        {
            var card = BuildProfileCard(
                activity,
                profile,
                thai,
                ink,
                muted,
                surface,
                surfaceSoft,
                border,
                primary);

            section.AddView(card, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent)
            {
                BottomMargin = Dp(activity, 10)
            });
        }

        var note = Text(
            activity,
            thai
                ? "แตะ OPEN GITHUB เพื่อเปิดโปรไฟล์ต้นฉบับบน GitHub"
                : "Tap OPEN GITHUB to view the original profile on GitHub.",
            11,
            muted,
            false);
        note.SetPadding(Dp(activity, 4), Dp(activity, 2), Dp(activity, 4), 0);
        section.AddView(note);

        return section;
    }

    private static View BuildProfileCard(
        Activity activity,
        CreditProfile profile,
        bool thai,
        Color ink,
        Color muted,
        Color surface,
        Color surfaceSoft,
        Color border,
        Color primary)
    {
        var card = new LinearLayout(activity)
        {
            Orientation = Orientation.Vertical,
            Background = Round(activity, surface, 18, border)
        };
        card.SetPadding(Dp(activity, 14), Dp(activity, 14), Dp(activity, 14), Dp(activity, 12));
        card.Elevation = Dp(activity, 1);
        card.Clickable = true;
        card.Focusable = true;
        card.Click += (_, _) => OpenGitHub(activity, profile.Login);

        var profileRow = new LinearLayout(activity) { Orientation = Orientation.Horizontal };
        profileRow.SetGravity(GravityFlags.CenterVertical);

        var avatarFrame = new FrameLayout(activity)
        {
            Background = Round(activity, surfaceSoft, 18, border),
            ClipToOutline = true
        };

        var fallback = Text(activity, profile.Initials, 17, primary, true);
        fallback.Gravity = GravityFlags.Center;
        avatarFrame.AddView(fallback, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent));

        var avatar = new ImageView(activity);
        avatar.SetScaleType(ImageView.ScaleType.CenterCrop);
        avatarFrame.AddView(avatar, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent));
        LoadAvatar(activity, avatar, profile.Login);

        profileRow.AddView(avatarFrame, new LinearLayout.LayoutParams(Dp(activity, 66), Dp(activity, 66)));

        var copy = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        copy.SetPadding(Dp(activity, 12), 0, 0, 0);
        copy.AddView(Text(activity, profile.Login, 16, ink, true));
        copy.AddView(Text(activity, thai ? profile.RoleThai : profile.RoleEnglish, 12, muted, false));

        var url = Text(activity, $"github.com/{profile.Login}", 11, primary, false);
        url.SetPadding(0, Dp(activity, 3), 0, 0);
        copy.AddView(url);
        profileRow.AddView(copy, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        card.AddView(profileRow);

        var open = new Button(activity)
        {
            Text = "OPEN GITHUB",
            TextSize = 11
        };
        open.SetAllCaps(false);
        open.SetTextColor(primary);
        open.Background = Round(activity, surfaceSoft, 14, border);
        open.SetPadding(Dp(activity, 12), 0, Dp(activity, 12), 0);
        open.Click += (_, _) => OpenGitHub(activity, profile.Login);
        card.AddView(open, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            Dp(activity, 44))
        {
            TopMargin = Dp(activity, 10)
        });

        return card;
    }

    private static void OpenGitHub(Activity activity, string login)
    {
        try
        {
            var uri = AndroidUri.Parse($"https://github.com/{login}");
            activity.StartActivity(new Intent(Intent.ActionView, uri));
        }
        catch
        {
        }
    }

    private static async void LoadAvatar(Activity activity, ImageView image, string login)
    {
        try
        {
            Bitmap? bitmap;
            lock (AvatarLock)
                AvatarCache.TryGetValue(login, out bitmap);

            if (bitmap == null)
            {
                var bytes = await AvatarClient.GetByteArrayAsync($"https://github.com/{System.Uri.EscapeDataString(login)}.png?size=200");
                bitmap = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
                if (bitmap != null)
                {
                    lock (AvatarLock)
                        AvatarCache[login] = bitmap;
                }
            }

            if (bitmap == null || activity.IsFinishing || activity.IsDestroyed)
                return;

            activity.RunOnUiThread(() => image.SetImageBitmap(bitmap));
        }
        catch
        {
            // Keep the initials fallback when GitHub is unavailable.
        }
    }

    private static ScrollView? FindSettingsScroll(View view)
    {
        if (view is ScrollView scroll)
        {
            var settingsTitle = ContainsExactText(scroll, "Settings") || ContainsExactText(scroll, "ตั้งค่า");
            var serverSection = ContainsExactText(scroll, "Voice Server") || ContainsExactText(scroll, "เซิร์ฟเวอร์เสียง");
            if (settingsTitle && serverSection)
                return scroll;
        }

        if (view is not ViewGroup group)
            return null;

        for (var i = 0; i < group.ChildCount; i++)
        {
            var child = group.GetChildAt(i);
            if (child == null)
                continue;

            var found = FindSettingsScroll(child);
            if (found != null)
                return found;
        }

        return null;
    }

    private static bool ContainsExactText(View view, string value)
    {
        if (view is TextView text && string.Equals(text.Text?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            return true;

        if (view is not ViewGroup group)
            return false;

        for (var i = 0; i < group.ChildCount; i++)
        {
            var child = group.GetChildAt(i);
            if (child != null && ContainsExactText(child, value))
                return true;
        }

        return false;
    }

    private static bool ContainsMarker(View view)
    {
        if (string.Equals(view.ContentDescription, Marker, StringComparison.Ordinal))
            return true;

        if (view is not ViewGroup group)
            return false;

        for (var i = 0; i < group.ChildCount; i++)
        {
            var child = group.GetChildAt(i);
            if (child != null && ContainsMarker(child))
                return true;
        }

        return false;
    }

    private static TextView Text(Activity activity, string value, float size, Color color, bool bold)
    {
        var text = new TextView(activity)
        {
            Text = value
        };
        text.SetTextColor(color);
        text.SetTextSize(global::Android.Util.ComplexUnitType.Sp, size);
        if (bold)
            text.SetTypeface(Typeface.Default, TypefaceStyle.Bold);
        return text;
    }

    private static GradientDrawable Round(Activity activity, Color fill, int radiusDp, Color stroke)
    {
        var drawable = new GradientDrawable();
        drawable.SetColor(fill);
        drawable.SetCornerRadius(Dp(activity, radiusDp));
        drawable.SetStroke(Dp(activity, 1), stroke);
        return drawable;
    }

    private static int Dp(Activity activity, int value)
    {
        var density = activity.Resources?.DisplayMetrics?.Density ?? 1f;
        return (int)(value * density + 0.5f);
    }
}
