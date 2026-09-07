using Android.Content;

namespace VoiceCraft.Server.Android;

/// <summary>
/// Managed Button wrapper used by the Android UI. It exposes writable minimum
/// dimensions for object initializers while still using TextView.SetMinHeight/
/// SetMinWidth underneath.
/// </summary>
internal sealed class Button : global::Android.Widget.Button
{
    internal Button(Context context) : base(context)
    {
    }

    public new int MinHeight
    {
        set => SetMinHeight(value);
    }

    public new int MinWidth
    {
        set => SetMinWidth(value);
    }
}
