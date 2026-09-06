#!/usr/bin/env python3
from __future__ import annotations

import argparse
from pathlib import Path


def replace_once(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding="utf-8-sig")
    if old not in text:
        raise RuntimeError(f"Expected source block not found in {path}:\n{old[:240]}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser(description="Add McHttp diagnostics hook for VoiceCraft Android")
    parser.add_argument("repo", nargs="?", default="VoiceCraft.Upstream")
    args = parser.parse_args()

    path = Path(args.repo).resolve() / "VoiceCraft.Network/Servers/HttpMcApiServer.cs"
    if not path.exists():
        raise SystemExit(f"HttpMcApiServer.cs not found: {path}")

    replace_once(
        path,
        '''    private const int MaxRequestLength = 1_000_000;\n''',
        '''    private const int MaxRequestLength = 1_000_000;\n\n    /// <summary>Optional non-secret diagnostics sink used by embedded hosts.</summary>\n    public static Action<string>? DiagnosticLog { get; set; }\n''')

    replace_once(
        path,
        '''                _httpServer.Start();\n''',
        '''                _httpServer.Start();\n                DiagnosticLog?.Invoke($"McHttp LISTENING {listenerPrefix}");\n''')

    replace_once(
        path,
        '''                var context = await listener.GetContextAsync();\n                _ = Task.Run(async () => await HandleRequestAsync(context)); //Threadpool it.\n''',
        '''                var context = await listener.GetContextAsync();\n                DiagnosticLog?.Invoke(\n                    $"McHttp RX {context.Request.HttpMethod} {context.Request.Url?.AbsolutePath ?? "/"} from {context.Request.RemoteEndPoint}");\n                _ = Task.Run(async () => await HandleRequestAsync(context)); //Threadpool it.\n''')

    replace_once(
        path,
        '''                if (!TryReadPackedPackets(stringData, packets))\n                {\n                    context.Response.StatusCode = 400;\n                    context.Response.Close();\n                    return;\n                }\n''',
        '''                if (!TryReadPackedPackets(stringData, packets))\n                {\n                    DiagnosticLog?.Invoke("McHttp REJECT 400 invalid packed payload");\n                    context.Response.StatusCode = 400;\n                    context.Response.Close();\n                    return;\n                }\n''')

    replace_once(
        path,
        '''            if (IsConnectPath(context.Request.Url?.AbsolutePath))\n            {\n                netPeer = await HandleConnectRequestAsync(packets);\n            }\n            else if (string.IsNullOrWhiteSpace(token) || !TryGetHttpPeer(token, out netPeer))\n            {\n                context.Response.StatusCode = 401;\n                context.Response.Close();\n                return;\n            }\n''',
        '''            if (IsConnectPath(context.Request.Url?.AbsolutePath))\n            {\n                DiagnosticLog?.Invoke($"McHttp CONNECT packets={packets.Count}");\n                netPeer = await HandleConnectRequestAsync(packets);\n                DiagnosticLog?.Invoke($"McHttp CONNECT result={netPeer.ConnectionState}");\n            }\n            else if (string.IsNullOrWhiteSpace(token) || !TryGetHttpPeer(token, out netPeer))\n            {\n                DiagnosticLog?.Invoke("McHttp REJECT 401 missing/invalid session token");\n                context.Response.StatusCode = 401;\n                context.Response.Close();\n                return;\n            }\n''')

    replace_once(
        path,
        '''        catch\n        {\n            try\n            {\n                context.Response.StatusCode = 500;\n''',
        '''        catch (Exception ex)\n        {\n            DiagnosticLog?.Invoke($"McHttp ERROR {ex.GetType().Name}: {ex.Message}");\n            try\n            {\n                context.Response.StatusCode = 500;\n''')

    replace_once(
        path,
        '''            context.Response.StatusCode = 200;\n            context.Response.ContentLength64 = encodedBytes;\n''',
        '''            context.Response.StatusCode = 200;\n            DiagnosticLog?.Invoke($"McHttp TX 200 packets={packets.Count} bytes={encodedBytes}");\n            context.Response.ContentLength64 = encodedBytes;\n''')

    print("McHttp diagnostics hook applied.")


if __name__ == "__main__":
    main()
