#!/usr/bin/env python3
from pathlib import Path
import re
import sys


def require(text: str, needle: str, label: str) -> None:
    if needle not in text:
        raise RuntimeError(f"Render security validation failed: missing {label}")


def forbid(text: str, pattern: str, label: str, flags: int = 0) -> None:
    if re.search(pattern, text, flags):
        raise RuntimeError(f"Render security validation failed: forbidden {label}")


def main() -> None:
    root = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
    android = root / "VoiceCraft.Server.Android"
    api_path = android / "RenderApiClient.cs"
    activity_path = android / "ModernMainActivity.cs"
    prefs_path = android / "ServerPreferences.cs"
    upstream_path = root / "VoiceCraft.Upstream" / "VoiceCraft.Network" / "Servers" / "VoiceCraftServer.cs"

    for path in (api_path, activity_path, prefs_path, upstream_path):
        if not path.exists():
            raise RuntimeError(f"Render security validation failed: missing {path}")

    api = api_path.read_text(encoding="utf-8")
    activity = activity_path.read_text(encoding="utf-8")
    prefs = prefs_path.read_text(encoding="utf-8")
    upstream = upstream_path.read_text(encoding="utf-8")

    require(api, 'new("https://api.render.com/v1/")', "fixed HTTPS Render API origin")
    require(api, "AllowAutoRedirect = false", "redirect blocking")
    require(api, 'request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim())', "Bearer authorization")
    require(api, 'private const string RelayBranch = "main";', "stable relay source branch")
    require(api, 'private const string RelayRootDir = "VoiceCraft.Bridge.Relay";', "relay root directory")
    require(api, 'new { key = "BRIDGE_SECRET", value = bridgeSecret }', "BRIDGE_SECRET environment variable")
    require(api, 'buildCommand = "npm install --omit=dev"', "relay build command")
    require(api, 'startCommand = "npm start"', "relay start command")
    require(api, 'healthCheckPath = "/health"', "relay health check")
    require(api, 'Redact(ExtractErrorMessage(responseText), apiKey)', "API error redaction")

    require(activity, "private EditText? _renderApiKey;", "session-only Render key field")
    require(activity, "_renderApiKey.Text = string.Empty;", "API key wipe")
    require(activity, "_renderProvisionCts?.Cancel();", "deploy poll cancellation")
    require(activity, '"BRIDGE" => HasLogCategory(row, "BRIDGE") || HasLogCategory(row, "RENDER")', "Render log filtering")
    require(activity, 'ServerPreferences.SaveBridge(this, true, websocket, CurrentServerId(), CurrentSecret())', "automatic relay persistence")

    # Never persist, copy, or log the API-key variable.
    forbid(prefs, r"render\s*_?api\s*_?key|renderApiKey", "Render API key persistence", re.I)
    forbid(activity, r"ServerPreferences\.[A-Za-z0-9_]*(?:\([^\n]*_renderApiKey|_renderApiKey[^\n]*\))", "Render API key sent to preferences")
    forbid(activity, r"AndroidRuntimeLog\.Append\([^\n]*\bapiKey\b", "Render API key written to logs")
    forbid(activity, r"Clipboard[^\n]*_renderApiKey|_renderApiKey[^\n]*Clipboard", "Render API key copied to clipboard", re.I)
    forbid(activity, r"CopyAllSetup[\s\S]{0,1500}_renderApiKey", "Render API key in copied setup")

    # Keep existing moderation/audio and protocol safety invariants untouched.
    require(upstream, "networkEntity.Muted || networkEntity.ServerMuted || itemMicMuted", "moderation-safe Item Mic audio gate")
    manifest = (root / "release-manifest.json").read_text(encoding="utf-8")
    require(manifest, '"protocol": 1', "protocol 1 manifest")

    print("Render provisioning security validation passed.")
    print("- API key is session-only, wiped on destroy, and excluded from prefs/logs/clipboard")
    print("- Render API requests stay on HTTPS api.render.com with redirects disabled")
    print("- relay source/build/env settings are pinned to the expected VoiceCraft configuration")
    print("- Item Mic moderation gate and protocol 1 remain intact")


if __name__ == "__main__":
    main()
