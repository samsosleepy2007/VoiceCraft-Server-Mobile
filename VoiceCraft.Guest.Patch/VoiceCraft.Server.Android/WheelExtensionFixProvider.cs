using System.Threading;
using Android.Content;
using Android.Database;
using Android.OS;
using Android.Provider;
using AndroidUri = Android.Net.Uri;

namespace VoiceCraft.Server.Android;

// Android/File Manager variants may append .zip because Python wheels are ZIP containers
// and the downloader writes them through MediaStore with a ZIP MIME type. Keep that
// behavior simple, then immediately normalize *.whl.zip back to the real wheel name.
[ContentProvider(new[] { "chat.voicecraft.server.wheelextensionfix" }, Exported = false, InitOrder = 1100)]
public sealed class WheelExtensionFixProvider : ContentProvider
{
    private WheelExtensionObserver? _observer;

    public override bool OnCreate()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Q)
            return true;

        try
        {
            var resolver = Context?.ContentResolver;
            if (resolver != null)
            {
                _observer = new WheelExtensionObserver(resolver);
                resolver.RegisterContentObserver(MediaStore.Downloads.ExternalContentUri, true, _observer);
                WheelExtensionFix.Normalize(resolver);
            }
        }
        catch (Exception ex)
        {
            AndroidRuntimeLog.Append("UI", $"Wheel extension normalizer unavailable: {ex.GetType().Name}: {ex.Message}");
        }

        return true;
    }

    public override ICursor? Query(AndroidUri uri, string[]? projection, string? selection, string[]? selectionArgs, string? sortOrder) => null;
    public override string? GetType(AndroidUri uri) => null;
    public override AndroidUri? Insert(AndroidUri uri, ContentValues? values) => null;
    public override int Delete(AndroidUri uri, string? selection, string[]? selectionArgs) => 0;
    public override int Update(AndroidUri uri, ContentValues? values, string? selection, string[]? selectionArgs) => 0;
}

internal sealed class WheelExtensionObserver : ContentObserver
{
    private readonly ContentResolver _resolver;
    private int _busy;

    public WheelExtensionObserver(ContentResolver resolver)
        : base(new Handler(Looper.MainLooper!))
    {
        _resolver = resolver;
    }

    public override void OnChange(bool selfChange)
    {
        base.OnChange(selfChange);
        Normalize();
    }

    public override void OnChange(bool selfChange, AndroidUri? uri)
    {
        base.OnChange(selfChange, uri);
        Normalize();
    }

    private void Normalize()
    {
        if (Interlocked.Exchange(ref _busy, 1) != 0)
            return;

        try
        {
            WheelExtensionFix.Normalize(_resolver);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }
}

internal static class WheelExtensionFix
{
    public static void Normalize(ContentResolver resolver)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Q)
            return;

#pragma warning disable CA1416
        var projection = new[]
        {
            "_id",
            MediaStore.IMediaColumns.DisplayName,
            MediaStore.IMediaColumns.RelativePath
        };

        using var cursor = resolver.Query(
            MediaStore.Downloads.ExternalContentUri,
            projection,
            MediaStore.IMediaColumns.DisplayName + " LIKE ?",
            new[] { "endstone_voicecraft-%.whl.zip" },
            null);

        if (cursor == null)
            return;

        var idIndex = cursor.GetColumnIndex("_id");
        var nameIndex = cursor.GetColumnIndex(MediaStore.IMediaColumns.DisplayName);
        var pathIndex = cursor.GetColumnIndex(MediaStore.IMediaColumns.RelativePath);
        if (idIndex < 0 || nameIndex < 0)
            return;

        while (cursor.MoveToNext())
        {
            var name = cursor.GetString(nameIndex) ?? string.Empty;
            var relativePath = pathIndex >= 0 ? cursor.GetString(pathIndex) ?? string.Empty : string.Empty;

            if (!name.StartsWith("endstone_voicecraft-", StringComparison.OrdinalIgnoreCase) ||
                !name.EndsWith(".whl.zip", StringComparison.OrdinalIgnoreCase) ||
                !relativePath.Contains("VoiceCraft", StringComparison.OrdinalIgnoreCase))
                continue;

            var fixedName = name[..^4]; // remove only the final .zip
            var id = cursor.GetLong(idIndex);
            var itemUri = ContentUris.WithAppendedId(MediaStore.Downloads.ExternalContentUri, id);
            var values = new ContentValues();
            values.Put(MediaStore.IMediaColumns.DisplayName, fixedName);
            values.Put(MediaStore.IMediaColumns.MimeType, "application/octet-stream");

            var updated = resolver.Update(itemUri, values, null, null);
            if (updated > 0)
                AndroidRuntimeLog.Append("UI", $"Normalized Endstone wheel filename: {name} -> {fixedName}");
        }
#pragma warning restore CA1416
    }
}
