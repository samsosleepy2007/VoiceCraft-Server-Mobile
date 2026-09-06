namespace VoiceCraft.Server.Android;

/// <summary>
/// Thread-safe in-memory log used by the Android UI. It intentionally avoids
/// writing login/session tokens so logs are safe to copy for troubleshooting.
/// </summary>
public static class AndroidRuntimeLog
{
    private const int MaxLines = 600;
    private static readonly object Sync = new();
    private static readonly Queue<string> Lines = new();
    private static long _version;

    public static long Version
    {
        get
        {
            lock (Sync)
                return _version;
        }
    }

    public static void Append(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var normalized = message.Replace("\r\n", "\n").Replace('\r', '\n');
        lock (Sync)
        {
            foreach (var line in normalized.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                Lines.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] {line}");
                while (Lines.Count > MaxLines)
                    Lines.Dequeue();
            }
            _version++;
        }
    }

    public static void Append(string category, string message)
        => Append($"{category}: {message}");

    public static string Snapshot() => Snapshot(false);

    public static string Snapshot(bool thai)
    {
        string raw;
        lock (Sync)
            raw = string.Join("\n", Lines);

        var output = RuntimeDiagnostics.TranslateSnapshot(raw, thai);
        if (!thai)
            return output;

        return output
            .Replace(" HELP: ", " คำแนะนำ: ", StringComparison.OrdinalIgnoreCase)
            .Replace(" | Cause: ", " | สาเหตุ: ", StringComparison.OrdinalIgnoreCase)
            .Replace(" | Fix: ", " | วิธีแก้: ", StringComparison.OrdinalIgnoreCase);
    }

    public static void Clear()
    {
        lock (Sync)
        {
            Lines.Clear();
            _version++;
        }
    }
}
