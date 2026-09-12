using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Widget;

namespace VoiceCraft.Server.Android;

internal static class ConfiguredPluginDownloadInjector
{
    private const string Marker = "voicecraft-configured-plugin-download";

    private static readonly string[] OldButtonTexts =
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

            var oldButton = FindOldButton(root);
            if (oldButton?.Parent is not ViewGroup parent)
                return;

            var index = parent.IndexOfChild(oldButton);
            if (index < 0)
                return;

            var thai = ServerPreferences.GetLanguage(activity) == "th";
            var replacement = new Button(activity)
            {
                Text = thai ? "ดาวน์โหลด Plugin" : "DOWNLOAD PLUGIN",
                TextSize = 11,
                ContentDescription = Marker,
                Gravity = oldButton.Gravity,
                Enabled = oldButton.Enabled,
                Alpha = oldButton.Alpha,
                Elevation = oldButton.Elevation
            };
            replacement.SetAllCaps(false);
            replacement.SetTextColor(oldButton.TextColors);
            replacement.SetTypeface(oldButton.Typeface, oldButton.Typeface?.Style ?? global::Android.Graphics.TypefaceStyle.Normal);
            replacement.SetPadding(oldButton.PaddingLeft, oldButton.PaddingTop, oldButton.PaddingRight, oldButton.PaddingBottom);
            replacement.Background = oldButton.Background;

            var layout = oldButton.LayoutParameters;
            parent.RemoveViewAt(index);
            if (layout != null)
                parent.AddView(replacement, index, layout);
            else
                parent.AddView(replacement, index);

            replacement.Click += (_, _) => BeginDownload(activity);
        }
        catch
        {
            // Optional UI enhancement; never interrupt the server controls.
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

            AndroidRuntimeLog.Append("UI", "Configured Endstone plugin download requested");
            _ = ConfiguredPluginDownloader.DownloadAsync(activity, config);
        }
        catch (Exception ex)
        {
            AndroidRuntimeLog.Append("UI", $"Configured plugin setup failed before download: {ex.GetType().Name}");
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
                ? "กรอก Render Relay, Server ID และ Bridge Secret ให้ครบก่อน จากนั้นกดดาวน์โหลดอีกครั้ง ไฟล์ Plugin จะใส่ค่าทั้งหมดให้โดยอัตโนมัติ"
                : "Complete Render Relay, Server ID and Bridge Secret first, then download again. The plugin will be provisioned automatically.")
            .SetPositiveButton(thai ? "ตกลง" : "OK", (_, _) => { })
            .Show();
    }

    private static Button? FindOldButton(View view)
    {
        if (view is Button button)
        {
            var text = button.Text?.ToString()?.Trim() ?? string.Empty;
            if (OldButtonTexts.Any(candidate => string.Equals(text, candidate, StringComparison.OrdinalIgnoreCase)))
                return button;
        }

        if (view is not ViewGroup group)
            return null;

        for (var i = 0; i < group.ChildCount; i++)
        {
            var child = group.GetChildAt(i);
            if (child == null)
                continue;
            var found = FindOldButton(child);
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

internal static class ConfiguredPluginDownloader
{
    private const string WheelFileName = "endstone_voicecraft-0.2.6-py3-none-any.whl";
    private const string ConfigEntryName = "endstone_voicecraft/config.toml";
    private const string SourceWheelSha256 = "b6725cc94609d27b3d2815f66e1373fe8c6917d7be68562456ac7727254e8ae4";
    private const string SourceWheelUrl =
        "https://github.com/samsosleepy2007/VoiceCraft-Server-Mobile-Unofficial/releases/download/" +
        "v1.7.1-android-phase2-ui4.5.2-account-v2-guest-endstone0.2.6-relay0.2.1/" +
        WheelFileName;

    private static readonly HttpClient Client = CreateClient();

    public static async Task DownloadAsync(Activity activity, string configuredToml)
    {
        var thai = ServerPreferences.GetLanguage(activity) == "th";
        AlertDialog? dialog = null;
        ProgressBar? progress = null;
        TextView? percent = null;
        TextView? status = null;

        try
        {
            (dialog, progress, percent, status) = ShowProgress(activity, thai);
            Update(activity, progress, percent, status, 3,
                thai ? "กำลังตรวจสอบการตั้งค่า Render Relay และ Secret" : "Checking Render Relay and secret");
            await Task.Delay(120);

            Update(activity, progress, percent, status, 8,
                thai ? "กำลังเตรียม Plugin Config สำหรับเซิร์ฟเวอร์นี้" : "Preparing the plugin configuration for this server");
            await Task.Delay(120);

            Update(activity, progress, percent, status, 12,
                thai ? "กำลังเชื่อมต่อ GitHub เพื่อดาวน์โหลด Endstone Plugin" : "Connecting to GitHub to download the Endstone plugin");

            var wheel = await DownloadWheelAsync(activity, progress, percent, status, thai);

            Update(activity, progress, percent, status, 70,
                thai ? "กำลังตรวจสอบ SHA-256 ของ Plugin ต้นฉบับ" : "Verifying the original plugin SHA-256");
            VerifySourceWheel(wheel);
            await Task.Delay(120);

            Update(activity, progress, percent, status, 78,
                thai
                    ? "กำลังใส่ Render Relay, Server ID, Backup Relay และ Bridge Secret ลงใน Plugin"
                    : "Injecting Render Relay, Server ID, backup relays and Bridge Secret into the plugin");
            var provisionedWheel = PatchWheel(wheel, configuredToml);
            await Task.Delay(140);

            Update(activity, progress, percent, status, 90,
                thai ? "กำลังอัปเดต Wheel RECORD และตรวจสอบแพ็กเกจ" : "Updating Wheel RECORD and validating the package");
            ValidateProvisionedWheel(provisionedWheel, configuredToml);
            await Task.Delay(120);

            Update(activity, progress, percent, status, 95,
                thai ? "กำลังบันทึก Plugin ลงโฟลเดอร์ Downloads" : "Saving the plugin to Downloads");
            var location = await SaveToDownloadsAsync(activity, provisionedWheel);

            Update(activity, progress, percent, status, 100,
                thai ? "เสร็จแล้ว — Plugin พร้อมนำเข้า Endstone Server" : "Done — plugin is ready for your Endstone server");
            AndroidRuntimeLog.Append("UI", "Configured Endstone plugin created successfully");
            await Task.Delay(650);

            activity.RunOnUiThread(() =>
            {
                dialog?.Dismiss();
                new AlertDialog.Builder(activity)
                    .SetTitle(thai ? "ดาวน์โหลด Plugin สำเร็จ" : "Plugin download complete")
                    .SetMessage(thai
                        ? $"บันทึกแล้วที่:\n{location}\n\nไฟล์นี้มี Render Relay, Server ID, Backup Relay และ Bridge Secret ของเซิร์ฟเวอร์นี้อยู่ภายในแล้ว สามารถนำไฟล์ .whl ไปใส่ใน Endstone Server ได้เลย\n\nไฟล์มี Bridge Secret อยู่ภายใน กรุณาอย่าแชร์กับบุคคลที่ไม่ไว้ใจ"
                        : $"Saved to:\n{location}\n\nThis .whl already contains this server's Render Relay, Server ID, backup relays and Bridge Secret and can be placed directly in the Endstone server.\n\nThe file contains your Bridge Secret. Do not share it with untrusted people.")
                    .SetPositiveButton(thai ? "ตกลง" : "OK", (_, _) => { })
                    .Show();
            });
        }
        catch (Exception ex)
        {
            AndroidRuntimeLog.Append("UI", $"Configured Endstone plugin download failed: {ex.GetType().Name}");
            activity.RunOnUiThread(() =>
            {
                dialog?.Dismiss();
                new AlertDialog.Builder(activity)
                    .SetTitle(thai ? "ดาวน์โหลด Plugin ไม่สำเร็จ" : "Plugin download failed")
                    .SetMessage(thai
                        ? $"เกิดข้อผิดพลาดระหว่างสร้าง Plugin ที่ตั้งค่าไว้แล้ว\n\n{SafeMessage(ex)}\n\nตรวจสอบอินเทอร์เน็ตและลองอีกครั้ง"
                        : $"An error occurred while creating the preconfigured plugin.\n\n{SafeMessage(ex)}\n\nCheck your internet connection and try again.")
                    .SetPositiveButton(thai ? "ปิด" : "CLOSE", (_, _) => { })
                    .Show();
            });
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("VoiceCraft-Server-Mobile/1.7.1");
        return client;
    }

    private static (AlertDialog Dialog, ProgressBar Progress, TextView Percent, TextView Status) ShowProgress(Activity activity, bool thai)
    {
        var density = activity.Resources?.DisplayMetrics?.Density ?? 1f;
        int Dp(int value) => (int)(value * density + 0.5f);

        var panel = new LinearLayout(activity)
        {
            Orientation = Orientation.Vertical
        };
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
        panel.AddView(progress, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(28))
        {
            TopMargin = Dp(8)
        });

        var status = new TextView(activity)
        {
            Text = thai ? "กำลังเตรียมการดาวน์โหลด..." : "Preparing download...",
            TextSize = 13
        };
        status.SetPadding(0, Dp(10), 0, 0);
        panel.AddView(status);

        var dialog = new AlertDialog.Builder(activity)
            .SetTitle(thai ? "ดาวน์โหลด Endstone Plugin" : "Download Endstone Plugin")
            .SetView(panel)
            .SetCancelable(false)
            .Create();
        dialog.SetCanceledOnTouchOutside(false);
        dialog.Show();
        return (dialog, progress, percent, status);
    }

    private static void Update(Activity activity, ProgressBar? progress, TextView? percent, TextView? status, int value, string message)
    {
        activity.RunOnUiThread(() =>
        {
            if (progress != null)
                progress.Progress = Math.Clamp(value, 0, 100);
            if (percent != null)
                percent.Text = $"{Math.Clamp(value, 0, 100)}%";
            if (status != null)
                status.Text = message;
        });
    }

    private static async Task<byte[]> DownloadWheelAsync(
        Activity activity,
        ProgressBar? progress,
        TextView? percent,
        TextView? status,
        bool thai)
    {
        using var response = await Client.GetAsync(SourceWheelUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync();
        await using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        long downloaded = 0;

        while (true)
        {
            var read = await source.ReadAsync(chunk);
            if (read <= 0)
                break;

            await buffer.WriteAsync(chunk.AsMemory(0, read));
            downloaded += read;

            var percentValue = total is > 0
                ? 12 + (int)Math.Clamp(downloaded * 53L / total.Value, 0L, 53L)
                : 35;
            var amount = total is > 0
                ? $"{downloaded / 1024.0:0.0} / {total.Value / 1024.0:0.0} KB"
                : $"{downloaded / 1024.0:0.0} KB";
            Update(activity, progress, percent, status, percentValue,
                thai ? $"กำลังดาวน์โหลด Endstone Plugin จาก GitHub\n{amount}" : $"Downloading Endstone Plugin from GitHub\n{amount}");
        }

        Update(activity, progress, percent, status, 65,
            thai ? "ดาวน์โหลด Plugin ครบแล้ว" : "Plugin download complete");
        return buffer.ToArray();
    }

    private static void VerifySourceWheel(byte[] wheel)
    {
        var actual = Convert.ToHexString(SHA256.HashData(wheel)).ToLowerInvariant();
        if (!actual.Equals(SourceWheelSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Downloaded plugin failed SHA-256 verification.");
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
                    using var stream = target.Open();
                    stream.Write(configBytes, 0, configBytes.Length);
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

    private static void ValidateProvisionedWheel(byte[] wheel, string configuredToml)
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
    }

    private static async Task<string> SaveToDownloadsAsync(Activity activity, byte[] wheel)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
        {
#pragma warning disable CA1416
            var resolver = activity.ContentResolver;
            var values = new ContentValues();
            var downloadsDirectory = global::Android.OS.Environment.DirectoryDownloads;
            values.Put(MediaStore.IMediaColumns.DisplayName, WheelFileName);
            values.Put(MediaStore.IMediaColumns.MimeType, "application/octet-stream");
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
                return $"Downloads/VoiceCraft/{WheelFileName}";
            }
            catch
            {
                resolver.Delete(uri, null, null);
                throw;
            }
#pragma warning restore CA1416
        }

        var directory = activity.GetExternalFilesDir(global::Android.OS.Environment.DirectoryDownloads)
            ?? activity.FilesDir
            ?? throw new IOException("No writable download directory is available.");
        if (!directory.Exists())
            directory.Mkdirs();
        var path = Path.Combine(directory.AbsolutePath, WheelFileName);
        await File.WriteAllBytesAsync(path, wheel);
        return path;
    }

    private static string SafeMessage(Exception ex)
    {
        return ex switch
        {
            HttpRequestException => "Network request failed.",
            TaskCanceledException => "Download timed out.",
            InvalidDataException invalidData => invalidData.Message,
            IOException io => io.Message,
            _ => "Unexpected plugin download error."
        };
    }
}
