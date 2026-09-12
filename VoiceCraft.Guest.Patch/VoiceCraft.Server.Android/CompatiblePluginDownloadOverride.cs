using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Android.App;
using Android.Content;
using Android.Database;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Widget;
using AndroidUri = Android.Net.Uri;

namespace VoiceCraft.Server.Android;

// Starts before activities without replacing the app's existing Application class.
// It registers a lifecycle callback that replaces the older plugin-download button
// with the v3 downloader. This keeps the change isolated for branch testing.
[ContentProvider(new[] { "chat.voicecraft.server.compatiblepluginbootstrap" }, Exported = false, InitOrder = 1000)]
public sealed class CompatiblePluginDownloadBootstrapProvider : ContentProvider
{
    private CompatiblePluginLifecycleCallbacks? _callbacks;

    public override bool OnCreate()
    {
        try
        {
            if (Context?.ApplicationContext is Application app)
            {
                _callbacks = new CompatiblePluginLifecycleCallbacks();
                app.RegisterActivityLifecycleCallbacks(_callbacks);
            }
        }
        catch (Exception ex)
        {
            AndroidRuntimeLog.Append("UI", $"Compatible plugin bootstrap skipped: {CompatiblePluginDownloader.DescribeException(ex)}");
        }

        return true;
    }

    public override ICursor? Query(AndroidUri uri, string[]? projection, string? selection, string[]? selectionArgs, string? sortOrder) => null;
    public override string? GetType(AndroidUri uri) => null;
    public override AndroidUri? Insert(AndroidUri uri, ContentValues? values) => null;
    public override int Delete(AndroidUri uri, string? selection, string[]? selectionArgs) => 0;
    public override int Update(AndroidUri uri, ContentValues? values, string? selection, string[]? selectionArgs) => 0;
}

internal sealed class CompatiblePluginLifecycleCallbacks : Java.Lang.Object, Application.IActivityLifecycleCallbacks
{
    private readonly Dictionary<Activity, ViewTreeObserver.IOnGlobalLayoutListener> _listeners = new();

    public void OnActivityCreated(Activity activity, Bundle? savedInstanceState) { }
    public void OnActivityStarted(Activity activity) { }

    public void OnActivityResumed(Activity activity)
    {
        if (activity is not ModernMainActivity)
            return;

        CompatiblePluginDownloadInjector.TryInject(activity);

        if (_listeners.ContainsKey(activity))
            return;

        var observer = activity.Window?.DecorView?.ViewTreeObserver;
        if (observer == null)
            return;

        var listener = new CompatiblePluginLayoutListener(() => CompatiblePluginDownloadInjector.TryInject(activity));
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

    private sealed class CompatiblePluginLayoutListener : Java.Lang.Object, ViewTreeObserver.IOnGlobalLayoutListener
    {
        private readonly Action _callback;
        public CompatiblePluginLayoutListener(Action callback) => _callback = callback;
        public void OnGlobalLayout() => _callback();
    }
}

internal static class CompatiblePluginDownloadInjector
{
    private const string Marker = "voicecraft-compatible-plugin-download-v3";
    private const string PreviousMarker = "voicecraft-configured-plugin-download";

    private static readonly string[] LegacyButtonTexts =
    {
        "COPY ALL SETUP",
        "คัดลอกข้อมูลตั้งค่าทั้งหมด",
        "คัดลอกทั้งหมด"
    };

    public static void TryInject(Activity activity)
    {
        try
        {
            var root = activity.Window?.DecorView;
            if (root == null || FindByContentDescription(root, Marker) != null)
                return;

            var target = FindByContentDescription(root, PreviousMarker) as Button ?? FindLegacyButton(root);
            if (target?.Parent is not ViewGroup parent)
                return;

            var index = parent.IndexOfChild(target);
            if (index < 0)
                return;

            var thai = ServerPreferences.GetLanguage(activity) == "th";
            var replacement = new Button(activity)
            {
                Text = thai ? "ดาวน์โหลด Plugin" : "DOWNLOAD PLUGIN",
                TextSize = target.TextSize / (activity.Resources?.DisplayMetrics?.ScaledDensity ?? 1f),
                ContentDescription = Marker,
                Gravity = target.Gravity,
                Enabled = target.Enabled,
                Alpha = target.Alpha,
                Elevation = target.Elevation
            };
            replacement.SetAllCaps(false);
            replacement.SetTextColor(target.TextColors);
            replacement.SetTypeface(target.Typeface, target.Typeface?.Style ?? global::Android.Graphics.TypefaceStyle.Normal);
            replacement.SetPadding(target.PaddingLeft, target.PaddingTop, target.PaddingRight, target.PaddingBottom);
            replacement.Background = target.Background;

            var layout = target.LayoutParameters;
            parent.RemoveViewAt(index);
            if (layout != null)
                parent.AddView(replacement, index, layout);
            else
                parent.AddView(replacement, index);

            replacement.Click += (_, _) => BeginDownload(activity);
            AndroidRuntimeLog.Append("UI", "Compatible GitHub plugin downloader v3 active");
        }
        catch (Exception ex)
        {
            AndroidRuntimeLog.Append("UI", $"Compatible plugin button injection skipped: {CompatiblePluginDownloader.DescribeException(ex)}");
        }
    }

    private static void BeginDownload(Activity activity)
    {
        try
        {
            var websocket = InvokeString(activity, "CurrentWebSocket");
            var serverId = InvokeString(activity, "CurrentServerId");
            var secret = InvokeString(activity, "CurrentSecret");
            var config = InvokePluginConfig(activity);

            if (!IsBridgeReady(websocket, serverId, secret) || string.IsNullOrWhiteSpace(config))
            {
                ShowMissingConfiguration(activity);
                return;
            }

            AndroidRuntimeLog.Append("UI", "Compatible Endstone plugin download requested");
            _ = CompatiblePluginDownloader.DownloadAsync(activity, config);
        }
        catch (Exception ex)
        {
            AndroidRuntimeLog.Append("UI", $"Compatible plugin setup failed before download: {CompatiblePluginDownloader.DescribeException(ex)}");
            ShowMissingConfiguration(activity);
        }
    }

    private static bool IsBridgeReady(string websocket, string serverId, string secret)
    {
        if (!Uri.TryCreate(websocket, UriKind.Absolute, out var uri))
            return false;
        if (!uri.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals("ws", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!uri.AbsolutePath.TrimEnd('/').EndsWith("/bridge", StringComparison.OrdinalIgnoreCase))
            return false;
        return !string.IsNullOrWhiteSpace(serverId) && secret.Length >= 16;
    }

    private static string InvokeString(Activity activity, string methodName)
    {
        var method = activity.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        return method?.Invoke(activity, null)?.ToString()?.Trim() ?? string.Empty;
    }

    private static string InvokePluginConfig(Activity activity)
    {
        var method = activity.GetType().GetMethod("PluginConfig", BindingFlags.Instance | BindingFlags.NonPublic);
        return method?.Invoke(activity, new object[] { false })?.ToString() ?? string.Empty;
    }

    private static void ShowMissingConfiguration(Activity activity)
    {
        var thai = ServerPreferences.GetLanguage(activity) == "th";
        new AlertDialog.Builder(activity)
            .SetTitle(thai ? "ยังดาวน์โหลด Plugin ไม่ได้" : "Plugin is not ready to download")
            .SetMessage(thai
                ? "กรอก Render Relay, Server ID และ Bridge Secret ให้ครบก่อน จากนั้นกดดาวน์โหลดอีกครั้ง"
                : "Complete Render Relay, Server ID and Bridge Secret first, then download again.")
            .SetPositiveButton(thai ? "ตกลง" : "OK", (_, _) => { })
            .Show();
    }

    private static Button? FindLegacyButton(View view)
    {
        if (view is Button button)
        {
            var text = button.Text?.ToString()?.Trim() ?? string.Empty;
            if (LegacyButtonTexts.Any(candidate => string.Equals(text, candidate, StringComparison.OrdinalIgnoreCase)))
                return button;
        }

        if (view is not ViewGroup group)
            return null;

        for (var i = 0; i < group.ChildCount; i++)
        {
            var child = group.GetChildAt(i);
            if (child == null)
                continue;
            var found = FindLegacyButton(child);
            if (found != null)
                return found;
        }

        return null;
    }

    private static View? FindByContentDescription(View view, string value)
    {
        if (string.Equals(view.ContentDescription, value, StringComparison.Ordinal))
            return view;

        if (view is not ViewGroup group)
            return null;

        for (var i = 0; i < group.ChildCount; i++)
        {
            var child = group.GetChildAt(i);
            if (child == null)
                continue;
            var found = FindByContentDescription(child, value);
            if (found != null)
                return found;
        }

        return null;
    }
}

internal static class CompatiblePluginDownloader
{
    private const string Repository = "samsosleepy2007/VoiceCraft-Server-Mobile-Unofficial";
    private const int SupportedProtocol = 1;
    private const int MaxPluginBytes = 10 * 1024 * 1024;
    private const int MaxRedirects = 5;
    private const int MaxAttempts = 3;
    private const string ConfigEntryName = "endstone_voicecraft/config.toml";
    private const string LatestReleaseApi = "https://api.github.com/repos/" + Repository + "/releases/latest";

    private static readonly HttpClient Client = CreateClient();

    private sealed record PluginRelease(
        string Tag,
        string Version,
        string FileName,
        string AssetApiUrl,
        string Sha256,
        long Size);

    private sealed class ProgressUi
    {
        public Dialog? Dialog { get; init; }
        public ProgressBar? Progress { get; init; }
        public TextView? Percent { get; init; }
        public TextView? Status { get; init; }
        public ProgressDialog? NativeProgress { get; init; }
    }

    public static async Task DownloadAsync(Activity activity, string configuredToml)
    {
        var thai = ServerPreferences.GetLanguage(activity) == "th";
        var stage = thai ? "กำลังเปิดหน้าต่างดาวน์โหลด" : "Opening download window";
        ProgressUi? ui = null;

        try
        {
            ui = TryShowProgress(activity, thai);

            stage = thai ? "ตรวจสอบการตั้งค่า" : "Checking configuration";
            Update(activity, ui, 3, thai ? "กำลังตรวจสอบ Render Relay, Server ID และ Secret" : "Checking Render Relay, Server ID and secret");

            stage = thai ? "ค้นหา Plugin เวอร์ชันล่าสุดที่รองรับ" : "Finding the latest compatible plugin";
            Update(activity, ui, 8, thai ? "กำลังตรวจสอบ GitHub Release ล่าสุด" : "Checking the latest GitHub Release");
            var release = await ResolveLatestCompatibleReleaseAsync(activity, ui, thai);

            stage = thai ? "ดาวน์โหลด Endstone Plugin จาก GitHub" : "Downloading Endstone plugin from GitHub";
            Update(activity, ui, 20,
                thai ? $"พบ Endstone Plugin v{release.Version} — กำลังดาวน์โหลด" : $"Found Endstone Plugin v{release.Version} — downloading");
            var wheel = await DownloadWheelWithRetryAsync(activity, ui, thai, release);

            stage = thai ? "ตรวจสอบ SHA-256 ของ Plugin" : "Verifying plugin SHA-256";
            Update(activity, ui, 70, thai ? "กำลังตรวจสอบ SHA-256 จาก GitHub Release" : "Verifying SHA-256 from the GitHub Release");
            VerifySourceWheel(wheel, release.Sha256);

            stage = thai ? "ตรวจสอบเวอร์ชันภายใน Wheel" : "Validating wheel version";
            Update(activity, ui, 76, thai ? $"กำลังตรวจสอบ Metadata ของ Endstone Plugin v{release.Version}" : $"Validating Endstone Plugin v{release.Version} metadata");
            ValidateSourceWheelIdentity(wheel, release.Version);

            stage = thai ? "ใส่ Render Relay และ Bridge Secret ลง Plugin" : "Injecting relay and secret into plugin";
            Update(activity, ui, 82,
                thai
                    ? "กำลังใส่ Render Relay, Server ID, Backup Relay และ Bridge Secret ลงใน Plugin"
                    : "Injecting Render Relay, Server ID, backup relays and Bridge Secret into the plugin");
            var provisionedWheel = PatchWheel(wheel, configuredToml);

            stage = thai ? "ตรวจสอบ Wheel RECORD" : "Validating wheel RECORD";
            Update(activity, ui, 90, thai ? "กำลังอัปเดต Wheel RECORD และตรวจสอบแพ็กเกจ" : "Updating Wheel RECORD and validating the package");
            ValidateProvisionedWheel(provisionedWheel, configuredToml, release.Version);

            stage = thai ? "บันทึก Plugin ลง Downloads" : "Saving plugin to Downloads";
            Update(activity, ui, 95, thai ? "กำลังบันทึก Plugin ลงโฟลเดอร์ Downloads" : "Saving the plugin to Downloads");
            var location = await SaveToDownloadsAsync(activity, provisionedWheel, release.FileName);

            stage = thai ? "เสร็จสิ้น" : "Complete";
            Update(activity, ui, 100,
                thai ? $"เสร็จแล้ว — Endstone Plugin v{release.Version} พร้อมใช้งาน" : $"Done — Endstone Plugin v{release.Version} is ready");
            AndroidRuntimeLog.Append("UI", $"Compatible Endstone plugin v{release.Version} created successfully at {location}");
            await Task.Delay(300);

            activity.RunOnUiThread(() =>
            {
                DismissSafely(ui);
                try
                {
                    new AlertDialog.Builder(activity)
                        .SetTitle(thai ? "ดาวน์โหลด Plugin สำเร็จ" : "Plugin download complete")
                        .SetMessage(thai
                            ? $"Endstone Plugin v{release.Version}\nบันทึกแล้วที่:\n{location}\n\nแอปตรวจสอบว่าเป็น Release ล่าสุดที่ใช้ Protocol {SupportedProtocol}, ตรวจ SHA-256 และตรวจ Version ภายใน Wheel แล้ว\n\nไฟล์นี้มี Render Relay, Server ID, Backup Relay และ Bridge Secret อยู่ภายใน กรุณาอย่าแชร์กับบุคคลที่ไม่ไว้ใจ"
                            : $"Endstone Plugin v{release.Version}\nSaved to:\n{location}\n\nThe app verified that this is the latest release using protocol {SupportedProtocol}, checked its SHA-256 and verified the version inside the wheel.\n\nThis file contains the Render Relay, Server ID, backup relays and Bridge Secret. Do not share it with untrusted people.")
                        .SetPositiveButton(thai ? "ตกลง" : "OK", (_, _) => { })
                        .Show();
                }
                catch
                {
                    Toast.MakeText(activity, thai ? "ดาวน์โหลด Plugin สำเร็จ" : "Plugin download complete", ToastLength.Long)?.Show();
                }
            });
        }
        catch (Exception ex)
        {
            AndroidRuntimeLog.Append("UI", $"Compatible Endstone plugin download failed at [{stage}]: {DescribeException(ex)}");
            activity.RunOnUiThread(() =>
            {
                DismissSafely(ui);
                var message = SafeMessage(ex, thai);
                try
                {
                    new AlertDialog.Builder(activity)
                        .SetTitle(thai ? "ดาวน์โหลด Plugin ไม่สำเร็จ" : "Plugin download failed")
                        .SetMessage(thai
                            ? $"เกิดข้อผิดพลาดในขั้นตอน:\n{stage}\n\n{message}\n\nดูหน้า Logs แล้วส่งบรรทัดที่มีคำว่า 'failed at' หากยังเกิดปัญหา"
                            : $"The download failed during:\n{stage}\n\n{message}\n\nCheck Logs and send the line containing 'failed at' if the problem continues.")
                        .SetPositiveButton(thai ? "ปิด" : "CLOSE", (_, _) => { })
                        .Show();
                }
                catch
                {
                    Toast.MakeText(activity, thai ? $"ดาวน์โหลดล้มเหลว: {stage}" : $"Download failed: {stage}", ToastLength.Long)?.Show();
                }
            });
        }
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(75)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("VoiceCraft-Server-Mobile/1.7.1-compatible-plugin-v3");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static async Task<PluginRelease> ResolveLatestCompatibleReleaseAsync(Activity activity, ProgressUi? ui, bool thai)
    {
        Update(activity, ui, 10, thai ? "กำลังอ่านข้อมูล GitHub Release ล่าสุด" : "Reading the latest GitHub Release metadata");
        using var releaseDoc = JsonDocument.Parse(await GetTextAsync(LatestReleaseApi, "application/vnd.github+json"));
        var root = releaseDoc.RootElement;

        var tag = RequiredString(root, "tag_name");
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean())
            throw new InvalidDataException("Latest GitHub release is still a draft.");
        if (root.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean())
            throw new InvalidDataException("Latest GitHub release is a prerelease.");

        Update(activity, ui, 13, thai ? "กำลังตรวจสอบ Compatibility Manifest ของ Release" : "Checking the release compatibility manifest");
        var manifestUrl = $"https://api.github.com/repos/{Repository}/contents/release-manifest.json?ref={Uri.EscapeDataString(tag)}";
        using var manifestDoc = JsonDocument.Parse(await GetTextAsync(manifestUrl, "application/vnd.github.raw+json"));
        var manifest = manifestDoc.RootElement;

        var manifestTag = RequiredString(manifest, "tag");
        var version = RequiredString(manifest, "endstone");
        var protocol = RequiredInt(manifest, "protocol");

        if (!string.Equals(manifestTag, tag, StringComparison.Ordinal))
            throw new InvalidDataException("Release manifest tag does not match the latest GitHub release.");
        if (protocol != SupportedProtocol)
            throw new InvalidDataException($"Latest plugin uses protocol {protocol}, but this app supports protocol {SupportedProtocol}. Update the Android app before downloading this plugin.");
        if (!IsSafeVersion(version))
            throw new InvalidDataException("Release manifest contains an invalid Endstone plugin version.");

        var fileName = $"endstone_voicecraft-{version}-py3-none-any.whl";
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Latest GitHub release does not contain assets.");

        JsonElement? selected = null;
        foreach (var asset in assets.EnumerateArray())
        {
            if (string.Equals(OptionalString(asset, "name"), fileName, StringComparison.Ordinal))
            {
                selected = asset;
                break;
            }
        }

        if (selected == null)
            throw new InvalidDataException($"Latest release is missing {fileName}.");

        var selectedAsset = selected.Value;
        var assetUrl = RequiredString(selectedAsset, "url");
        var digest = RequiredString(selectedAsset, "digest");
        var size = RequiredLong(selectedAsset, "size");

        if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) || digest.Length != 71)
            throw new InvalidDataException("GitHub release asset is missing a valid SHA-256 digest.");
        if (size <= 0 || size > MaxPluginBytes)
            throw new InvalidDataException("GitHub release asset has an invalid size.");

        var sha256 = digest[7..].ToLowerInvariant();
        if (!sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("GitHub release asset SHA-256 digest is invalid.");

        Update(activity, ui, 17,
            thai ? $"Release ล่าสุดรองรับแล้ว: Endstone v{version} / Protocol {protocol}" : $"Latest compatible release found: Endstone v{version} / Protocol {protocol}");
        AndroidRuntimeLog.Append("UI", $"Latest compatible Endstone asset selected: tag={tag}, version={version}, protocol={protocol}, size={size}");
        return new PluginRelease(tag, version, fileName, assetUrl, sha256, size);
    }

    private static async Task<string> GetTextAsync(string url, string accept)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept", accept);
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseContentRead);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"GitHub API returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.", null, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<byte[]> DownloadWheelWithRetryAsync(Activity activity, ProgressUi? ui, bool thai, PluginRelease release)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                if (attempt > 1)
                {
                    Update(activity, ui, 20,
                        thai ? $"กำลังลองดาวน์โหลดอีกครั้ง ({attempt}/{MaxAttempts})" : $"Retrying download ({attempt}/{MaxAttempts})");
                }

                return await DownloadWheelOnceAsync(activity, ui, thai, release);
            }
            catch (Exception ex) when (attempt < MaxAttempts && IsRetryable(ex))
            {
                last = ex;
                AndroidRuntimeLog.Append("UI", $"GitHub plugin download attempt {attempt}/{MaxAttempts} failed and will retry: {DescribeException(ex)}");
                await Task.Delay(TimeSpan.FromMilliseconds(700 * attempt));
            }
        }

        throw last ?? new IOException("Plugin download failed after retry attempts.");
    }

    private static async Task<byte[]> DownloadWheelOnceAsync(Activity activity, ProgressUi? ui, bool thai, PluginRelease release)
    {
        var current = new Uri(release.AssetApiUrl);

        for (var redirect = 0; redirect <= MaxRedirects; redirect++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.TryAddWithoutValidation("Accept", "application/octet-stream");
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            if (IsRedirect(response.StatusCode))
            {
                if (redirect == MaxRedirects)
                    throw new HttpRequestException("GitHub asset download exceeded the redirect limit.");

                var location = response.Headers.Location ?? throw new HttpRequestException("GitHub redirect did not include a Location header.");
                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                ValidateRedirectUri(current);
                AndroidRuntimeLog.Append("UI", $"GitHub asset redirect {redirect + 1}: host={current.Host}");
                continue;
            }

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"GitHub asset returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.", null, response.StatusCode);

            var total = response.Content.Headers.ContentLength ?? release.Size;
            if (total <= 0 || total > MaxPluginBytes)
                throw new InvalidDataException("Downloaded plugin Content-Length is invalid.");

            await using var source = await response.Content.ReadAsStreamAsync();
            await using var buffer = new MemoryStream((int)Math.Min(total, MaxPluginBytes));
            var chunk = new byte[16 * 1024];
            long downloaded = 0;

            while (true)
            {
                var read = await source.ReadAsync(chunk.AsMemory(0, chunk.Length));
                if (read <= 0)
                    break;

                downloaded += read;
                if (downloaded > MaxPluginBytes)
                    throw new InvalidDataException("Downloaded plugin exceeded the maximum allowed size.");

                await buffer.WriteAsync(chunk.AsMemory(0, read));
                var percentValue = 20 + (int)Math.Clamp(downloaded * 45L / Math.Max(total, 1L), 0L, 45L);
                var amount = $"{downloaded / 1024.0:0.0} / {total / 1024.0:0.0} KB";
                Update(activity, ui, percentValue,
                    thai ? $"กำลังดาวน์โหลด Endstone Plugin v{release.Version}\n{amount}" : $"Downloading Endstone Plugin v{release.Version}\n{amount}");
            }

            var bytes = buffer.ToArray();
            if (bytes.LongLength != release.Size)
                throw new IOException($"Plugin download size mismatch. Expected {release.Size} bytes, received {bytes.LongLength} bytes.");

            Update(activity, ui, 65, thai ? "ดาวน์โหลด Plugin ครบแล้ว" : "Plugin download complete");
            return bytes;
        }

        throw new HttpRequestException("GitHub asset download failed before receiving content.");
    }

    private static bool IsRetryable(Exception ex)
    {
        if (ex is TaskCanceledException or IOException)
            return true;
        if (ex is HttpRequestException http)
        {
            if (http.StatusCode == null)
                return true;
            var code = (int)http.StatusCode.Value;
            return code == 408 || code == 429 || code >= 500;
        }
        return false;
    }

    private static bool IsRedirect(HttpStatusCode code) => code is
        HttpStatusCode.Moved or
        HttpStatusCode.Redirect or
        HttpStatusCode.RedirectMethod or
        HttpStatusCode.TemporaryRedirect or
        HttpStatusCode.PermanentRedirect;

    private static void ValidateRedirectUri(Uri uri)
    {
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GitHub asset redirect was not HTTPS.");

        var host = uri.Host;
        var trusted = host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase);
        if (!trusted)
            throw new InvalidDataException($"GitHub asset redirected to an unexpected host: {host}");
    }

    private static void VerifySourceWheel(byte[] wheel, string expectedSha256)
    {
        var actual = Convert.ToHexString(SHA256.HashData(wheel)).ToLowerInvariant();
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Downloaded plugin failed SHA-256 verification.");
    }

    private static void ValidateSourceWheelIdentity(byte[] wheel, string expectedVersion)
    {
        using var stream = new MemoryStream(wheel, writable: false);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        var metadata = zip.Entries.FirstOrDefault(entry => entry.FullName.EndsWith(".dist-info/METADATA", StringComparison.Ordinal));
        if (metadata == null)
            throw new InvalidDataException("Plugin wheel is missing METADATA.");

        using var reader = new StreamReader(metadata.Open(), Encoding.UTF8);
        var text = reader.ReadToEnd().Replace("\r\n", "\n");
        var name = ReadMetadataValue(text, "Name");
        var version = ReadMetadataValue(text, "Version");

        if (!string.Equals(name, "endstone-voicecraft", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Downloaded wheel is not the Endstone VoiceCraft plugin.");
        if (!string.Equals(version, expectedVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Plugin wheel version mismatch. Expected {expectedVersion}, found {version}.");
        if (zip.GetEntry(ConfigEntryName) == null)
            throw new InvalidDataException("Plugin wheel is missing endstone_voicecraft/config.toml.");
        if (!zip.Entries.Any(entry => entry.FullName.EndsWith(".dist-info/RECORD", StringComparison.Ordinal)))
            throw new InvalidDataException("Plugin wheel is missing RECORD.");
    }

    private static string ReadMetadataValue(string metadata, string key)
    {
        var prefix = key + ":";
        foreach (var line in metadata.Split('\n'))
        {
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return line[prefix.Length..].Trim();
        }
        return string.Empty;
    }

    private static byte[] PatchWheel(byte[] sourceWheel, string configuredToml)
    {
        var configBytes = Encoding.UTF8.GetBytes(configuredToml.Replace("\r\n", "\n"));
        using var inputStream = new MemoryStream(sourceWheel, writable: false);
        using var input = new ZipArchive(inputStream, ZipArchiveMode.Read, leaveOpen: false);
        using var outputStream = new MemoryStream();

        string? recordName = null;
        string? recordText = null;
        var foundConfig = false;

        using (var output = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in input.Entries)
            {
                if (entry.FullName.EndsWith(".dist-info/RECORD", StringComparison.Ordinal))
                {
                    recordName = entry.FullName;
                    using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
                    recordText = reader.ReadToEnd();
                    continue;
                }

                var target = output.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                if (entry.FullName.Equals(ConfigEntryName, StringComparison.Ordinal))
                {
                    using var targetStream = target.Open();
                    targetStream.Write(configBytes, 0, configBytes.Length);
                    foundConfig = true;
                    continue;
                }

                using var source = entry.Open();
                using var destination = target.Open();
                source.CopyTo(destination);
            }

            if (!foundConfig)
                throw new InvalidDataException("Plugin wheel does not contain endstone_voicecraft/config.toml.");
            if (string.IsNullOrWhiteSpace(recordName) || recordText == null)
                throw new InvalidDataException("Plugin wheel does not contain a valid RECORD file.");

            var updatedRecord = RewriteRecord(recordText, recordName, configBytes);
            var recordEntry = output.CreateEntry(recordName, CompressionLevel.Optimal);
            using var recordStream = recordEntry.Open();
            var recordBytes = Encoding.UTF8.GetBytes(updatedRecord);
            recordStream.Write(recordBytes, 0, recordBytes.Length);
        }

        return outputStream.ToArray();
    }

    private static string RewriteRecord(string original, string recordName, byte[] configBytes)
    {
        var hash = Convert.ToBase64String(SHA256.HashData(configBytes))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var configLine = $"{ConfigEntryName},sha256={hash},{configBytes.Length}";
        var recordLine = $"{recordName},,";

        var lines = original.Replace("\r\n", "\n").Split('\n').ToList();
        var configIndex = lines.FindIndex(line => line.StartsWith(ConfigEntryName + ",", StringComparison.Ordinal));
        if (configIndex >= 0)
            lines[configIndex] = configLine;
        else
            lines.Add(configLine);

        var recordIndex = lines.FindIndex(line => line.StartsWith(recordName + ",", StringComparison.Ordinal));
        if (recordIndex >= 0)
            lines[recordIndex] = recordLine;
        else
            lines.Add(recordLine);

        while (lines.Count > 0 && string.IsNullOrEmpty(lines[^1]))
            lines.RemoveAt(lines.Count - 1);
        return string.Join("\n", lines) + "\n";
    }

    private static void ValidateProvisionedWheel(byte[] wheel, string configuredToml, string expectedVersion)
    {
        using var stream = new MemoryStream(wheel, writable: false);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var config = zip.GetEntry(ConfigEntryName) ?? throw new InvalidDataException("Configured plugin is missing config.toml.");
        using var reader = new StreamReader(config.Open(), Encoding.UTF8);
        var actual = reader.ReadToEnd().Replace("\r\n", "\n");
        var expected = configuredToml.Replace("\r\n", "\n");
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidDataException("Configured plugin verification failed.");

        var record = zip.Entries.FirstOrDefault(entry => entry.FullName.EndsWith(".dist-info/RECORD", StringComparison.Ordinal));
        if (record == null)
            throw new InvalidDataException("Configured plugin is missing RECORD.");

        var metadata = zip.Entries.FirstOrDefault(entry => entry.FullName.EndsWith(".dist-info/METADATA", StringComparison.Ordinal));
        if (metadata == null)
            throw new InvalidDataException("Configured plugin is missing METADATA.");
        using var metadataReader = new StreamReader(metadata.Open(), Encoding.UTF8);
        var metadataText = metadataReader.ReadToEnd();
        if (!string.Equals(ReadMetadataValue(metadataText, "Version"), expectedVersion, StringComparison.Ordinal))
            throw new InvalidDataException("Configured plugin version changed unexpectedly.");
    }

    private static async Task<string> SaveToDownloadsAsync(Activity activity, byte[] wheel, string fileName)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
        {
            try
            {
                return await SaveWithMediaStoreAsync(activity, wheel, fileName);
            }
            catch (Exception ex)
            {
                AndroidRuntimeLog.Append("UI", $"MediaStore plugin save failed; using app Downloads fallback: {DescribeException(ex)}");
            }
        }

        return await SaveToAppDownloadsAsync(activity, wheel, fileName);
    }

    private static async Task<string> SaveWithMediaStoreAsync(Activity activity, byte[] wheel, string fileName)
    {
#pragma warning disable CA1416
        var resolver = activity.ContentResolver ?? throw new IOException("Android ContentResolver is unavailable.");
        var values = new ContentValues();
        var downloadsDirectory = global::Android.OS.Environment.DirectoryDownloads;
        values.Put(MediaStore.IMediaColumns.DisplayName, fileName);
        values.Put(MediaStore.IMediaColumns.MimeType, "application/zip");
        values.Put(MediaStore.IMediaColumns.RelativePath, $"{downloadsDirectory}/VoiceCraft");
        values.Put(MediaStore.IMediaColumns.IsPending, 1);

        var uri = resolver.Insert(MediaStore.Downloads.ExternalContentUri, values)
            ?? throw new IOException("Android could not create a Downloads entry.");
        try
        {
            await using var output = resolver.OpenOutputStream(uri, "w")
                ?? throw new IOException("Android could not open the Downloads file.");
            await output.WriteAsync(wheel);
            await output.FlushAsync();

            values.Clear();
            values.Put(MediaStore.IMediaColumns.IsPending, 0);
            resolver.Update(uri, values, null, null);
            return $"Downloads/VoiceCraft/{fileName}";
        }
        catch
        {
            try { resolver.Delete(uri, null, null); } catch { }
            throw;
        }
#pragma warning restore CA1416
    }

    private static async Task<string> SaveToAppDownloadsAsync(Activity activity, byte[] wheel, string fileName)
    {
        var root = activity.GetExternalFilesDir(global::Android.OS.Environment.DirectoryDownloads)
            ?? activity.FilesDir
            ?? throw new IOException("No writable download directory is available.");
        var directory = new Java.IO.File(root, "VoiceCraft");
        if (!directory.Exists() && !directory.Mkdirs())
            throw new IOException("Android could not create the VoiceCraft download directory.");

        var path = Path.Combine(directory.AbsolutePath, fileName);
        await File.WriteAllBytesAsync(path, wheel);
        return path;
    }

    private static ProgressUi? TryShowProgress(Activity activity, bool thai)
    {
        try
        {
            var density = activity.Resources?.DisplayMetrics?.Density ?? 1f;
            int Dp(int value) => (int)(value * density + 0.5f);

            var panel = new LinearLayout(activity) { Orientation = Orientation.Vertical };
            panel.SetPadding(Dp(22), Dp(12), Dp(22), Dp(10));

            var percent = new TextView(activity)
            {
                Text = "0%",
                TextSize = 26,
                Gravity = GravityFlags.CenterHorizontal
            };
            percent.SetTypeface(global::Android.Graphics.Typeface.Default, global::Android.Graphics.TypefaceStyle.Bold);
            panel.AddView(percent);

            var progress = new ProgressBar(activity, null, global::Android.Resource.Attribute.ProgressBarStyleHorizontal)
            {
                Max = 100,
                Progress = 0,
                Indeterminate = false
            };
            panel.AddView(progress, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(28)) { TopMargin = Dp(8) });

            var status = new TextView(activity)
            {
                Text = thai ? "กำลังตรวจสอบ GitHub Release..." : "Checking GitHub Release...",
                TextSize = 13
            };
            status.SetPadding(0, Dp(10), 0, 0);
            panel.AddView(status);

            var dialog = new AlertDialog.Builder(activity)
                .SetTitle(thai ? "ดาวน์โหลด Endstone Plugin" : "Download Endstone Plugin")
                .SetView(panel)
                .SetCancelable(false)
                .Create();
            dialog.Show();
            dialog.SetCanceledOnTouchOutside(false);
            return new ProgressUi { Dialog = dialog, Progress = progress, Percent = percent, Status = status };
        }
        catch (Exception ex)
        {
            AndroidRuntimeLog.Append("UI", $"Compatible plugin progress UI unavailable; using fallback: {DescribeException(ex)}");
        }

        try
        {
#pragma warning disable CS0618
            var progressDialog = new ProgressDialog(activity);
            progressDialog.SetTitle(thai ? "ดาวน์โหลด Endstone Plugin" : "Download Endstone Plugin");
            progressDialog.SetProgressStyle(ProgressDialogStyle.Horizontal);
            progressDialog.Max = 100;
            progressDialog.Progress = 0;
            progressDialog.Indeterminate = false;
            progressDialog.SetCancelable(false);
            progressDialog.SetMessage(thai ? "กำลังตรวจสอบ GitHub Release... 0%" : "Checking GitHub Release... 0%");
            progressDialog.Show();
#pragma warning restore CS0618
            return new ProgressUi { Dialog = progressDialog, NativeProgress = progressDialog };
        }
        catch
        {
            return null;
        }
    }

    private static void Update(Activity activity, ProgressUi? ui, int value, string message)
    {
        if (ui == null)
            return;

        var safeValue = Math.Clamp(value, 0, 100);
        activity.RunOnUiThread(() =>
        {
            try
            {
                if (ui.Progress != null)
                    ui.Progress.Progress = safeValue;
                if (ui.Percent != null)
                    ui.Percent.Text = $"{safeValue}%";
                if (ui.Status != null)
                    ui.Status.Text = message;
                if (ui.NativeProgress != null)
                {
                    ui.NativeProgress.Progress = safeValue;
                    ui.NativeProgress.SetMessage($"{message}\n{safeValue}%");
                }
            }
            catch { }
        });
    }

    private static void DismissSafely(ProgressUi? ui)
    {
        try { ui?.Dialog?.Dismiss(); } catch { }
    }

    private static string RequiredString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"GitHub metadata is missing '{name}'.");
        return value.GetString()?.Trim() ?? throw new InvalidDataException($"GitHub metadata '{name}' is empty.");
    }

    private static string? OptionalString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int RequiredInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result))
            throw new InvalidDataException($"Release manifest is missing integer '{name}'.");
        return result;
    }

    private static long RequiredLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || !value.TryGetInt64(out var result))
            throw new InvalidDataException($"GitHub metadata is missing integer '{name}'.");
        return result;
    }

    private static bool IsSafeVersion(string version)
    {
        if (version.Length is < 1 or > 32)
            return false;
        return version.All(ch => char.IsDigit(ch) || ch == '.');
    }

    private static string SafeMessage(Exception ex, bool thai)
    {
        if (ex is TaskCanceledException)
            return thai ? "การเชื่อมต่อ GitHub หมดเวลา กรุณาลองใหม่" : "The GitHub connection timed out. Please try again.";
        if (ex is HttpRequestException http)
        {
            var code = http.StatusCode.HasValue ? $" HTTP {(int)http.StatusCode.Value}" : string.Empty;
            return thai ? $"เชื่อมต่อ GitHub ไม่สำเร็จ{code}" : $"GitHub request failed{code}.";
        }
        if (ex is InvalidDataException invalidData)
            return invalidData.Message;
        if (ex is IOException io)
            return io.Message;

        var message = ex.Message?.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return string.IsNullOrWhiteSpace(message) ? ex.GetType().Name : $"{ex.GetType().Name}: {message}";
    }

    internal static string DescribeException(Exception ex)
    {
        var message = ex.Message?.Replace('\r', ' ').Replace('\n', ' ').Trim() ?? string.Empty;
        if (message.Length > 500)
            message = message[..500];
        var type = ex.GetType().FullName ?? ex.GetType().Name;
        var inner = ex.InnerException;
        if (inner != null)
        {
            var innerMessage = inner.Message?.Replace('\r', ' ').Replace('\n', ' ').Trim() ?? string.Empty;
            if (innerMessage.Length > 300)
                innerMessage = innerMessage[..300];
            return string.IsNullOrWhiteSpace(innerMessage)
                ? $"{type}: {message}; inner={inner.GetType().FullName}"
                : $"{type}: {message}; inner={inner.GetType().FullName}: {innerMessage}";
        }
        return string.IsNullOrWhiteSpace(message) ? type : $"{type}: {message}";
    }
}
