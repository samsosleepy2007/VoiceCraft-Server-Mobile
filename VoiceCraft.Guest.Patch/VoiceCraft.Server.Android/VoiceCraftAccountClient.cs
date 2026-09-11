using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace VoiceCraft.Server.Android;

internal static class VoiceCraftAccountClient
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public static async Task<RegisteredSession> LoginAsync(
        global::Android.Content.Context context,
        string loginName,
        string password,
        CancellationToken cancellationToken = default)
    {
        using var response = await Http.PostAsJsonAsync(
            AccountApiConfig.AccountApiBase + "/v1/login",
            new
            {
                loginName = loginName.Trim(),
                password,
                installationId =
                    LocalInstallationStore.GetOrCreate(context),
                deviceName =
                    global::Android.OS.Build.Manufacturer + " " +
                    global::Android.OS.Build.Model,
                deviceModel = global::Android.OS.Build.Model,
                platform = "android",
                appVersion = AccountApiConfig.AppVersion
            },
            cancellationToken);

        var text = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            try
            {
                using var errorDoc = JsonDocument.Parse(text);
                var message =
                    errorDoc.RootElement.TryGetProperty(
                        "error",
                        out var e)
                    ? e.GetString()
                    : null;

                var code =
                    errorDoc.RootElement.TryGetProperty(
                        "code",
                        out var c)
                    ? c.GetString()
                    : null;

                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(message)
                        ? $"Login failed ({(int)response.StatusCode})."
                        : $"{message} [{code}]");
            }
            catch (JsonException)
            {
                throw new InvalidOperationException(
                    $"Login failed ({(int)response.StatusCode}).");
            }
        }

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        var account = root.GetProperty("account");

        var token = root.GetProperty("token").GetString()
            ?? throw new InvalidOperationException(
                "Server did not return a session token.");

        if (!DateTimeOffset.TryParse(
                root.GetProperty("expiresAt").GetString(),
                out var expiresAt))
            throw new InvalidOperationException(
                "Server returned an invalid session expiry.");

        var session = new RegisteredSession
        {
            Version = 1,
            Token = token,
            AccountId =
                account.GetProperty("id").GetString()
                ?? string.Empty,
            LoginName =
                account.GetProperty("loginName").GetString()
                ?? loginName.Trim(),
            ExpiresAt = expiresAt
        };

        if (!session.IsUsable)
            throw new InvalidOperationException(
                "Server returned an unusable session.");

        RegisteredSessionStore.Save(context, session);
        return session;
    }

    public static async Task LogoutAsync(
        RegisteredSession session,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                AccountApiConfig.AccountApiBase + "/v1/logout");

            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    session.Token);

            using var response =
                await Http.SendAsync(request, cancellationToken);
        }
        catch
        {
            // Local logout must work even if the network is offline.
        }
    }
}
