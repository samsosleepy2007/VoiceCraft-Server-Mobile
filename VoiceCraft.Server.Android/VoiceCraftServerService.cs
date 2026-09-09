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
    private EndstoneBridgeController? _bridgeController;
    private static EndstoneBridgeController? _activeBridgeController;

    public static bool IsServiceRunning { get; private set; }
    public static string? LastError { get; private set; }
    public static string BridgeStatus { get; private set; } = "disabled";
    public static string BridgeLastError { get; private set; } = string.Empty;
    public static string BridgeActiveRelay { get; private set; } = "Primary";
    public static int BridgeActiveRelayIndex { get; private set; }
    public static int BridgeRelayCount { get; private set; } = 1;
    internal static BridgeDashboardSnapshot BridgeDashboard { get; private set; } = BridgeDashboardSnapshot.Empty;

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
        StartForeground(NotificationId, BuildNotification(IsThai() ? "กำลังตรวจสอบการตั้งค่าก่อนเริ่ม…" : "Checking required setup before start…"));

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

        var bridgeUrl = ServerPreferences.GetBridgeUrl(this);
        var bridgeServerId = ServerPreferences.GetBridgeServerId(this);
        var bridgeSecret = ServerPreferences.GetBridgeSecret(this);
        var bridgeRelayUrls = new List<string> { bridgeUrl };
        foreach (var backup in ServerPreferences.GetBridgeBackupUrls(this))
        {
            if (!IsBridgeUrlValid(backup))
            {
                AndroidRuntimeLog.Append("BRIDGE", $"Ignoring invalid optional Backup Relay: {SafeBridgeUrl(backup)}");
                continue;
            }
            if (!bridgeRelayUrls.Contains(backup, StringComparer.OrdinalIgnoreCase))
                bridgeRelayUrls.Add(backup);
        }

        if (!IsValidBridgeConfig(bridgeUrl, bridgeServerId, bridgeSecret))
        {
            RejectStartupForMissingBridgeConfig(bridgeUrl, bridgeServerId, bridgeSecret);
#pragma warning disable CS0618
            StopForeground(true);
#pragma warning restore CS0618
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        ServerPreferences.Save(this, port, key);
        ServerPreferences.SaveBridge(this, true, bridgeUrl, bridgeServerId, bridgeSecret);
        IsServiceRunning = true;
        LastError = null;
        BridgeLastError = string.Empty;
        BridgeStatus = "starting";
        BridgeActiveRelay = "Primary";
        BridgeActiveRelayIndex = 0;
        BridgeRelayCount = bridgeRelayUrls.Count;
        BridgeDashboard = BridgeDashboardSnapshot.Empty;
        AndroidRuntimeLog.Append("SERVICE", $"Starting foreground server on port {port}");
        AndroidRuntimeLog.Append("SERVICE", $"App data: {FilesDir?.AbsolutePath ?? "(unknown)"}");
        AndroidRuntimeLog.Append(
            "BRIDGE",
            $"Configured Primary relay={SafeBridgeUrl(bridgeUrl)} backups={bridgeRelayUrls.Count - 1} server_id={bridgeServerId}; shared secret hidden");
        AcquireWakeLock();

        _notificationCts?.Cancel();
        _notificationCts?.Dispose();
        _notificationCts = new CancellationTokenSource();
        _ = NotificationLoopAsync(port, _notificationCts.Token);
        _serverTask = Task.Run(() => RunServerAsync(port, key, bridgeRelayUrls, bridgeServerId, bridgeSecret));

        return StartCommandResult.Sticky;
    }

    private void RejectStartupForMissingBridgeConfig(string url, string serverId, string secret)
    {
        var missing = new List<string>();
        if (!IsBridgeUrlValid(url))
            missing.Add("Render/WebSocket URL");
        if (string.IsNullOrWhiteSpace(serverId))
            missing.Add("Server ID");
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 16)
            missing.Add("Bridge Secret");

        var missingText = missing.Count == 0 ? "bridge configuration" : string.Join(", ", missing);
        LastError = $"CONFIG_REQUIRED: VoiceCraft Server startup was stopped because required Render Relay configuration is missing or invalid: {missingText}. Open Bridge Setup in the Android app.";
        BridgeLastError = LastError;
        BridgeStatus = "invalid-config";
        IsServiceRunning = false;
        AndroidRuntimeLog.Append("FATAL", LastError);
        var advice = RuntimeDiagnostics.Describe(LastError, IsThai());
        AndroidRuntimeLog.Append("HELP", $"{advice.Title} | Cause: {advice.Cause} | Fix: {advice.Fix}");
    }

    private async Task RunServerAsync(
        int port,
        string key,
        IReadOnlyList<string> bridgeRelayUrls,
        string bridgeServerId,
        string bridgeSecret)
    {
        try
        {
            var bridgeUrl = bridgeRelayUrls.Count > 0 ? bridgeRelayUrls[0] : string.Empty;
            if (!IsValidBridgeConfig(bridgeUrl, bridgeServerId, bridgeSecret))
                throw new InvalidOperationException("CONFIG_REQUIRED: Render Relay configuration became invalid before runtime startup.");

            AndroidRuntimeLog.Append("RUNTIME", "Initializing VoiceCraft v1.7.1 server runtime");
            Program.InitializeRuntime(FilesDir?.AbsolutePath);
            ServerConsole.Sink = AndroidRuntimeLog.Append;
            HttpMcApiServer.DiagnosticLog = AndroidRuntimeLog.Append;

            AndroidRuntimeLog.Append("VOICE", $"Requested UDP listener 0.0.0.0:{port}");
            AndroidRuntimeLog.Append("McHttp", $"Requested HTTP listener 0.0.0.0:{port}");
            AndroidRuntimeLog.Append("SECURITY", "Login token loaded (value hidden from log)");

            _ = ProbeMcHttpTcpAsync(port);
            var language = IsThai() ? "th-TH" : "en-US";

            var appTask = VcServerApp.Start(new RuntimeOptions
            {
                Headless = true,
                Language = language,
                TransportMode = ["http"],
                TransportHost = "0.0.0.0",
                TransportPort = port,
                VoicePort = (uint)port,
                ServerKey = key
            });

            _bridgeController = new EndstoneBridgeController(bridgeRelayUrls, bridgeServerId, bridgeSecret);
            _activeBridgeController = _bridgeController;
            _bridgeController.Start();
            BridgeStatus = "starting";

            await appTask;
            AndroidRuntimeLog.Append("RUNTIME", "VoiceCraft server loop ended normally");
        }
        catch (Exception ex)
        {
            LastError = ex.ToString();
            AndroidRuntimeLog.Append("FATAL", ex.ToString());
            var advice = RuntimeDiagnostics.Describe(LastError, IsThai());
            AndroidRuntimeLog.Append("HELP", $"{advice.Title} | Cause: {advice.Cause} | Fix: {advice.Fix}");
        }
        finally
        {
            var bridgeController = _bridgeController;
            if (bridgeController is not null)
                await bridgeController.DisposeAsync();
            if (ReferenceEquals(_activeBridgeController, bridgeController))
                _activeBridgeController = null;
            _bridgeController = null;
            BridgeDashboard = BridgeDashboardSnapshot.Empty;
            BridgeStatus = "disabled";
            BridgeLastError = string.Empty;
            BridgeActiveRelay = "Primary";
            BridgeActiveRelayIndex = 0;
            BridgeRelayCount = 1;
            ServerConsole.Sink = null;
            HttpMcApiServer.DiagnosticLog = null;
            IsServiceRunning = false;
            _notificationCts?.Cancel();
            AndroidRuntimeLog.Append("SERVICE", "Server task stopped");
            StopSelf();
        }
    }

    internal static bool RequestBridgeSnapshot()
    {
        var controller = _activeBridgeController;
        if (controller is null)
            return false;
        controller.RequestSnapshot();
        return true;
    }

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
                if (_bridgeController is not null)
                {
                    BridgeStatus = _bridgeController.Status;
                    BridgeLastError = _bridgeController.LastError;
                    BridgeActiveRelay = _bridgeController.ActiveRelayName;
                    BridgeActiveRelayIndex = _bridgeController.ActiveRelayIndex;
                    BridgeRelayCount = _bridgeController.RelayCount;
                    BridgeDashboard = _bridgeController.DashboardSnapshot;
                }

                var thai = IsThai();
                var text = LastError != null
                    ? thai ? "เซิร์ฟเวอร์เกิดข้อผิดพลาด — เปิดแอปเพื่อดูสาเหตุ" : "Server error — open app for details"
                    : VcServerApp.IsRunning
                        ? $"UDP/TCP {port} • Clients {VcServerApp.ConnectedClients} • {BridgeActiveRelay} • Bridge {BridgeStatus}"
                        : thai ? $"กำลังเริ่มที่พอร์ต {port}…" : $"Starting on {port}…";

                if (GetSystemService(NotificationService) is NotificationManager manager)
                    manager.Notify(NotificationId, BuildNotification(text));

                await Task.Delay(1000, token);
            }
        }
        catch (System.OperationCanceledException)
        {
        }
    }

    private static bool IsValidBridgeConfig(string url, string serverId, string secret)
    {
        if (string.IsNullOrWhiteSpace(serverId) || string.IsNullOrWhiteSpace(secret) || secret.Length < 16)
            return false;
        return IsBridgeUrlValid(url);
    }

    private static bool IsBridgeUrlValid(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme is not ("ws" or "wss"))
            return false;
        if (string.IsNullOrWhiteSpace(uri.Host) || url.Contains("YOUR-RELAY", StringComparison.OrdinalIgnoreCase))
            return false;
        return string.Equals(uri.AbsolutePath.TrimEnd('/'), "/bridge", StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeBridgeUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return "(invalid)";
        return uri.GetLeftPart(UriPartial.Path);
    }

    private bool IsThai() => ServerPreferences.GetLanguage(this) == "th";

    private Notification BuildNotification(string text)
    {
        var launchIntent = new Intent(this, typeof(ModernMainActivity));
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
            Description = IsThai()
                ? "ทำให้ VoiceCraft Server ทำงานเบื้องหลังต่อเนื่อง"
                : "Keeps the VoiceCraft server running in the background"
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
        BridgeLastError = string.Empty;
        base.OnDestroy();
    }
}
