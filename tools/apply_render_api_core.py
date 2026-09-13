#!/usr/bin/env python3
from pathlib import Path
import sys


RENDER_API_CLIENT = r'''using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace VoiceCraft.Server.Android;

internal sealed record RenderWorkspaceOption(string Id, string Name, string Email);
internal sealed record RenderCreatedService(string Id, string Name, string Url);

internal sealed class RenderApiException : Exception
{
    internal HttpStatusCode? StatusCode { get; }

    internal RenderApiException(string message, HttpStatusCode? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}

internal static class RenderApiClient
{
    private const string RelayRepository = "https://github.com/samsosleepy2007/VoiceCraft-Server-Mobile-Unofficial";
    private const string RelayBranch = "main";
    private const string RelayRootDir = "VoiceCraft.Bridge.Relay";
    private static readonly Uri BaseUri = new("https://api.render.com/v1/");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(12),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    internal static async Task<IReadOnlyList<RenderWorkspaceOption>> ListWorkspacesAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        ValidateApiKey(apiKey);
        using var document = await SendJsonAsync(
            HttpMethod.Get,
            "owners?limit=100",
            apiKey,
            body: null,
            cancellationToken);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new RenderApiException("Render returned an unexpected workspace response.");

        var result = new List<RenderWorkspaceOption>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var owner = item;
            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("owner", out var nestedOwner)
                && nestedOwner.ValueKind == JsonValueKind.Object)
                owner = nestedOwner;

            var id = ReadString(owner, "id");
            var name = ReadString(owner, "name");
            var email = ReadString(owner, "email");
            if (string.IsNullOrWhiteSpace(id))
                continue;
            if (string.IsNullOrWhiteSpace(name))
                name = id;
            result.Add(new RenderWorkspaceOption(id, name, email));
        }

        if (result.Count == 0)
            throw new RenderApiException("No Render workspace is available for this API key.");
        return result;
    }

    internal static async Task<RenderCreatedService> CreateRelayServiceAsync(
        string apiKey,
        string ownerId,
        string serviceName,
        string region,
        string plan,
        string bridgeSecret,
        CancellationToken cancellationToken = default)
    {
        ValidateApiKey(apiKey);
        if (string.IsNullOrWhiteSpace(ownerId))
            throw new RenderApiException("Select a Render workspace first.");

        serviceName = serviceName.Trim().ToLowerInvariant();
        if (serviceName.Length is < 2 or > 63
            || serviceName.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch == '-')))
            throw new RenderApiException("Service name can use only letters, numbers, and hyphens.");

        region = NormalizeRegion(region);
        plan = NormalizePlan(plan);
        if (string.IsNullOrWhiteSpace(bridgeSecret) || bridgeSecret.Length < 16)
            throw new RenderApiException("Bridge Secret must contain at least 16 characters.");

        var body = new
        {
            type = "web_service",
            name = serviceName,
            ownerId,
            repo = RelayRepository,
            branch = RelayBranch,
            rootDir = RelayRootDir,
            autoDeploy = "yes",
            envVars = new[]
            {
                new { key = "BRIDGE_SECRET", value = bridgeSecret }
            },
            serviceDetails = new
            {
                runtime = "node",
                plan,
                region,
                healthCheckPath = "/health",
                envSpecificDetails = new
                {
                    buildCommand = "npm install --omit=dev",
                    startCommand = "npm start"
                }
            }
        };

        using var document = await SendJsonAsync(
            HttpMethod.Post,
            "services",
            apiKey,
            body,
            cancellationToken);

        var service = UnwrapObject(document.RootElement, "service");
        var id = ReadString(service, "id");
        var name = ReadString(service, "name");
        var url = ReadString(service, "url");
        if (service.ValueKind == JsonValueKind.Object
            && service.TryGetProperty("serviceDetails", out var details)
            && details.ValueKind == JsonValueKind.Object)
            url = ReadString(details, "url") is { Length: > 0 } nestedUrl ? nestedUrl : url;

        if (string.IsNullOrWhiteSpace(id))
            throw new RenderApiException("Render created the service but did not return a service ID.");
        if (string.IsNullOrWhiteSpace(name))
            name = serviceName;
        return new RenderCreatedService(id, name, url);
    }

    internal static async Task<JsonDocument> SendJsonAsync(
        HttpMethod method,
        string relativePath,
        string apiKey,
        object? body,
        CancellationToken cancellationToken = default)
    {
        ValidateApiKey(apiKey);
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("Render API path is required.", nameof(relativePath));

        var target = new Uri(BaseUri, relativePath.TrimStart('/'));
        if (!string.Equals(target.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(target.Host, BaseUri.Host, StringComparison.OrdinalIgnoreCase))
            throw new RenderApiException("Blocked an unsafe Render API destination.");

        using var request = new HttpRequestMessage(method, target);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("VoiceCraft-Server-Mobile/1.7.1");
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await Client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RenderApiException("Render API request timed out.");
        }
        catch (Exception ex)
        {
            throw new RenderApiException("Unable to reach the Render API.", inner: ex);
        }

        using (response)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if ((int)response.StatusCode is >= 300 and < 400)
                throw new RenderApiException("Render API redirect was blocked for credential safety.", response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                var safe = ExtractErrorMessage(responseText);
                safe = Redact(safe, apiKey);
                throw new RenderApiException(
                    string.IsNullOrWhiteSpace(safe)
                        ? $"Render API returned HTTP {(int)response.StatusCode}."
                        : $"Render API returned HTTP {(int)response.StatusCode}: {safe}",
                    response.StatusCode);
            }

            try
            {
                return JsonDocument.Parse(string.IsNullOrWhiteSpace(responseText) ? "{}" : responseText);
            }
            catch (JsonException ex)
            {
                throw new RenderApiException("Render returned invalid JSON.", response.StatusCode, ex);
            }
        }
    }

    private static string NormalizeRegion(string value) => value.Trim().ToLowerInvariant() switch
    {
        "singapore" => "singapore",
        "oregon" => "oregon",
        "frankfurt" => "frankfurt",
        "ohio" => "ohio",
        "virginia" => "virginia",
        _ => throw new RenderApiException("Unsupported Render region.")
    };

    private static string NormalizePlan(string value) => value.Trim().ToLowerInvariant() switch
    {
        "free" => "free",
        _ => throw new RenderApiException("Unsupported Render plan.")
    };

    private static void ValidateApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Trim().Length < 12)
            throw new RenderApiException("Enter a valid Render API key.");
    }

    private static JsonElement UnwrapObject(JsonElement element, string property)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var nested)
            && nested.ValueKind == JsonValueKind.Object)
            return nested;
        return element;
    }

    private static string ReadString(JsonElement element, string property)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? string.Empty;
        return string.Empty;
    }

    private static string ExtractErrorMessage(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "message", "error", "detail" })
                {
                    if (doc.RootElement.TryGetProperty(key, out var value)
                        && value.ValueKind == JsonValueKind.String)
                        return value.GetString() ?? string.Empty;
                }
            }
        }
        catch (JsonException)
        {
        }
        return string.Empty;
    }

    private static string Redact(string text, string secret)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(secret))
            return text;
        return text.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
    }
}
'''


def main() -> None:
    root = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
    android = root / "VoiceCraft.Server.Android"
    if not android.is_dir():
        raise SystemExit(f"Android project not found: {android}")

    target = android / "RenderApiClient.cs"
    target.write_text(RENDER_API_CLIENT, encoding="utf-8")
    print(f"Generated Render API core client: {target}")
    print("- API key is request-scoped and never persisted by the client")
    print("- redirects are blocked to avoid credential forwarding")
    print("- workspace discovery uses GET /v1/owners")
    print("- relay creation uses the stable main branch and BRIDGE_SECRET env var")


if __name__ == "__main__":
    main()
