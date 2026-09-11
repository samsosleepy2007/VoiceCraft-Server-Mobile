using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoiceCraft.Server.Android;

internal sealed class SupabaseAuthClient
{
    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(SupabaseConfig.ProjectUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(20)
        };
        client.DefaultRequestHeaders.Add("apikey", SupabaseConfig.PublishableKey);
        return client;
    }

    internal async Task<AuthResult> SignInAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        return await SendSessionRequestAsync(
            "auth/v1/token?grant_type=password",
            new { email = email.Trim(), password },
            cancellationToken);
    }

    internal async Task<AuthResult> SignUpAsync(string email, string password, string username, CancellationToken cancellationToken = default)
    {
        using var response = await Http.PostAsJsonAsync(
            "auth/v1/signup",
            new
            {
                email = email.Trim(),
                password,
                data = new { username = string.IsNullOrWhiteSpace(username) ? null : username.Trim() }
            },
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            return AuthResult.Fail(ReadError(body, response.ReasonPhrase));

        var payload = JsonSerializer.Deserialize<AuthPayload>(body, JsonOptions);
        if (payload?.AccessToken is { Length: > 0 } && payload.RefreshToken is { Length: > 0 })
            return AuthResult.Success(AuthSession.FromPayload(payload));

        return AuthResult.PendingVerification(
            "Account created. Check your email and verify the account before signing in.");
    }

    internal async Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return AuthResult.Fail("No refresh token is available.");

        return await SendSessionRequestAsync(
            "auth/v1/token?grant_type=refresh_token",
            new { refresh_token = refreshToken },
            cancellationToken);
    }

    internal async Task<AuthResult> GetUserAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return AuthResult.Fail("No access token is available.");

        using var request = new HttpRequestMessage(HttpMethod.Get, "auth/v1/user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await Http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            return AuthResult.Fail(ReadError(body, response.ReasonPhrase));

        var user = JsonSerializer.Deserialize<AuthUser>(body, JsonOptions);
        return user is null
            ? AuthResult.Fail("Supabase returned an invalid user response.")
            : AuthResult.Success(new AuthSession { AccessToken = accessToken, User = user });
    }

    internal async Task<AuthResult> SendPasswordResetAsync(string email, CancellationToken cancellationToken = default)
    {
        using var response = await Http.PostAsJsonAsync(
            "auth/v1/recover",
            new { email = email.Trim() },
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return response.IsSuccessStatusCode
            ? AuthResult.Message("Password reset email sent. Check your inbox.")
            : AuthResult.Fail(ReadError(body, response.ReasonPhrase));
    }

    internal async Task SignOutAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) return;
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/v1/logout");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new { scope = "local" });
        try
        {
            using var _ = await Http.SendAsync(request, cancellationToken);
        }
        catch
        {
            // Local session is still cleared by the caller even if the device is offline.
        }
    }

    internal async Task<bool> UpsertDeviceAsync(AuthSession session, string installationId, string deviceName, string model, string appVersion, CancellationToken cancellationToken = default)
    {
        if (session.User?.Id is not { Length: > 0 } userId || string.IsNullOrWhiteSpace(session.AccessToken))
            return false;

        var payload = new[]
        {
            new
            {
                user_id = userId,
                installation_id = installationId,
                device_name = deviceName,
                device_model = model,
                app_version = appVersion,
                platform = "android",
                revoked = false,
                last_seen_at = DateTimeOffset.UtcNow
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "rest/v1/devices?on_conflict=user_id,installation_id");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        request.Headers.TryAddWithoutValidation("Prefer", "resolution=merge-duplicates,return=minimal");
        request.Content = JsonContent.Create(payload);
        using var response = await Http.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private static async Task<AuthResult> SendSessionRequestAsync(string path, object payload, CancellationToken cancellationToken)
    {
        using var response = await Http.PostAsJsonAsync(path, payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            return AuthResult.Fail(ReadError(body, response.ReasonPhrase));

        var parsed = JsonSerializer.Deserialize<AuthPayload>(body, JsonOptions);
        if (parsed?.AccessToken is not { Length: > 0 } || parsed.RefreshToken is not { Length: > 0 })
            return AuthResult.Fail("Supabase did not return a complete session.");
        return AuthResult.Success(AuthSession.FromPayload(parsed));
    }

    private static string ReadError(string body, string? fallback)
    {
        try
        {
            var error = JsonSerializer.Deserialize<AuthError>(body, JsonOptions);
            return error?.Message ?? error?.ErrorDescription ?? error?.Error ?? fallback ?? "Authentication request failed.";
        }
        catch
        {
            return fallback ?? "Authentication request failed.";
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal sealed class AuthPayload
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("user")]
        public AuthUser? User { get; set; }
    }

    private sealed class AuthError
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    internal sealed class AuthUser
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;
    }

    internal sealed class AuthSession
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public long ExpiresAtUnix { get; set; }
        public AuthUser? User { get; set; }

        internal static AuthSession FromPayload(AuthPayload payload)
        {
            return new AuthSession
            {
                AccessToken = payload.AccessToken ?? string.Empty,
                RefreshToken = payload.RefreshToken ?? string.Empty,
                ExpiresAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + Math.Max(60, payload.ExpiresIn),
                User = payload.User
            };
        }
    }

    internal sealed class AuthResult
    {
        public bool Ok { get; private set; }
        public bool NeedsEmailVerification { get; private set; }
        public string MessageText { get; private set; } = string.Empty;
        public AuthSession? Session { get; private set; }

        internal static AuthResult Success(AuthSession session) => new() { Ok = true, Session = session };
        internal static AuthResult Fail(string message) => new() { MessageText = message };
        internal static AuthResult PendingVerification(string message) => new() { Ok = true, NeedsEmailVerification = true, MessageText = message };
        internal static AuthResult Message(string message) => new() { Ok = true, MessageText = message };
    }
}
