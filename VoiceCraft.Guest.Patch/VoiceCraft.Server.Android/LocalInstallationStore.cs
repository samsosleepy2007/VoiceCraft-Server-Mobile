namespace VoiceCraft.Server.Android;

internal static class LocalInstallationStore
{
    private const string FileName = "voicecraft_installation_id.txt";
    private static readonly object Sync = new();

    public static string GetOrCreate(global::Android.Content.Context context)
    {
        lock (Sync)
        {
            var dir = context.NoBackupFilesDir?.AbsolutePath
                ?? context.FilesDir?.AbsolutePath
                ?? throw new InvalidOperationException("Android app storage is unavailable.");

            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, FileName);

            if (File.Exists(path))
            {
                var current = File.ReadAllText(path).Trim();
                if (Guid.TryParse(current, out _))
                    return current;
            }

            var value = Guid.NewGuid().ToString("D");
            File.WriteAllText(path, value);
            return value;
        }
    }
}
