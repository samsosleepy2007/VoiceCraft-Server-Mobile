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

    path.write_text(text, encoding="utf-8")
    print(f"Applied UI5 polish to {path}")
    print("- removed decorative Render Relay connected badge")


if __name__ == "__main__":
    main()
