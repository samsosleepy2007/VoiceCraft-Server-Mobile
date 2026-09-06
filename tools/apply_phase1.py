#!/usr/bin/env python3
from __future__ import annotations

import argparse
from pathlib import Path


def replace_once(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding="utf-8-sig")
    if old not in text:
        raise RuntimeError(f"Expected source block not found in {path}:\n{old[:200]}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")


def write(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


def patch_server_project(root: Path) -> None:
    path = root / "VoiceCraft.Server/VoiceCraft.Server.csproj"
    # The Android APK is self-contained. .NET does not allow a self-contained
    # executable to reference the upstream framework-dependent server EXE.
    # For the mobile host the server assembly is embedded, so build it as a DLL.
    replace_once(path, "        <OutputType>Exe</OutputType>", "        <OutputType>Library</OutputType>")


def patch_program(root: Path) -> None:
    path = root / "VoiceCraft.Server/Program.cs"
    replace_once(path,
'''    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;
        Localizer.BaseLocalizer = new EmbeddedJsonLocalizer("VoiceCraft.Server.Locales");
        FleckLog.LogAction = (_, _, _) => { }; //Remove all websocket logs.
        LogService.Load(); //Load Logs.
        new VoiceCraftRootCommand().Parse(args).InvokeAsync().GetAwaiter().GetResult();
        ServiceProvider.Dispose(); //Dispose
    }

    private static ServiceProvider BuildServiceProvider()
''',
'''    public static void Main(string[] args)
    {
        InitializeRuntime();
        new VoiceCraftRootCommand().Parse(args).InvokeAsync().GetAwaiter().GetResult();
        ServiceProvider.Dispose(); //Dispose
    }

    /// <summary>
    /// Initializes platform-neutral VoiceCraft server services. Embedded/mobile
    /// hosts can point configuration and logs to a writable application folder.
    /// </summary>
    public static void InitializeRuntime(string? dataDirectory = null)
    {
        ServerPaths.Configure(dataDirectory);
        AppDomain.CurrentDomain.UnhandledException -= CurrentDomainOnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;
        Localizer.BaseLocalizer = new EmbeddedJsonLocalizer("VoiceCraft.Server.Locales");
        FleckLog.LogAction = (_, _, _) => { }; //Remove all websocket logs.
        LogService.Load();
    }

    public static ServiceProvider BuildServiceProvider()
''')


def patch_server_properties(root: Path) -> None:
    path = root / "VoiceCraft.Server/ServerProperties.cs"
    replace_once(path,
        "        var files = Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, FileName, SearchOption.AllDirectories);",
        "        var files = Directory.GetFiles(ServerPaths.BaseDirectory, FileName, SearchOption.AllDirectories);")
    replace_once(path,
'''        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigPath);
        var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigPath, FileName);''',
'''        var path = Path.Combine(ServerPaths.BaseDirectory, ConfigPath);
        var filePath = Path.Combine(ServerPaths.BaseDirectory, ConfigPath, FileName);''')
    replace_once(path,
'''    public string? ServerKey { get; init; }
}''',
'''    public string? ServerKey { get; init; }
    /// <summary>Run without console input/title handling for mobile/service hosts.</summary>
    public bool Headless { get; init; }
}''')


def patch_log_service(root: Path) -> None:
    path = root / "VoiceCraft.Server/LogService.cs"
    replace_once(path,
        "            var fileDirectory = Path.Join(ConfigPath, FileName);",
        "            var fileDirectory = Path.Combine(ServerPaths.BaseDirectory, ConfigPath, FileName);")
    replace_once(path,
'''        if (!Directory.Exists(ConfigPath))
            Directory.CreateDirectory(ConfigPath);

        File.WriteAllBytes(Path.Join(ConfigPath, FileName),''',
'''        var configDirectory = Path.Combine(ServerPaths.BaseDirectory, ConfigPath);
        if (!Directory.Exists(configDirectory))
            Directory.CreateDirectory(configDirectory);

        File.WriteAllBytes(Path.Combine(configDirectory, FileName),''')


def patch_app(root: Path) -> None:
    path = root / "VoiceCraft.Server/App.cs"
    replace_once(path,
'''    private static bool _shuttingDown;
    private static readonly CancellationTokenSource Cts = new();
    private static string? _bufferedCommand;''',
'''    private static readonly object LifecycleLock = new();
    private static bool _shuttingDown;
    private static CancellationTokenSource? _cts;
    private static string? _bufferedCommand;

    public static bool IsRunning { get; private set; }
    public static int ConnectedClients { get; private set; }''')

    replace_once(path,
'''    public static async Task Start(RuntimeOptions runtimeOptions)
    {
        var languageOverriden = !string.IsNullOrWhiteSpace(runtimeOptions.Language);''',
'''    public static async Task Start(RuntimeOptions runtimeOptions)
    {
        CancellationTokenSource cts;
        lock (LifecycleLock)
        {
            if (IsRunning)
                throw new InvalidOperationException("VoiceCraft server is already running.");

            _shuttingDown = false;
            _bufferedCommand = null;
            ConnectedClients = 0;
            _cts = cts = new CancellationTokenSource();
            IsRunning = true;
        }

        var serviceProvider = runtimeOptions.Headless
            ? Program.BuildServiceProvider()
            : Program.ServiceProvider;
        var ownsServiceProvider = runtimeOptions.Headless;

        var languageOverriden = !string.IsNullOrWhiteSpace(runtimeOptions.Language);''')

    text = path.read_text(encoding="utf-8")
    text = text.replace("Program.ServiceProvider.GetRequiredService<", "serviceProvider.GetRequiredService<")
    text = text.replace("Program.ServiceProvider.GetServices<Command>()", "serviceProvider.GetServices<Command>()")
    path.write_text(text, encoding="utf-8")

    replace_once(path,
        '            Console.Title = $"VoiceCraft - {VoiceCraftServer.Version}: {Localizer.Get("Title.Starting")}";',
        '            if (!runtimeOptions.Headless)\n                Console.Title = $"VoiceCraft - {VoiceCraftServer.Version}: {Localizer.Get("Title.Starting")}";')
    replace_once(path,
        '            Console.Title = $"VoiceCraft - {VoiceCraftServer.Version}: {Localizer.Get("Title.Running")}";',
        '            if (!runtimeOptions.Headless)\n                Console.Title = $"VoiceCraft - {VoiceCraftServer.Version}: {Localizer.Get("Title.Running")}";')
    replace_once(path,
        "            StartCommandTask();\n            var startTime = DateTime.UtcNow;",
        "            if (!runtimeOptions.Headless)\n                StartCommandTask(cts);\n            var startTime = DateTime.UtcNow;")
    replace_once(path, "            while (!Cts.IsCancellationRequested)", "            while (!cts.IsCancellationRequested)")
    replace_once(path,
        "                    await FlushCommand(rootCommand);",
        "                    if (!runtimeOptions.Headless)\n                        await FlushCommand(rootCommand);\n                    ConnectedClients = liteNetServer.ConnectedPeers;")
    replace_once(path, "            Shutdown(10000);", "            Shutdown(runtimeOptions.Headless ? 0u : 10000u);")
    replace_once(path,
'''            Cts.Dispose();
        }
    }

    public static void Shutdown(uint delayMs = 0)
    {
        if (Cts.IsCancellationRequested || _shuttingDown) return;''',
'''            if (ownsServiceProvider)
                serviceProvider.Dispose();
            cts.Dispose();
            lock (LifecycleLock)
            {
                if (ReferenceEquals(_cts, cts))
                    _cts = null;
                ConnectedClients = 0;
                IsRunning = false;
                _shuttingDown = false;
            }
        }
    }

    public static void Shutdown(uint delayMs = 0)
    {
        var cts = _cts;
        if (cts == null || cts.IsCancellationRequested || _shuttingDown) return;''')
    replace_once(path, "        Cts.Cancel();", "        cts.Cancel();")
    replace_once(path,
'''    private static void StartCommandTask()
    {
        Task.Run(async () =>
        {
            while (!Cts.IsCancellationRequested && !_shuttingDown)''',
'''    private static void StartCommandTask(CancellationTokenSource cts)
    {
        Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested && !_shuttingDown)''')
    replace_once(path,
        "                if (Cts.IsCancellationRequested || _shuttingDown) return;",
        "                if (cts.IsCancellationRequested || _shuttingDown) return;")


def add_server_paths(root: Path) -> None:
    write(root / "VoiceCraft.Server/ServerPaths.cs", '''namespace VoiceCraft.Server;

/// <summary>
/// Resolves writable server storage independently from the executable location.
/// Desktop keeps the original base-directory behavior; mobile hosts can point
/// this to application-private writable storage.
/// </summary>
public static class ServerPaths
{
    private static readonly object Sync = new();
    private static string _baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

    public static string BaseDirectory
    {
        get
        {
            lock (Sync)
                return _baseDirectory;
        }
    }

    public static void Configure(string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            return;

        var fullPath = Path.GetFullPath(baseDirectory);
        Directory.CreateDirectory(fullPath);
        lock (Sync)
            _baseDirectory = fullPath;
    }
}
''')


def main() -> None:
    parser = argparse.ArgumentParser(description="Patch VoiceCraft v1.7.1 for Android headless hosting")
    parser.add_argument("repo", nargs="?", default="VoiceCraft.Upstream", help="Path to VoiceCraft v1.7.1 checkout/submodule")
    args = parser.parse_args()
    root = Path(args.repo).resolve()

    required = [
        root / "VoiceCraft.Server/VoiceCraft.Server.csproj",
        root / "VoiceCraft.Server/App.cs",
        root / "VoiceCraft.Server/Program.cs",
        root / "VoiceCraft.Server/ServerProperties.cs",
        root / "VoiceCraft.Server/LogService.cs",
        root / "VoiceCraft.Network/Servers/LiteNetVoiceCraftServer.cs",
    ]
    missing = [str(p) for p in required if not p.exists()]
    if missing:
        raise SystemExit("Not a VoiceCraft v1.7.1 checkout; missing:\n" + "\n".join(missing))

    patch_server_project(root)
    patch_program(root)
    patch_server_properties(root)
    patch_log_service(root)
    patch_app(root)
    add_server_paths(root)
    print("VoiceCraft v1.7.1 Android headless patch applied.")


if __name__ == "__main__":
    main()
