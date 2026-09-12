using System.Reflection;
using System.Runtime.CompilerServices;
using Android.App;
using Android.Content;
using Android.Database;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidUri = Android.Net.Uri;

namespace VoiceCraft.Server.Android;

[ContentProvider(new[] { "chat.voicecraft.server.serveridhint" }, Exported = false, InitOrder = 1050)]
public sealed class ServerIdUiProvider : ContentProvider
{
    private ServerIdUiLifecycleCallbacks? _callbacks;

    public override bool OnCreate()
    {
        try
        {
            if (Context?.ApplicationContext is Application app)
            {
                _callbacks = new ServerIdUiLifecycleCallbacks();
                app.RegisterActivityLifecycleCallbacks(_callbacks);
            }
        }
        catch (Exception ex)
        {
            AndroidRuntimeLog.Append("UI", $"Server ID UI helper unavailable: {ex.GetType().Name}: {ex.Message}");
        }

        return true;
    }

    public override ICursor? Query(AndroidUri uri, string[]? projection, string? selection, string[]? selectionArgs, string? sortOrder) => null;
    public override string? GetType(AndroidUri uri) => null;
    public override AndroidUri? Insert(AndroidUri uri, ContentValues? values) => null;
    public override int Delete(AndroidUri uri, string? selection, string[]? selectionArgs) => 0;
    public override int Update(AndroidUri uri, ContentValues? values, string? selection, string[]? selectionArgs) => 0;
}

internal sealed class ServerIdUiLifecycleCallbacks : Java.Lang.Object, Application.IActivityLifecycleCallbacks
{
    private readonly Dictionary<Activity, ViewTreeObserver.IOnGlobalLayoutListener> _listeners = new();

    public void OnActivityCreated(Activity activity, Bundle? savedInstanceState) { }
    public void OnActivityStarted(Activity activity) { }

    public void OnActivityResumed(Activity activity)
    {
        if (activity is not ModernMainActivity)
            return;

        ServerIdUiHelper.TryApply(activity);

        if (_listeners.ContainsKey(activity))
            return;

        var observer = activity.Window?.DecorView?.ViewTreeObserver;
        if (observer == null)
            return;

        var listener = new ServerIdLayoutListener(() => ServerIdUiHelper.TryApply(activity));
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

    private sealed class ServerIdLayoutListener : Java.Lang.Object, ViewTreeObserver.IOnGlobalLayoutListener
    {
        private readonly Action _callback;
        public ServerIdLayoutListener(Action callback) => _callback = callback;
        public void OnGlobalLayout() => _callback();
    }
}

internal static class ServerIdUiHelper
{
    private const string HelperMarker = "voicecraft-server-id-help";
    private const string BlankDefaultMigration = "server_id_default_blank_v1";
    private static readonly ConditionalWeakTable<EditText, object> Attached = new();

    public static void TryApply(Activity activity)
    {
        try
        {
            var field = activity.GetType().GetField("_serverId", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field?.GetValue(activity) is not EditText serverId)
                return;

            var prefs = ServerPreferences.Get(activity);
            if (!prefs.GetBoolean(BlankDefaultMigration, false))
            {
                if (string.Equals(serverId.Text?.Trim(), "mcsv-main", StringComparison.OrdinalIgnoreCase))
                {
                    serverId.Text = string.Empty;
                    prefs.Edit()!.PutString(ServerPreferences.ExtraBridgeServerId, string.Empty)!.Apply();
                }
                prefs.Edit()!.PutBoolean(BlankDefaultMigration, true)!.Apply();
            }

            var thai = ServerPreferences.GetLanguage(activity) == "th";
            serverId.Hint = thai ? "ใส่อะไรก็ได้ เช่น ชื่อโปรเจกต์" : "Anything, e.g. your project name";

            if (!Attached.TryGetValue(serverId, out _))
            {
                Attached.Add(serverId, new object());
                serverId.TextChanged += (_, _) =>
                {
                    var value = serverId.Text?.Trim() ?? string.Empty;
                    ServerPreferences.Get(activity).Edit()!
                        .PutString(ServerPreferences.ExtraBridgeServerId, value)!
                        .Apply();
                };
            }

            if (serverId.Parent is not LinearLayout parent || FindHelper(parent) != null)
                return;

            var helper = new TextView(activity)
            {
                Text = thai
                    ? "ใส่อะไรก็ได้ เช่น ชื่อโปรเจกต์ • ใช้เป็น Server ID และช่วยระบุไฟล์ Plugin ที่ดาวน์โหลด"
                    : "Use any name, such as your project name. It is used as the Server ID and to identify the downloaded plugin file.",
                TextSize = 11,
                ContentDescription = HelperMarker
            };
            helper.SetTextColor(serverId.HintTextColors);
            helper.SetPadding(serverId.PaddingLeft, 4, serverId.PaddingRight, 6);

            var index = parent.IndexOfChild(serverId);
            parent.AddView(helper, index + 1, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        }
        catch (Exception ex)
        {
            AndroidRuntimeLog.Append("UI", $"Server ID UI helper skipped: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static View? FindHelper(View view)
    {
        if (string.Equals(view.ContentDescription, HelperMarker, StringComparison.Ordinal))
            return view;

        if (view is not ViewGroup group)
            return null;

        for (var i = 0; i < group.ChildCount; i++)
        {
            var child = group.GetChildAt(i);
            if (child == null)
                continue;
            var found = FindHelper(child);
            if (found != null)
                return found;
        }

        return null;
    }
}
