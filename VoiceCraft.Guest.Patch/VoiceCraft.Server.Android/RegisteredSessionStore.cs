using Android.Content;

namespace VoiceCraft.Server.Android;

internal sealed class RegisteredSession
{
    public int Version { get; set; } = 1;
    public string Token { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string LoginName { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }

    public bool IsUsable =>
        Version == 1
        && !string.IsNullOrWhiteSpace(Token)
        && !string.IsNullOrWhiteSpace(AccountId)
        && ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1);
}

internal static class RegisteredSessionStore
{
    private static readonly KeystoreJsonStore Store = new(
        "voicecraft_registered_session_key_v1",
        "voicecraft_registered_session.enc");

    public static bool TryLoad(
        Context context,
        out RegisteredSession? session)
    {
        if (!Store.TryLoad(context, out session) || session == null)
            return false;

        if (session.IsUsable)
            return true;

        Delete(context);
        session = null;
        return false;
    }

    public static void Save(
        Context context,
        RegisteredSession session) =>
        Store.Save(context, session);

    public static void Delete(Context context) =>
        Store.Delete(context);
}
