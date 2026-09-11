using System.Security.Cryptography;
using Android.Content;

namespace VoiceCraft.Server.Android;

internal sealed class GuestProfile
{
    public int Version { get; set; } = 1;
    public string GuestId { get; set; } = string.Empty;
    public string InstallationId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string? GuestToken { get; set; }
    public DateTimeOffset? GuestTokenExpiresAt { get; set; }
}

internal static class GuestAccountStore
{
    private static readonly KeystoreJsonStore Store = new(
        "voicecraft_guest_profile_key_v1",
        "voicecraft_guest_profile.enc");

    public static bool TryLoad(Context context, out GuestProfile? profile)
    {
        if (!Store.TryLoad(context, out profile) || profile == null)
            return false;

        return profile.Version == 1
            && profile.GuestId.StartsWith("GUEST-", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(profile.InstallationId);
    }

    public static GuestProfile GetOrCreate(Context context)
    {
        if (TryLoad(context, out var existing) && existing != null)
            return existing;

        var profile = new GuestProfile
        {
            Version = 1,
            GuestId = "GUEST-" +
                Convert.ToHexString(RandomNumberGenerator.GetBytes(12)),
            InstallationId = LocalInstallationStore.GetOrCreate(context),
            CreatedAt = DateTimeOffset.UtcNow
        };

        Store.Save(context, profile);
        return profile;
    }

    public static void Save(Context context, GuestProfile profile) =>
        Store.Save(context, profile);

    public static void Delete(Context context) =>
        Store.Delete(context);
}
