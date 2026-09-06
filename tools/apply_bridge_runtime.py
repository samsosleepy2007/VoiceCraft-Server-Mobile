#!/usr/bin/env python3
from __future__ import annotations

import argparse
from pathlib import Path


def replace_once(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding="utf-8-sig")
    if old not in text:
        raise RuntimeError(f"Expected source block not found in {path}:\n{old[:240]}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")


def write(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


def patch_app(root: Path) -> None:
    path = root / "VoiceCraft.Server/App.cs"

    replace_once(
        path,
        '''    public static bool IsRunning { get; private set; }
    public static int ConnectedClients { get; private set; }''',
        '''    public static bool IsRunning { get; private set; }
    public static int ConnectedClients { get; private set; }

    /// <summary>
    /// The currently running service provider for embedded/mobile hosts.
    /// External integrations must schedule world mutations through RuntimeDispatcher.
    /// </summary>
    public static IServiceProvider? ActiveServiceProvider { get; private set; }''',
    )

    replace_once(
        path,
        '''        var ownsServiceProvider = runtimeOptions.Headless;

        var languageOverriden = !string.IsNullOrWhiteSpace(runtimeOptions.Language);''',
        '''        var ownsServiceProvider = runtimeOptions.Headless;
        ActiveServiceProvider = serviceProvider;

        var languageOverriden = !string.IsNullOrWhiteSpace(runtimeOptions.Language);''',
    )

    replace_once(
        path,
        '''                    mcWssMcApiServer.Update();
                    visibilitySystem.Update();''',
        '''                    mcWssMcApiServer.Update();
                    RuntimeDispatcher.Drain();
                    visibilitySystem.Update();''',
    )

    replace_once(
        path,
        '''            if (ownsServiceProvider)
                serviceProvider.Dispose();
            cts.Dispose();''',
        '''            RuntimeDispatcher.Clear();
            if (ReferenceEquals(ActiveServiceProvider, serviceProvider))
                ActiveServiceProvider = null;
            if (ownsServiceProvider)
                serviceProvider.Dispose();
            cts.Dispose();''',
    )


def add_dispatcher(root: Path) -> None:
    write(
        root / "VoiceCraft.Server/RuntimeDispatcher.cs",
        '''using System.Collections.Concurrent;

namespace VoiceCraft.Server;

/// <summary>
/// Thread-safe handoff into the VoiceCraft server tick. Android bridge/network
/// callbacks enqueue work here so VoiceCraft world state is never mutated from
/// websocket or UI threads.
/// </summary>
public static class RuntimeDispatcher
{
    private static readonly ConcurrentQueue<Action> Queue = new();

    public static int PendingCount => Queue.Count;

    public static void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Queue.Enqueue(action);
    }

    internal static void Drain()
    {
        while (Queue.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                LogService.Log(ex);
            }
        }
    }

    internal static void Clear()
    {
        while (Queue.TryDequeue(out _))
        {
        }
    }
}
''',
    )


def main() -> None:
    parser = argparse.ArgumentParser(description="Patch VoiceCraft Android runtime for external Endstone bridge dispatch")
    parser.add_argument("repo", nargs="?", default="VoiceCraft.Upstream")
    args = parser.parse_args()
    root = Path(args.repo).resolve()
    path = root / "VoiceCraft.Server/App.cs"
    if not path.exists():
        raise SystemExit(f"Missing {path}")
    patch_app(root)
    add_dispatcher(root)
    print("VoiceCraft Android bridge runtime dispatcher patch applied.")


if __name__ == "__main__":
    main()
