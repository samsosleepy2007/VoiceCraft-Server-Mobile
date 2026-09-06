using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
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
            VcServerApp.Shutdown();
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        EnsureNotificationChannel();
        StartForeground(NotificationId, BuildNotification("Starting VoiceCraft server…"));

        if (_serverTask is { IsCompleted: false })
            return StartCommandResult.Sticky;

        var port = intent?.GetIntExtra(ServerPreferences.ExtraVoicePort, -1) ?? -1;
        if (port is < 1 or > 65535)
            port = ServerPreferences.GetVoicePort(this);

        var key = intent?.GetStringExtra(ServerPreferences.ExtraServerKey);
        if (string.IsNullOrWhiteSpace(key))
            key = ServerPreferences.GetServerKey(this);

        ServerPreferences.Save(this, port, key);
        IsServiceRunning = true;
        LastError = null;
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
            Program.InitializeRuntime(FilesDir?.AbsolutePath);
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
        }
        catch (Exception ex)
        {
            LastError = ex.ToString();
        }
        finally
        {
            IsServiceRunning = false;
            _notificationCts?.Cancel();
            StopSelf();
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
    }

    public override void OnDestroy()
    {
        VcServerApp.Shutdown();
        _notificationCts?.Cancel();
        _notificationCts?.Dispose();
        _notificationCts = null;

        if (_wakeLock?.IsHeld == true)
            _wakeLock.Release();
        _wakeLock?.Dispose();
        _wakeLock = null;

        IsServiceRunning = false;
        base.OnDestroy();
    }
}
