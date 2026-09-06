using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Text;
using Android.Widget;
using Java.Lang;
using VcServerApp = VoiceCraft.Server.App;

namespace VoiceCraft.Server.Android;

[Activity(
    Name = "chat.voicecraft.server.MainActivity",
    Label = "VoiceCraft Server",
    MainLauncher = true,
    Exported = true)]
public sealed class MainActivity : Activity
{
    private TextView? _status;
    private TextView? _connectionInfo;
    private EditText? _port;
    private EditText? _serverKey;
    private Handler? _handler;
    private IRunnable? _refreshRunnable;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        BuildUi();
        RequestNotificationPermission();
        StartRefreshLoop();
    }

    private void BuildUi()
    {
        var density = Resources?.DisplayMetrics?.Density ?? 1f;
        int Dp(int value) => (int)(value * density + 0.5f);

        var scroll = new ScrollView(this);
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetPadding(Dp(20), Dp(24), Dp(20), Dp(24));
        scroll.AddView(root);

        root.AddView(new TextView(this)
        {
            Text = "VoiceCraft Server • Android Phase 1",
            TextSize = 22
        });

        _status = new TextView(this) { TextSize = 18 };
        _status.SetPadding(0, Dp(18), 0, Dp(8));
        root.AddView(_status);

        _connectionInfo = new TextView(this) { TextSize = 15 };
        _connectionInfo.SetTextIsSelectable(true);
        root.AddView(_connectionInfo);

        root.AddView(new TextView(this) { Text = "Voice / McHttp port", TextSize = 14 });
        _port = new EditText(this)
        {
            InputType = InputTypes.ClassNumber,
            Text = ServerPreferences.GetVoicePort(this).ToString()
        };
        root.AddView(_port);

        root.AddView(new TextView(this) { Text = "Server key", TextSize = 14 });
        _serverKey = new EditText(this)
        {
            InputType = InputTypes.ClassText,
            Text = ServerPreferences.GetServerKey(this)
        };
        _serverKey.SetSingleLine(true);
        root.AddView(_serverKey);

        var start = new Button(this) { Text = "START SERVER" };
        start.Click += (_, _) => StartServer();
        root.AddView(start);

        var stop = new Button(this) { Text = "STOP SERVER" };
        stop.Click += (_, _) => StopServer();
        root.AddView(stop);

        var copy = new Button(this) { Text = "COPY CONNECTION INFO" };
        copy.Click += (_, _) => CopyConnectionInfo();
        root.AddView(copy);

        var note = new TextView(this)
        {
            Text = "Phase 1 is LAN-first. Set battery usage to Unrestricted for reliable background hosting.",
            TextSize = 13
        };
        note.SetPadding(0, Dp(16), 0, 0);
        root.AddView(note);

        SetContentView(scroll);
        RefreshUi();
    }

    private void StartServer()
    {
        if (!int.TryParse(_port?.Text, out var port) || port is < 1 or > 65535)
        {
            Toast.MakeText(this, "Port must be 1-65535", ToastLength.Short)?.Show();
            return;
        }

        var key = _serverKey?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            key = Guid.NewGuid().ToString("N");
            if (_serverKey != null)
                _serverKey.Text = key;
        }

        ServerPreferences.Save(this, port, key);
        var intent = new Intent(this, typeof(VoiceCraftServerService));
        intent.PutExtra(ServerPreferences.ExtraVoicePort, port);
        intent.PutExtra(ServerPreferences.ExtraServerKey, key);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
#pragma warning disable CA1416
            StartForegroundService(intent);
#pragma warning restore CA1416
        else
            StartService(intent);

        RefreshUi();
    }

    private void StopServer()
    {
        VoiceCraft.Server.App.Shutdown();
        StopService(new Intent(this, typeof(VoiceCraftServerService)));
        RefreshUi();
    }

    private void RefreshUi()
    {
        var ip = GetLanIpv4() ?? "(no LAN IPv4 detected)";
        var port = ServerPreferences.GetVoicePort(this);
        var key = ServerPreferences.GetServerKey(this);
        var running = VcServerApp.IsRunning;
        var service = VoiceCraftServerService.IsServiceRunning;

        if (_status != null)
        {
            _status.Text = VoiceCraftServerService.LastError != null
                ? $"Status: ERROR\n{VoiceCraftServerService.LastError}"
                : running
                    ? $"Status: RUNNING • Clients {VcServerApp.ConnectedClients}"
                    : service ? "Status: STARTING…" : "Status: STOPPED";
        }

        if (_connectionInfo != null)
        {
            _connectionInfo.Text =
                $"LAN IP: {ip}\n" +
                $"Voice client: {ip}:{port} (UDP)\n" +
                $"McHttp: http://{ip}:{port} (TCP)\n" +
                $"Server key: {key}\n\n" +
                $"Bedrock command:\n/voicecraft:vcconnect \"http://{ip}:{port}\" \"{key}\"\n";
        }
    }

    private void CopyConnectionInfo()
    {
        var ip = GetLanIpv4() ?? "0.0.0.0";
        var port = ServerPreferences.GetVoicePort(this);
        var key = ServerPreferences.GetServerKey(this);
        var text = $"VoiceCraft: {ip}:{port}\nMcHttp: http://{ip}:{port}\nKey: {key}";

        if (GetSystemService(ClipboardService) is Android.Content.ClipboardManager clipboard)
        {
            clipboard.PrimaryClip = ClipData.NewPlainText("VoiceCraft connection", text);
            Toast.MakeText(this, "Copied", ToastLength.Short)?.Show();
        }
    }

    private void StartRefreshLoop()
    {
        _handler = new Handler(Looper.MainLooper!);
        _refreshRunnable = new Runnable(() =>
        {
            RefreshUi();
            if (_handler != null && _refreshRunnable != null)
                _handler.PostDelayed(_refreshRunnable, 1000);
        });
        _handler.Post(_refreshRunnable);
    }

    protected override void OnDestroy()
    {
        if (_handler != null && _refreshRunnable != null)
            _handler.RemoveCallbacks(_refreshRunnable);
        _refreshRunnable?.Dispose();
        _refreshRunnable = null;
        _handler?.Dispose();
        _handler = null;
        base.OnDestroy();
    }

    private void RequestNotificationPermission()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu)
            return;

#pragma warning disable CA1416
        if (CheckSelfPermission(Manifest.Permission.PostNotifications) != Permission.Granted)
            RequestPermissions([Manifest.Permission.PostNotifications], 1701);
#pragma warning restore CA1416
    }

    private static string? GetLanIpv4()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(x => x.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork &&
                                     !IPAddress.IsLoopback(a))
                ?.ToString();
        }
        catch
        {
            return null;
        }
    }
}
