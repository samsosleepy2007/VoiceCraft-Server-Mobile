#!/usr/bin/env python3
from pathlib import Path
import re
import shutil
import sys

PATCH_ROOT = Path(__file__).resolve().parent

def main():
    if len(sys.argv) != 2:
        print(
            "Usage: python apply_guest_account_patch.py "
            "<VoiceCraft-Server-Mobile repo root>")
        raise SystemExit(2)

    repo = Path(sys.argv[1]).resolve()
    android = repo / "VoiceCraft.Server.Android"

    if not android.is_dir():
        raise SystemExit(
            f"Android project not found: {android}")

    patch_android = PATCH_ROOT / "VoiceCraft.Server.Android"

    for src in patch_android.rglob("*"):
        if not src.is_file():
            continue

        rel = src.relative_to(patch_android)

        if rel.as_posix() == \
                "Properties/AndroidManifest.guest-snippet.xml":
            continue

        dst = android / rel
        dst.parent.mkdir(
            parents=True,
            exist_ok=True)

        shutil.copy2(src, dst)
        print("copied", dst.relative_to(repo))

    # Exactly one launcher: AccountGateActivity.
    for path in android.glob("*.cs"):
        if path.name == "AccountGateActivity.cs":
            continue

        text = path.read_text(encoding="utf-8")
        changed = text.replace(
            "MainLauncher = true",
            "MainLauncher = false")

        if changed != text:
            path.write_text(
                changed,
                encoding="utf-8")
            print(
                "disabled old launcher",
                path.relative_to(repo))

    modern = android / "ModernMainActivity.cs"

    if modern.exists():
        text = modern.read_text(encoding="utf-8")

        marker = \
            'AddHeaderTool(tools, "INFO", ShowInformation);'

        account_line = \
            'AddHeaderTool(tools, T("บัญชี", "ACCOUNT"), OpenAccountCenter);'

        if account_line not in text \
                and marker in text:
            text = text.replace(
                marker,
                marker + "\n        " + account_line)

        method_marker = \
            "    private void ShowInformation()"

        method = '''    private void OpenAccountCenter()
    {
        StartActivity(
            new Intent(
                this,
                typeof(AccountStatusActivity)));
    }

'''

        if "private void OpenAccountCenter()" not in text \
                and method_marker in text:
            text = text.replace(
                method_marker,
                method + method_marker)

        modern.write_text(
            text,
            encoding="utf-8")

        print("patched", modern.relative_to(repo))

    manifest = \
        android / "Properties" / "AndroidManifest.xml"

    if manifest.exists():
        text = manifest.read_text(encoding="utf-8")

        if "<application" in text:
            attrs = {
                "android:allowBackup":
                    "false",
                "android:fullBackupContent":
                    "@xml/backup_rules",
                "android:dataExtractionRules":
                    "@xml/data_extraction_rules",
            }

            for attr, value in attrs.items():
                if attr in text:
                    text = re.sub(
                        rf'{re.escape(attr)}="[^"]*"',
                        f'{attr}="{value}"',
                        text,
                        count=1)
                else:
                    text = text.replace(
                        "<application",
                        f'<application {attr}="{value}"',
                        1)

            manifest.write_text(
                text,
                encoding="utf-8")

            print(
                "hardened",
                manifest.relative_to(repo))
    else:
        manifest.parent.mkdir(
            parents=True,
            exist_ok=True)

        manifest.write_text(
'''<?xml version="1.0" encoding="utf-8"?>
<manifest xmlns:android="http://schemas.android.com/apk/res/android">
  <application
      android:allowBackup="false"
      android:fullBackupContent="@xml/backup_rules"
      android:dataExtractionRules="@xml/data_extraction_rules" />
</manifest>
''',
            encoding="utf-8")

        print(
            "created",
            manifest.relative_to(repo))

    csproj = \
        android / "VoiceCraft.Server.Android.csproj"

    if csproj.exists():
        text = csproj.read_text(encoding="utf-8")

        text = re.sub(
            r"<ApplicationVersion>\d+</ApplicationVersion>",
            "<ApplicationVersion>11</ApplicationVersion>",
            text)

        text = re.sub(
            r"<ApplicationDisplayVersion>[^<]+"
            r"</ApplicationDisplayVersion>",
            "<ApplicationDisplayVersion>"
            "1.7.1-android-phase2-ui4.5-account-v2-guest"
            "</ApplicationDisplayVersion>",
            text)

        csproj.write_text(
            text,
            encoding="utf-8")

        print(
            "versioned",
            csproj.relative_to(repo))

    old_version = re.compile(
        r"1\.7\.1-android-phase2-ui4\.[0-9]+"
        r"(?:-[A-Za-z0-9._-]+)?")

    for path in android.glob("*.cs"):
        text = path.read_text(encoding="utf-8")
        changed = old_version.sub(
            "1.7.1-android-phase2-ui4.5-account-v2-guest",
            text)

        if changed != text:
            path.write_text(
                changed,
                encoding="utf-8")

    print()
    print("Guest Account patch applied.")
    print("Build and verify:")
    print("  1) first launch shows LOGIN / CREATE FREE ACCOUNT")
    print("  2) guest survives app restart/update")
    print("  3) Clear App Data creates a different Guest ID")
    print("  4) uninstall/reinstall creates a different Guest ID")
    print("  5) no guest row exists in VoiceCraft account tables")

if __name__ == "__main__":
    main()
