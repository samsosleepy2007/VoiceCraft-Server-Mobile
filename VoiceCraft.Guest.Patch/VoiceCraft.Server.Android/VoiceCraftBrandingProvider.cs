using Android.App;
using Android.Content;
using Android.Database;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidUri = Android.Net.Uri;

namespace VoiceCraft.Server.Android;

[ContentProvider(new[] { "chat.voicecraft.server.branding" }, Exported = false, InitOrder = 1090)]
public sealed class VoiceCraftBrandingProvider : ContentProvider
{
    private VoiceCraftBrandingLifecycle? _callbacks;

    public override bool OnCreate()
    {
        try
        {
            if (Context?.ApplicationContext is Application app)
            {
                _callbacks = new VoiceCraftBrandingLifecycle();
                app.RegisterActivityLifecycleCallbacks(_callbacks);
            }
        }
        catch (Exception ex)
        {
            AndroidRuntimeLog.Append("UI", $"VoiceCraft Server branding helper unavailable: {ex.GetType().Name}: {ex.Message}");
        }

        return true;
    }

    public override ICursor? Query(AndroidUri uri, string[]? projection, string? selection, string[]? selectionArgs, string? sortOrder) => null;
    public override string? GetType(AndroidUri uri) => null;
    public override AndroidUri? Insert(AndroidUri uri, ContentValues? values) => null;
    public override int Delete(AndroidUri uri, string? selection, string[]? selectionArgs) => 0;
    public override int Update(AndroidUri uri, ContentValues? values, string? selection, string[]? selectionArgs) => 0;
}

internal sealed class VoiceCraftBrandingLifecycle : Java.Lang.Object, Application.IActivityLifecycleCallbacks
{
    private readonly Dictionary<Activity, ViewTreeObserver.IOnGlobalLayoutListener> _listeners = new();

    public void OnActivityCreated(Activity activity, Bundle? savedInstanceState) { }
    public void OnActivityStarted(Activity activity) { }

    public void OnActivityResumed(Activity activity)
    {
        VoiceCraftBranding.Apply(activity.Window?.DecorView);

        if (_listeners.ContainsKey(activity))
            return;

        var observer = activity.Window?.DecorView?.ViewTreeObserver;
        if (observer == null)
            return;

        var listener = new BrandingLayoutListener(() => VoiceCraftBranding.Apply(activity.Window?.DecorView));
        observer.AddOnGlobalLayoutListener(listener);
        _listeners[activity] = listener;
    }

    public void OnActivityPaused(Activity activity) { }
    public void OnActivityStopped(Activity activity) { }
    public void OnActivitySaveInstanceState(Activity activity, Bundle outState) { }

    public void OnActivityDestroyed(Activity activity)
    {
        if (!_listeners.Remove(activity, out var listener))
            return;

        var observer = activity.Window?.DecorView?.ViewTreeObserver;
        if (observer?.IsAlive == true)
            observer.RemoveOnGlobalLayoutListener(listener);
    }

    private sealed class BrandingLayoutListener : Java.Lang.Object, ViewTreeObserver.IOnGlobalLayoutListener
    {
        private readonly Action _callback;
        public BrandingLayoutListener(Action callback) => _callback = callback;
        public void OnGlobalLayout() => _callback();
    }
}

internal static class VoiceCraftBranding
{
    private static readonly (string Old, string New)[] Replacements =
    [
        ("เซิร์ฟเวอร์เสียง VoiceCraft บน Android", "VoiceCraft Server"),
        ("VoiceCraft voice server on Android", "VoiceCraft Server"),
        ("Android VoiceCraft control center", "VoiceCraft Server control center"),
        ("Android • VoiceCraft 1.7.1", "VoiceCraft Server • 1.7.1"),
        ("Android Server", "VoiceCraft Server"),
        ("เสียงยังใช้ UDP ไป Android โดยตรง", "เสียงยังใช้ UDP ไป VoiceCraft Server โดยตรง"),
        ("Voice audio still uses UDP directly to Android", "Voice audio still uses UDP directly to VoiceCraft Server"),
        ("ตอนทดสอบให้ Client อยู่ LAN เดียวกับ Android", "ตอนทดสอบให้ Client อยู่ LAN เดียวกับ VoiceCraft Server"),
        ("keep the client on the same LAN as Android", "keep the client on the same LAN as VoiceCraft Server"),
        ("Audio still uses UDP directly to Android;", "Audio still uses UDP directly to VoiceCraft Server;"),
        ("2) ตั้งค่า Android", "2) ตั้งค่า VoiceCraft Server"),
        ("2) Configure Android", "2) Configure VoiceCraft Server"),
        ("Android Bridge", "VoiceCraft Server Bridge")
    ];

    public static void Apply(View? view)
    {
        if (view == null)
            return;

        if (view is TextView textView)
        {
            var value = textView.Text?.ToString();
            if (!string.IsNullOrEmpty(value))
            {
                var changed = value;
                foreach (var pair in Replacements)
                    changed = changed.Replace(pair.Old, pair.New, StringComparison.Ordinal);
                if (!string.Equals(value, changed, StringComparison.Ordinal))
                    textView.Text = changed;
            }
        }

        if (view is not ViewGroup group)
            return;

        for (var i = 0; i < group.ChildCount; i++)
            Apply(group.GetChildAt(i));
    }
}
