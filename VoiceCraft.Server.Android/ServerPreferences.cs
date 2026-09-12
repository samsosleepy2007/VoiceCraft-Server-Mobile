using System.Text.Json;
using Android.Content;

namespace VoiceCraft.Server.Android;

internal static class ServerPreferences
{
    private const string PreferenceName = "voicecraft_server";
    internal const string ExtraVoicePort = "voice_port";
    internal const string ExtraServerKey = "server_key";
    internal const string ExtraBridgeEnabled = "bridge_enabled";
    internal const string ExtraBridgeUrl = "bridge_url";
    internal const string ExtraBridgeBackupUrls = "bridge_backup_urls_json";
    internal const string ExtraBridgeServerId = "bridge_server_id";
    internal const string ExtraBridgeSecret = "bridge_secret";
    internal const string ExtraLanguage = "ui_language";
    internal const string ExtraDarkTheme = "ui_dark_theme";
    internal const string ActionStop = "chat.voicecraft.server.STOP";

    internal static ISharedPreferences Get(Context context) =>
        context.GetSharedPreferences(PreferenceName, FileCreationMode.Private)!;

    internal static int GetVoicePort(Context context) =>
        Get(context).GetInt(ExtraVoicePort, 9050);

    internal static string GetServerKey(Context context)
    {
        var prefs = Get(context);
        var key = prefs.GetString(ExtraServerKey, null);
        if (!string.IsNullOrWhiteSpace(key))
            return key;

        key = Guid.NewGuid().ToString("N");
        prefs.Edit()!.PutString(ExtraServerKey, key)!.Apply();
        return key;
    }

    internal static bool GetBridgeEnabled(Context context) =>
        Get(context).GetBoolean(ExtraBridgeEnabled, false);

    internal static string GetBridgeUrl(Context context) =>
        Get(context).GetString(ExtraBridgeUrl, "") ?? string.Empty;

    internal static IReadOnlyList<string> GetBridgeBackupUrls(Context context)
    {
        var json = Get(context).GetString(ExtraBridgeBackupUrls, "[]") ?? "[]";
        try
        {
            var values = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            var cleaned = new List<string>();
            foreach (var raw in values)
            {
                var value = (raw ?? string.Empty).Trim();
                if (value.Length == 0 || cleaned.Contains(value, StringComparer.OrdinalIgnoreCase))
                    continue;
                cleaned.Add(value);
            }
            return cleaned;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    internal static IReadOnlyList<string> GetBridgeRelayUrls(Context context)
    {
        var primary = GetBridgeUrl(context).Trim();
        var result = new List<string>();
        if (primary.Length > 0)
            result.Add(primary);
        foreach (var backup in GetBridgeBackupUrls(context))
        {
            if (!result.Contains(backup, StringComparer.OrdinalIgnoreCase))
                result.Add(backup);
        }
        return result;
    }

    internal static string GetBridgeServerId(Context context) =>
        Get(context).GetString(ExtraBridgeServerId, "") ?? string.Empty;

    internal static string GetBridgeSecret(Context context) =>
        Get(context).GetString(ExtraBridgeSecret, "") ?? string.Empty;

    internal static string GetLanguage(Context context)
    {
        var value = Get(context).GetString(ExtraLanguage, "th") ?? "th";
        return value.Equals("en", StringComparison.OrdinalIgnoreCase) ? "en" : "th";
    }

    internal static bool GetDarkTheme(Context context) =>
        Get(context).GetBoolean(ExtraDarkTheme, false);

    internal static void Save(Context context, int voicePort, string serverKey)
    {
        Get(context).Edit()!
            .PutInt(ExtraVoicePort, voicePort)!
            .PutString(ExtraServerKey, serverKey)!
            .Apply();
    }

    internal static void SaveBridge(Context context, bool enabled, string url, string serverId, string secret)
    {
        Get(context).Edit()!
            .PutBoolean(ExtraBridgeEnabled, enabled)!
            .PutString(ExtraBridgeUrl, url.Trim())!
            .PutString(ExtraBridgeServerId, serverId.Trim())!
            .PutString(ExtraBridgeSecret, secret.Trim())!
            .Apply();
    }

    internal static void SaveBridgeBackups(Context context, IEnumerable<string> backupUrls)
    {
        var cleaned = new List<string>();
        foreach (var raw in backupUrls)
        {
            var value = (raw ?? string.Empty).Trim();
            if (value.Length == 0 || cleaned.Contains(value, StringComparer.OrdinalIgnoreCase))
                continue;
            cleaned.Add(value);
        }
        Get(context).Edit()!
            .PutString(ExtraBridgeBackupUrls, JsonSerializer.Serialize(cleaned))!
            .Apply();
    }

    internal static void SaveUi(Context context, string language, bool darkTheme)
    {
        Get(context).Edit()!
            .PutString(ExtraLanguage, language.Equals("en", StringComparison.OrdinalIgnoreCase) ? "en" : "th")!
            .PutBoolean(ExtraDarkTheme, darkTheme)!
            .Apply();
    }
}