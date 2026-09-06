using System.Net.Http;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using VoiceCraft.Network.Servers;
using VoiceCraft.Server;
using VcServerApp = VoiceCraft.Server.App;

namespace VoiceCraft.Server.Android;

[Service(
    Name = "chat.voicecraft.server.VoiceCraftServerService",
    Exported = false,
    ForegroundServiceType = ForegroundService.TypeSpecialUse)]
public sealed class VoiceCraftServerService : Service
{
    private const int NotificationId = 1701;
    private const string ChannelId = "voicecraft_server";
    private CancellationTokenSource? _notificationCts;
    private PowerManager.WakeLock? _wakeLock;
    private Task? _serverTask;

    public static bool IsServiceRunning { get; private set; }
    public static string? LastError { get; private set; }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == ServerPreferences.ActionStop)
        {
            AndroidRuntimeLog.Append("SERVICE", "Stop action received");
            VcServerApp.Shutdown();
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        EnsureNotificationChannel();
        StartForeground(NotificationId, BuildNotification("Starting VoiceCraft server…"));

        if (_serverTask is { IsCompleted: false })
        {
            AndroidRuntimeLog.Append("SERVICE", "Start ignored: server task is already active");
            return StartCommandResult.Sticky;
        }

        var port = intent?.GetIntExtra(ServerPreferences.ExtraVoicePort, -1) ?? -1;
        if (port is < 1 or > 65535)
            port = ServerPreferences.GetVoicePort(this);

        var key = intent?.GetStringExtra(ServerPreferences.ExtraServerKey);
        if (string.IsNullOrWhiteSpace(key))
            key = ServerPreferences.GetServerKey(this);

        ServerPreferences.Save(this, port, key);
        IsServiceRunning = true;
        LastError = null;
        AndroidRuntimeLog.Append("SERVICE", $"Starting foreground server on port {port}");
        AndroidRuntimeLog.Append("SERVICE", $"App data: {FilesDir?.AbsolutePath ?? "(unknown)"}");
        AcquireWakeLock();

        _notificationCts?.Cancel();
        _notificationCts?.Dispose();
        _notificationCts = new CancellationTokenSource();
        _ = NotificationLoopAsync(port, _notificationCts.Token);
        _serverTask = Task.Run(() => RunServerAsync(port, key));

        return StartCommandResult.Sticky;
    }

    private async Task RunServerAsync(int port, string key)
    {
        try
        {
            AndroidRuntimeLog.Append("RUNTIME", "Initializing VoiceCraft v1.7.1 server runtime");
            Program.InitializeRuntime(FilesDir?.AbsolutePath);
            ServerConsole.Sink = AndroidRuntimeLog.Append;
            HttpMcApiServer.DiagnosticLog = AndroidRuntimeLog.Append;

            AndroidRuntimeLog.Append("VOICE", $"Requested UDP listener 0.0.0.0:{port}");
            AndroidRuntimeLog.Append("McHttp", $"Requested HTTP listener 0.0.0.0:{port}");
            AndroidRuntimeLog.Append("SECURITY", "Login token loaded (value hidden from log)");

            _ = ProbeMcHttpAsync(port);

            await VcServerApp.Start(new RuntimeOptions
            {
                Headless = true,
                Language = "th-TH",
                TransportMode = ["http"],
                TransportHost = "0.0.0.0",
                TransportPort = port,
                VoicePort = (uint)port,
                ServerKey = key
            });

            AndroidRuntimeLog.Append("RUNTIME", "VoiceCraft server loop ended normally");
        }
        catch (Exception ex)
        {
            LastError = ex.ToString();
            AndroidRuntimeLog.Append("FATAL", ex.ToString());
        }
        finally
        {
            ServerConsole.Sink = null;
            HttpMcApiServer.DiagnosticLog = null;
            IsServiceRunning = false;
            _notificationCts?.Cancel();
            AndroidRuntimeLog.Append("SERVICE", "Server task stopped");
            StopSelf();
        }
    }

    /// <summary>
    /// Probes the McHttp listener from inside the same Android process. A 403 is
    /// expected for GET and proves HttpListener accepted a TCP/HTTP connection.
    /// This helps distinguish server-listener failures from Minecraft-side
    /// networking restrictions.
    /// </summary>
    private static async Task ProbeMcHttpAsync(int port)
    {
        await Task.Delay(1200);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var response = await client.GetAsync($"http://127.0.0.1:{port}/");
                AndroidRuntimeLog.Append(
                    "PROBE",
                    $"McHttp localhost reachable: HTTP {(int)response.StatusCode} {response.StatusCode} (attempt {attempt})");
                return;
            }
            catch (Exception ex)
            {
                AndroidRuntimeLog.Append("PROBE", $"McHttp localhost attempt {attempt} failed: {ex.GetType().Name}: {ex.Message}");
                await Task.Delay(700);
            }
        }
    }

    private async Task NotificationLoopAsync(int port, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var text = LastError != null
                    ? "Server error — open app for details"
                    : VcServerApp.IsRunning
                        ? $"UDP/TCP {port} • Clients {VcServerApp.ConnectedClients}"
                        : $"Starting on {port}…";

                if (GetSystemService(NotificationService) is NotificationManager manager)
                    manager.Notify(NotificationId, BuildNotification(text));

                await Task.Delay(1000, token);
            }
        }
        catch (System.OperationCanceledException)
        {
            // Normal service shutdown.
        }
    }

    private Notification BuildNotification(string text)
    {
        var launchIntent = new Intent(this, typeof(MainActivity));
        var pendingFlags = PendingIntentFlags.UpdateCurrent;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
            pendingFlags |= PendingIntentFlags.Immutable;

        var contentIntent = PendingIntent.GetActivity(this, 0, launchIntent, pendingFlags);
        Notification.Builder builder;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
#pragma warning disable CA1416
            builder = new Notification.Builder(this, ChannelId);
#pragma warning restore CA1416
        else
            builder = new Notification.Builder(this);

        return builder
            .SetContentTitle("VoiceCraft Server")
            .SetContentText(text)
            .SetContentIntent(contentIntent)
            .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
            .SetOngoing(true)
            .Build();
    }

    private void EnsureNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
            return;

#pragma warning disable CA1416
        var channel = new NotificationChannel(ChannelId, "VoiceCraft Server", NotificationImportance.Low)
        {
            Description = "Keeps the VoiceCraft server running in the background."
        };
        if (GetSystemService(NotificationService) is NotificationManager manager)
            manager.CreateNotificationChannel(channel);
#pragma warning restore CA1416
    }

    private void AcquireWakeLock()
    {
        if (GetSystemService(PowerService) is not PowerManager powerManager)
            return;

        _wakeLock = powerManager.NewWakeLock(WakeLockFlags.Partial, "VoiceCraftServer:Runtime");
        _wakeLock?.SetReferenceCounted(false);
        _wakeLock?.Acquire();
        AndroidRuntimeLog.Append("POWER", "Partial wake lock acquired");
    }

    public override void OnDestroy()
    {
        AndroidRuntimeLog.Append("SERVICE", "Android service OnDestroy");
        VcServerApp.Shutdown();
        _notificationCts?.Cancel();
        _notificationCts?.Dispose();
        _notificationCts = null;

        if (_wakeLock?.IsHeld == true)
        {
            _wakeLock.Release();
            AndroidRuntimeLog.Append("POWER", "Partial wake lock released");
        }
        _wakeLock?.Dispose();
        _wakeLock = null;

        IsServiceRunning = false;
        base.OnDestroy();
    }
}
