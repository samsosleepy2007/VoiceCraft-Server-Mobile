#!/usr/bin/env python3
from pathlib import Path
import sys


def main() -> None:
    root = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
    path = root / "VoiceCraft.Server.Android" / "ModernMainActivity.cs"
    text = path.read_text(encoding="utf-8")

    broken_split = "text.Split('" + "\n" + "');"
    broken_join = 'text = string.Join("' + "\n" + '", rows);'

    if broken_split in text:
        text = text.replace(
            broken_split,
            "text.Split(Environment.NewLine, StringSplitOptions.None);",
            1,
        )
    if broken_join in text:
        text = text.replace(
            broken_join,
            "text = string.Join(Environment.NewLine, rows);",
            1,
        )

    if broken_split in text or broken_join in text:
        raise RuntimeError("UI5 generated newline literals were not fully repaired")
    if "text.Split(Environment.NewLine, StringSplitOptions.None);" not in text:
        raise RuntimeError("UI5 split newline repair anchor missing")
    if "text = string.Join(Environment.NewLine, rows);" not in text:
        raise RuntimeError("UI5 join newline repair anchor missing")

    path.write_text(text, encoding="utf-8")
    print(f"Repaired UI5 generated newline literals in {path}")


if __name__ == "__main__":
    main()
