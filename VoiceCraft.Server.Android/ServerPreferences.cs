using Android.Content;

namespace VoiceCraft.Server.Android;

internal static class ServerPreferences
{
    private const string PreferenceName = "voicecraft_server";
    internal const string ExtraVoicePort = "voice_port";
    internal const string ExtraServerKey = "server_key";
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

    internal static void Save(Context context, int voicePort, string serverKey)
    {
        Get(context).Edit()!
            .PutInt(ExtraVoicePort, voicePort)!
            .PutString(ExtraServerKey, serverKey)!
            .Apply();
    }
}
