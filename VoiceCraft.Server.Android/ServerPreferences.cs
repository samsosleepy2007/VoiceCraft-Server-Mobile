using Android.Content;

namespace VoiceCraft.Server.Android;

internal static class ServerPreferences
{
    private const string PreferenceName = "voicecraft_server";
    internal const string ExtraVoicePort = "voice_port";
    internal const string ExtraServerKey = "server_key";
    internal const string ExtraBridgeEnabled = "bridge_enabled";
    internal const string ExtraBridgeUrl = "bridge_url";
    internal const string ExtraBridgeServerId = "bridge_server_id";
    internal const string ExtraBridgeSecret = "bridge_secret";
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
        Get(context).GetString(ExtraBridgeUrl, "wss://YOUR-RELAY.onrender.com/bridge") ?? string.Empty;

    internal static string GetBridgeServerId(Context context) =>
        Get(context).GetString(ExtraBridgeServerId, "mcsv-main") ?? "mcsv-main";

    internal static string GetBridgeSecret(Context context)
    {
        var prefs = Get(context);
        var key = prefs.GetString(ExtraBridgeSecret, null);
        if (!string.IsNullOrWhiteSpace(key))
            return key;

        key = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        prefs.Edit()!.PutString(ExtraBridgeSecret, key)!.Apply();
        return key;
    }

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
}
