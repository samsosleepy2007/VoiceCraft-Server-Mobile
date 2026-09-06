#!/usr/bin/env python3
from __future__ import annotations

import argparse
from pathlib import Path


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Disable Spectre.Console rendering for VoiceCraft Android headless mode"
    )
    parser.add_argument(
        "repo",
        nargs="?",
        default="VoiceCraft.Upstream",
        help="Path to the patched VoiceCraft v1.7.1 checkout",
    )
    args = parser.parse_args()

    root = Path(args.repo).resolve()
    server_root = root / "VoiceCraft.Server"
    app_path = server_root / "App.cs"

    if not app_path.exists():
        raise SystemExit(f"VoiceCraft.Server/App.cs not found under {root}")

    app = app_path.read_text(encoding="utf-8-sig")
    needle = '''        var ownsServiceProvider = runtimeOptions.Headless;\n\n        var languageOverriden = !string.IsNullOrWhiteSpace(runtimeOptions.Language);'''
    replacement = '''        var ownsServiceProvider = runtimeOptions.Headless;\n\n        // Android does not provide a desktop terminal. Spectre.Console attempts\n        // to query terminal width while rendering and throws\n        // PlatformNotSupportedException, so suppress terminal rendering for\n        // embedded/headless hosts. ServerConsole still forwards plain messages\n        // to an optional log sink used by the Android UI.\n        ServerConsole.Enabled = !runtimeOptions.Headless;\n\n        var languageOverriden = !string.IsNullOrWhiteSpace(runtimeOptions.Language);'''

    if needle not in app:
        raise RuntimeError("Expected Phase 1 App.Start block was not found")
    app_path.write_text(app.replace(needle, replacement, 1), encoding="utf-8")

    # Route every Spectre.Console write in the server assembly through one guard.
    # This includes startup/config messages, client connect/disconnect messages,
    # commands, shutdown output and optional port-mapping messages.
    patched_files = []
    for path in server_root.rglob("*.cs"):
        if path.name == "ServerConsole.cs":
            continue
        text = path.read_text(encoding="utf-8-sig")
        if "AnsiConsole." not in text:
            continue
        updated = text.replace(
            "AnsiConsole.",
            "global::VoiceCraft.Server.ServerConsole.",
        )
        path.write_text(updated, encoding="utf-8")
        patched_files.append(path.relative_to(root).as_posix())

    helper = '''using Spectre.Console;\nusing Spectre.Console.Rendering;\n\nnamespace VoiceCraft.Server;\n\n/// <summary>\n/// Console facade that can be disabled for platforms without a terminal, such\n/// as the Android foreground-service host. Messages can still be forwarded to\n/// Sink so a mobile UI can expose useful runtime diagnostics.\n/// </summary>\npublic static class ServerConsole\n{\n    public static bool Enabled { get; set; } = true;\n    public static Action<string>? Sink { get; set; }\n\n    private static string StripMarkup(string text)\n    {\n        try\n        {\n            return Markup.Remove(text);\n        }\n        catch\n        {\n            return text;\n        }\n    }\n\n    public static void Write(IRenderable renderable)\n    {\n        Sink?.Invoke($"[renderable] {renderable.GetType().Name}");\n        if (Enabled)\n            AnsiConsole.Write(renderable);\n    }\n\n    public static void WriteLine(string text)\n    {\n        Sink?.Invoke(text);\n        if (Enabled)\n            AnsiConsole.WriteLine(text);\n    }\n\n    public static void MarkupLine(string markup)\n    {\n        Sink?.Invoke(StripMarkup(markup));\n        if (Enabled)\n            AnsiConsole.MarkupLine(markup);\n    }\n}\n'''
    (server_root / "ServerConsole.cs").write_text(helper, encoding="utf-8")

    if not patched_files:
        raise RuntimeError("No Spectre.Console call sites were patched")

    print("Android headless console fix applied to:")
    for path in patched_files:
        print(f"  - {path}")


if __name__ == "__main__":
    main()
