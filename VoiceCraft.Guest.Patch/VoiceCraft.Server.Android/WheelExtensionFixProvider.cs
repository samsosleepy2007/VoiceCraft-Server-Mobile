using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Android.Content;
using Android.Database;
using Android.OS;
using Android.Provider;
using AndroidUri = Android.Net.Uri;

namespace VoiceCraft.Server.Android;

// Android/File Manager variants may append .zip because Python wheels are ZIP containers
// and may also add " (1)", " (2)", ... when a previous copy already exists. Normalize
// those storage-only suffixes, then add the Server ID as a valid wheel build tag so the
// final filename remains loadable by Python/Endstone.
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
            var context = Context;
            var resolver = context?.ContentResolver;
            if (resolver != null && context != null)
            {
                _observer = new WheelExtensionObserver(resolver, context);
                resolver.RegisterContentObserver(MediaStore.Downloads.ExternalContentUri, true, _observer);
                WheelExtensionFix.Normalize(resolver, context);
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
    private readonly Context _context;
    private int _busy;

    public WheelExtensionObserver(ContentResolver resolver, Context context)
        : base(new Handler(Looper.MainLooper!))
    {
        _resolver = resolver;
        _context = context;
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
            WheelExtensionFix.Normalize(_resolver, _context);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }
}

internal static class WheelExtensionFix
{
    public static void Normalize(ContentResolver resolver, Context context)
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
            new[] { "endstone_voicecraft-%" },
            null);

        if (cursor == null)
            return;

        var idIndex = cursor.GetColumnIndex("_id");
        var nameIndex = cursor.GetColumnIndex(MediaStore.IMediaColumns.DisplayName);
        var pathIndex = cursor.GetColumnIndex(MediaStore.IMediaColumns.RelativePath);
        if (idIndex < 0 || nameIndex < 0)
            return;

        var serverId = ServerPreferences.GetBridgeServerId(context).Trim();

        while (cursor.MoveToNext())
        {
            var name = cursor.GetString(nameIndex) ?? string.Empty;
            var relativePath = pathIndex >= 0 ? cursor.GetString(pathIndex) ?? string.Empty : string.Empty;

            if (!name.StartsWith("endstone_voicecraft-", StringComparison.OrdinalIgnoreCase) ||
                !relativePath.Contains("VoiceCraft", StringComparison.OrdinalIgnoreCase))
                continue;

            var fixedName = CanonicalWheelName(name, serverId);
            if (string.Equals(name, fixedName, StringComparison.Ordinal))
                continue;

            var id = cursor.GetLong(idIndex);
            DeleteConflictingCanonical(resolver, fixedName, relativePath, id);

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

    private static string CanonicalWheelName(string name, string serverId)
    {
        var fixedName = name;

        if (fixedName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            fixedName = fixedName[..^4];

        fixedName = RemoveDuplicateSuffix(fixedName);

        if (string.IsNullOrWhiteSpace(serverId))
            return fixedName;

        const string prefix = "endstone_voicecraft-";
        const string suffix = "-py3-none-any.whl";
        if (!fixedName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !fixedName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return fixedName;

        var middle = fixedName[prefix.Length..^suffix.Length];
        if (middle.Length == 0 || middle.Contains('-'))
            return fixedName; // already has a build tag or is not the canonical source wheel name

        var tag = ToWheelBuildTag(serverId);
        return $"{prefix}{middle}-1{tag}{suffix}";
    }

    private static string RemoveDuplicateSuffix(string name)
    {
        if (!name.EndsWith(".whl", StringComparison.OrdinalIgnoreCase))
            return name;

        var whlStart = name.Length - 4;
        var close = whlStart - 1;
        if (close < 0 || name[close] != ')')
            return name;

        var open = name.LastIndexOf(" (", close, StringComparison.Ordinal);
        if (open < 0 || open + 2 >= close)
            return name;

        for (var i = open + 2; i < close; i++)
        {
            if (!char.IsDigit(name[i]))
                return name;
        }

        return name[..open] + name[whlStart..];
    }

    private static string ToWheelBuildTag(string serverId)
    {
        var builder = new StringBuilder(24);
        foreach (var ch in serverId)
        {
            if (ch is >= 'A' and <= 'Z')
                builder.Append(char.ToLowerInvariant(ch));
            else if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
                builder.Append(ch);

            if (builder.Length >= 24)
                break;
        }

        if (builder.Length > 0)
            return builder.ToString();

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(serverId));
        return "u" + Convert.ToHexString(hash.AsSpan(0, 6)).ToLowerInvariant();
    }

    private static void DeleteConflictingCanonical(ContentResolver resolver, string fixedName, string relativePath, long currentId)
    {
        var projection = new[] { "_id", MediaStore.IMediaColumns.RelativePath };
        using var cursor = resolver.Query(
            MediaStore.Downloads.ExternalContentUri,
            projection,
            MediaStore.IMediaColumns.DisplayName + " = ?",
            new[] { fixedName },
            null);

        if (cursor == null)
            return;

        var idIndex = cursor.GetColumnIndex("_id");
        var pathIndex = cursor.GetColumnIndex(MediaStore.IMediaColumns.RelativePath);
        if (idIndex < 0)
            return;

        while (cursor.MoveToNext())
        {
            var id = cursor.GetLong(idIndex);
            if (id == currentId)
                continue;

            var candidatePath = pathIndex >= 0 ? cursor.GetString(pathIndex) ?? string.Empty : string.Empty;
            if (!candidatePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase))
                continue;

            var uri = ContentUris.WithAppendedId(MediaStore.Downloads.ExternalContentUri, id);
            resolver.Delete(uri, null, null);
        }
    }
}
