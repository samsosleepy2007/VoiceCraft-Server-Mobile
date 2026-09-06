using System.Net.Sockets;
using System.Text;
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

            _ = ProbeMcHttpTcpAsync(port);

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
    /// Uses a raw TCP socket so Android cleartext HTTP policy cannot create a
    /// false-negative probe. After the TCP connection succeeds we send a
    /// minimal HTTP/1.1 GET request. A 403 response is expected and proves
    /// the McHttp listener is reachable inside the Android process.
    /// </summary>
    private static async Task ProbeMcHttpTcpAsync(int port)
    {
        await Task.Delay(1200);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var tcp = new TcpClient(AddressFamily.InterNetwork);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await tcp.ConnectAsync("127.0.0.1", port, timeout.Token);
                AndroidRuntimeLog.Append("PROBE", $"TCP 127.0.0.1:{port} connected (attempt {attempt})");

                using var stream = tcp.GetStream();
                var request = Encoding.ASCII.GetBytes(
                    $"GET / HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(request, timeout.Token);
                await stream.FlushAsync(timeout.Token);

                var buffer = new byte[1024];
                var read = await stream.ReadAsync(buffer, timeout.Token);
                var firstLine = read > 0
                    ? Encoding.ASCII.GetString(buffer, 0, read).Split("\r\n", 2)[0]
                    : "(empty HTTP response)";

                AndroidRuntimeLog.Append("PROBE", $"McHttp raw HTTP response: {firstLine}");
                return;
            }
            catch (Exception ex)
            {
                AndroidRuntimeLog.Append("PROBE", $"TCP localhost attempt {attempt} failed: {ex.GetType().Name}: {ex.Message}");
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
#pragma warning disable CS0618
            builder = new Notification.Builder(this);
#pragma warning restore CS0618

        return builder
            .SetContentTitle("VoiceCraft Server")
            .SetContentText(text)
            .SetSmallIcon(global::Android.Resource.Drawable.IcMediaPlay)
            .SetOngoing(true)
            .SetContentIntent(contentIntent)
            .Build();
    }

    private void EnsureNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
            return;

#pragma warning disable CA1416
        var channel = new NotificationChannel(ChannelId, "VoiceCraft Server", NotificationImportance.Low)
        {
            Description = "Keeps the VoiceCraft server running in the background"
        };
        if (GetSystemService(NotificationService) is NotificationManager manager)
            manager.CreateNotificationChannel(channel);
#pragma warning restore CA1416
    }

    private void AcquireWakeLock()
    {
        try
        {
            if (GetSystemService(PowerService) is not PowerManager powerManager)
                return;

#pragma warning disable CA1416
            _wakeLock = powerManager.NewWakeLock(WakeLockFlags.Partial, "VoiceCraftServer::WakeLock");
            _wakeLock?.Acquire();
#pragma warning restore CA1416
            AndroidRuntimeLog.Append("POWER", "Partial wake lock acquired");
        }
        catch (Exception ex)
        {
            AndroidRuntimeLog.Append("POWER", $"Wake lock unavailable: {ex.Message}");
        }
    }

    private void ReleaseWakeLock()
    {
        try
        {
            if (_wakeLock?.IsHeld == true)
                _wakeLock.Release();
        }
        catch
        {
            // Ignore shutdown cleanup errors.
        }
        finally
        {
            _wakeLock?.Dispose();
            _wakeLock = null;
        }
    }

    public override void OnDestroy()
    {
        AndroidRuntimeLog.Append("SERVICE", "Foreground service destroyed");
        _notificationCts?.Cancel();
        _notificationCts?.Dispose();
        _notificationCts = null;
        VcServerApp.Shutdown();
        ReleaseWakeLock();
        IsServiceRunning = false;
        base.OnDestroy();
    }
}
