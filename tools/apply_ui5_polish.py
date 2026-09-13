#!/usr/bin/env python3
from pathlib import Path
import sys


def replace_required(text: str, old: str, new: str, label: str) -> str:
    if old not in text:
        raise RuntimeError(f"UI5 polish failed: {label}")
    return text.replace(old, new, 1)


def main() -> None:
    root = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
    path = root / "VoiceCraft.Server.Android" / "ModernMainActivity.cs"
    text = path.read_text(encoding="utf-8")

    # 1) Render Relay: remove the decorative always-connected green badge.
    # Runtime relay state is still available on Dashboard and Runtime Logs.
    relay_with_badge = '''        var relay = Card();
        var relayHead = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        relayHead.SetGravity(GravityFlags.CenterVertical);
        relayHead.AddView(SectionTitle("Render Relay", Primary), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        relayHead.AddView(Pill(T("● เชื่อมต่อ", "● CONNECTED"), SuccessFill, Green, true), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, Dp(30)));
        relay.AddView(relayHead);
'''
    relay_plain = '''        var relay = Card();
        relay.AddView(SectionTitle("Render Relay", Primary));
'''
    text = replace_required(text, relay_with_badge, relay_plain, "remove Render Relay connected badge")

    # 2) Endstone Integration: remove the decorative bridge-connected text.
    # Real bridge state remains visible from Dashboard and Runtime Logs.
    endstone_connected = '''        endstone.AddView(Label(T("● Minecraft bridge connected", "● Minecraft bridge connected"), 11, Green, true), Top(Dp(8)));
'''
    text = replace_required(text, endstone_connected, "", "remove Endstone bridge connected text")

    # 3) Dashboard: keep ONLINE/OFFLINE in the same address panel as IP/Port,
    # and remove the decorative progress bar under the server status card.
    old_top_status = '''        var onlineRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        onlineRow.SetGravity(GravityFlags.CenterVertical);
        _statusBadge = Pill(T("● ออฟไลน์", "● OFFLINE"), _dark ? Color.Rgb(20, 58, 48) : Color.Rgb(229, 249, 239), Green, true);
        onlineRow.AddView(_statusBadge, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, Dp(32)));
        body.AddView(onlineRow, Top(Dp(2)));
        body.AddView(Label("VoiceCraft Server", 18, Ink, true), Top(Dp(12)));
'''
    new_top_status = '''        body.AddView(Label("VoiceCraft Server", 18, Ink, true), Top(Dp(2)));
'''
    text = replace_required(text, old_top_status, new_top_status, "move dashboard status badge")

    address_title = '''        addressBox.SetPadding(Dp(14), Dp(12), Dp(14), Dp(12));
        addressBox.AddView(Label(T("ที่อยู่เซิร์ฟเวอร์", "SERVER ADDRESS"), 10, Primary, true));
'''
    address_with_status = '''        addressBox.SetPadding(Dp(14), Dp(12), Dp(14), Dp(12));
        addressBox.AddView(Label(T("ที่อยู่เซิร์ฟเวอร์", "SERVER ADDRESS"), 10, Primary, true));

        var statusRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        statusRow.SetGravity(GravityFlags.CenterVertical);
        statusRow.AddView(Label(T("สถานะ", "STATUS"), 10, Muted, true), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        _statusBadge = Pill(T("● ออฟไลน์", "● OFFLINE"), _dark ? Color.Rgb(20, 58, 48) : Color.Rgb(229, 249, 239), Green, true);
        statusRow.AddView(_statusBadge, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, Dp(32)));
        addressBox.AddView(statusRow, Top(Dp(10)));
'''
    text = replace_required(text, address_title, address_with_status, "add status badge to address panel")

    progress = '''        var progress = new ProgressBar(this, null, global::Android.Resource.Attribute.ProgressBarStyleHorizontal)
        {
            Indeterminate = false,
            Progress = 72,
            Max = 100
        };
        running.AddView(progress, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(5)) { TopMargin = Dp(14) });
'''
    text = replace_required(text, progress, "", "remove dashboard progress bar")

    path.write_text(text, encoding="utf-8")
    print(f"Applied UI5 polish to {path}")
    print("- removed decorative Render Relay connected badge")
    print("- removed decorative Endstone bridge connected text")
    print("- moved ONLINE/OFFLINE into the server address panel")
    print("- removed dashboard progress bar")


if __name__ == "__main__":
    main()
