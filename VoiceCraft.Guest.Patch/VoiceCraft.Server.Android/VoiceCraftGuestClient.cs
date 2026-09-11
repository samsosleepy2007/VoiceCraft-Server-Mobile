using System.Net.Http.Json;
using System.Text.Json;

namespace VoiceCraft.Server.Android;

internal static class VoiceCraftGuestClient
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    public static async Task<bool> RefreshSessionAsync(
        global::Android.Content.Context context,
        GuestProfile profile,
        CancellationToken cancellationToken = default)
    {
        using var response = await Http.PostAsJsonAsync(
            AccountApiConfig.GuestApiBase + "/v1/session",
            new
            {
                guestId = profile.GuestId,
                installationId = profile.InstallationId,
                appVersion = AccountApiConfig.AppVersion
            },
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
            return false;

        using var doc = JsonDocument.Parse(body);
        var json = doc.RootElement;

        if (!json.TryGetProperty("token", out var tokenNode)
            || !json.TryGetProperty("expiresAt", out var expiresNode))
            return false;

        var token = tokenNode.GetString();
        var expiresText = expiresNode.GetString();

        if (string.IsNullOrWhiteSpace(token)
            || !DateTimeOffset.TryParse(expiresText, out var expiresAt))
            return false;

        profile.GuestToken = token;
        profile.GuestTokenExpiresAt = expiresAt;
        GuestAccountStore.Save(context, profile);
        return true;
    }

    public static bool NeedsRefresh(GuestProfile profile) =>
        string.IsNullOrWhiteSpace(profile.GuestToken)
        || profile.GuestTokenExpiresAt is null
        || profile.GuestTokenExpiresAt
            <= DateTimeOffset.UtcNow.AddHours(2);
}
