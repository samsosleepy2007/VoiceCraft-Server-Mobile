using Android.App;
using Android.Content;
using Android.Database;
using Android.OS;
using Android.Widget;
using AndroidUri = Android.Net.Uri;

namespace VoiceCraft.Server.Android;

// VoiceCraft Server is a user-started long-running foreground service. Ask Android
// for a battery-optimization exemption once the user reaches the main server UI.
[ContentProvider(new[] { "chat.voicecraft.server.batteryoptimizationrequest" }, Exported = false, InitOrder = 1075)]
public sealed class BatteryOptimizationRequestProvider : ContentProvider
{
    private BatteryOptimizationLifecycleCallbacks? _callbacks;

    public override bool OnCreate()
    {
        try
        {
            if (Context?.ApplicationContext is Application app)
            {
                _callbacks = new BatteryOptimizationLifecycleCallbacks();
                app.RegisterActivityLifecycleCallbacks(_callbacks);
            }
        }
        catch (Exception ex)
        {
            AndroidRuntimeLog.Append("UI", $"Battery optimization helper unavailable: {ex.GetType().Name}: {ex.Message}");
        }

        return true;
    }

    public override ICursor? Query(AndroidUri uri, string[]? projection, string? selection, string[]? selectionArgs, string? sortOrder) => null;
    public override string? GetType(AndroidUri uri) => null;
    public override AndroidUri? Insert(AndroidUri uri, ContentValues? values) => null;
    public override int Delete(AndroidUri uri, string? selection, string[]? selectionArgs) => 0;
    public override int Update(AndroidUri uri, ContentValues? values, string? selection, string[]? selectionArgs) => 0;
}

internal sealed class BatteryOptimizationLifecycleCallbacks : Java.Lang.Object, Application.IActivityLifecycleCallbacks
{
    private static int _promptedThisProcess;

    public void OnActivityCreated(Activity activity, Bundle? savedInstanceState) { }
    public void OnActivityStarted(Activity activity) { }

    public void OnActivityResumed(Activity activity)
    {
        if (activity is not ModernMainActivity || Interlocked.Exchange(ref _promptedThisProcess, 1) != 0)
            return;

        BatteryOptimizationRequest.TryPrompt(activity);
    }

    public void OnActivityPaused(Activity activity) { }
    public void OnActivityStopped(Activity activity) { }
    public void OnActivitySaveInstanceState(Activity activity, Bundle outState) { }
    public void OnActivityDestroyed(Activity activity) { }
}

internal static class BatteryOptimizationRequest
{
    public static void TryPrompt(Activity activity)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.M || IsUnrestricted(activity))
            return;

        var thai = ServerPreferences.GetLanguage(activity) == "th";

        try
        {
            new AlertDialog.Builder(activity)
                .SetTitle(thai ? "อนุญาตการใช้แบตเตอรี่แบบไม่จำกัด" : "Allow unrestricted battery usage")
                .SetMessage(thai
                    ? "เพื่อช่วยให้ VoiceCraft Server ทำงานเบื้องหลังได้ต่อเนื่องและลดโอกาสที่ Android จะหยุดเซิร์ฟเวอร์ กรุณาอนุญาตให้แอปไม่ถูกจำกัดด้วยการเพิ่มประสิทธิภาพแบตเตอรี่ ระบบ Android จะเป็นผู้แสดงหน้าต่างยืนยันสิทธิ์นี้"
                    : "To help VoiceCraft Server keep running in the background and reduce the chance that Android stops the server, allow the app to be excluded from battery optimization. Android will show the system confirmation screen for this permission.")
                .SetPositiveButton(thai ? "อนุญาต" : "ALLOW", (_, _) => OpenRequest(activity))
                .SetNegativeButton(thai ? "ภายหลัง" : "LATER", (_, _) => { })
                .Show();
        }
        catch (Exception ex)
        {
            AndroidRuntimeLog.Append("UI", $"Battery optimization prompt failed: {ex.GetType().Name}: {ex.Message}");
            OpenRequest(activity);
        }
    }

    private static bool IsUnrestricted(Context context)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.M)
            return true;

        try
        {
            var power = context.GetSystemService(Context.PowerService) as PowerManager;
            return power?.IsIgnoringBatteryOptimizations(context.PackageName) == true;
        }
        catch
        {
            return false;
        }
    }

    private static void OpenRequest(Activity activity)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.M || IsUnrestricted(activity))
            return;

        try
        {
            var packageUri = AndroidUri.Parse($"package:{activity.PackageName}");
            var intent = new Intent(global::Android.Provider.Settings.ActionRequestIgnoreBatteryOptimizations, packageUri);
            activity.StartActivity(intent);
            AndroidRuntimeLog.Append("UI", "Opened Android unrestricted battery request");
            return;
        }
        catch (Exception ex)
        {
            AndroidRuntimeLog.Append("UI", $"Direct battery exemption request unavailable: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            activity.StartActivity(new Intent(global::Android.Provider.Settings.ActionIgnoreBatteryOptimizationSettings));
        }
        catch (Exception ex)
        {
            AndroidRuntimeLog.Append("UI", $"Battery optimization settings unavailable: {ex.GetType().Name}: {ex.Message}");
            var thai = ServerPreferences.GetLanguage(activity) == "th";
            Toast.MakeText(
                activity,
                thai ? "ไม่สามารถเปิดหน้าตั้งค่าแบตเตอรี่ของระบบได้" : "Could not open Android battery optimization settings",
                ToastLength.Long)?.Show();
        }
    }
}
