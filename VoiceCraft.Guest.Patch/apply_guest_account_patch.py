#!/usr/bin/env python3
from pathlib import Path
import re
import shutil
import sys

PATCH_ROOT = Path(__file__).resolve().parent


def sub_required(text, pattern, replacement, label):
    changed, count = re.subn(pattern, replacement, text, count=1, flags=re.S)
    if count != 1:
        raise RuntimeError(f"Could not patch ModernMainActivity: {label}")
    return changed


def patch_modern_main(text):
    text = text.replace(
        "shell.AddView(BuildHeader(), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(112)));",
        "shell.AddView(BuildHeader(), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(88)));",
        1)

    header = '''    private View BuildHeader()
    {
        var outer = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Background = Solid(Page)
        };
        outer.SetPadding(Dp(14), Dp(8), Dp(14), Dp(4));

        var card = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            Background = Round(Surface, 20, Border)
        };
        card.SetGravity(GravityFlags.CenterVertical);
        card.Elevation = Dp(2);
        card.SetPadding(Dp(12), Dp(8), Dp(12), Dp(8));

        var logo = new ImageView(this);
        logo.SetImageResource(Resource.Drawable.voicecraft_logo);
        logo.SetAdjustViewBounds(true);
        logo.SetScaleType(ImageView.ScaleType.FitCenter);
        card.AddView(logo, new LinearLayout.LayoutParams(Dp(46), Dp(46)));

        var title = new LinearLayout(this) { Orientation = Orientation.Vertical };
        title.SetPadding(Dp(10), 0, Dp(8), 0);
        title.AddView(Label("VoiceCraft Server", 18, Ink, true));
        title.AddView(Label(T("เซิร์ฟเวอร์เสียง VoiceCraft บน Android", "VoiceCraft voice server on Android"), 11, Muted));
        card.AddView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        var version = Pill("1.7.1", Tint, Primary, true);
        card.AddView(version, new LinearLayout.LayoutParams(Dp(62), Dp(34)));

        outer.AddView(card, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        return outer;
    }

'''
    text = sub_required(
        text,
        r"    private View BuildHeader\(\)\n    \{.*?\n    \}\n\n    private void AddHeaderTool\(.*?\n    \}\n\n(?=    private View BuildNav\(\))",
        header,
        "simplified header")

    text = text.replace('AddNav(nav, T("หน้าหลัก", "HOME"), 0);', 'AddNav(nav, T("ภาพรวม", "HOME"), 0);', 1)
    text = text.replace('AddNav(nav, T("บริดจ์", "BRIDGE"), 1);', 'AddNav(nav, T("Relay", "RELAY"), 1);', 1)
    text = text.replace('AddNav(nav, T("ล็อก", "LOGS"), 2);', 'AddNav(nav, T("บันทึก", "LOGS"), 2);', 1)

    text = text.replace(
        '            T("สวัสดี 👋", "Hello 👋"),\n            T("เช็กสถานะและควบคุม VoiceCraft Server ได้จากที่นี่", "Check status and control VoiceCraft Server from here"));',
        '            T("ภาพรวมเซิร์ฟเวอร์", "Server Overview"),\n            T("ดูสถานะ ผู้เล่น การเชื่อมต่อ และเริ่มหรือหยุดเซิร์ฟเวอร์จากแถบด้านล่าง", "See status, players and connections; start or stop the server from the bottom bar"));',
        1)

    text = text.replace(
        '''        var quickButtons = ButtonRow();
        AddButton(quickButtons, T("คัดลอก IP", "COPY IP"), CopyIp);
        AddButton(quickButtons, T("คัดลอก Port", "COPY PORT"), CopyPort);
        AddButton(quickButtons, T("คัดลอกทั้งหมด", "COPY ADDRESS"), CopyAddress, primary: true);
        quick.AddView(quickButtons);''',
        '''        var quickButtons = ButtonRow();
        AddButton(quickButtons, T("คัดลอก IP", "COPY IP"), CopyIp);
        AddButton(quickButtons, T("คัดลอก IP:Port", "COPY IP:PORT"), CopyAddress, primary: true);
        quick.AddView(quickButtons);''',
        1)

    text = text.replace(
        '''        var bridgeButtons = ButtonRow();
        AddButton(bridgeButtons, T("ตั้งค่า", "SET UP"), () => ShowPage(1), primary: true);
        AddButton(bridgeButtons, T("คัดลอก WSS", "COPY WSS"), CopyWebSocket);
        AddButton(bridgeButtons, "PLUGIN CONFIG", CopyPluginConfig);
        bridge.AddView(bridgeButtons);''',
        '''        var bridgeButtons = ButtonRow();
        AddButton(bridgeButtons, T("เปิดการตั้งค่า Relay", "OPEN RELAY SETTINGS"), () => ShowPage(1), primary: true);
        AddButton(bridgeButtons, T("คัดลอก WSS", "COPY WSS"), CopyWebSocket);
        bridge.AddView(bridgeButtons);''',
        1)

    text = sub_required(
        text,
        r'''\n        var control = Card\(Tint, Border\);.*?\n        body\.AddView\(control, CardLayout\(\)\);''',
        '',
        "remove duplicate Home server controls")

    bridge_method = '''    private ScrollView BuildBridge()
    {
        var (scroll, body) = NewPage(
            T("Relay และ Bridge", "Relay & Bridge"),
            T("ตั้งค่าการเชื่อมต่อ Render แบบเป็นขั้นตอน โดย Primary จำเป็นและ Backup เป็นตัวเลือก", "Configure the Render connection step by step. Primary is required; backups are optional"));
        _bridgeScroll = scroll;

        var required = Card(WarningFill, Amber);
        required.AddView(SectionTitle(T("ก่อนเริ่มเซิร์ฟเวอร์", "Before you start"), Amber));
        required.AddView(Label(T("กรอก Render URL, Server ID และ Bridge Secret ให้ครบ ช่องที่ผิดจะมีกรอบสีแดง", "Complete Render URL, Server ID and Bridge Secret. Invalid fields are highlighted red"), 12, Ink));
        body.AddView(required, CardLayout());

        var relay = Card();
        relay.AddView(SectionTitle(T("1. Render Relay", "1. Render Relay"), Primary));
        relay.AddView(Label(T("ใส่ลิงก์ Web Service ของ Render แอปจะสร้าง WebSocket ให้เอง", "Paste the Render Web Service URL and the app generates the WebSocket URL automatically"), 12, Muted));
        relay.AddView(InputLabel("Render Service URL"));
        _renderUrl = Input(ToServiceUrl(ServerPreferences.GetBridgeUrl(this)), InputTypes.ClassText | InputTypes.TextVariationUri);
        _renderUrl.Hint = "https://voicecraft-server-mobile.onrender.com";
        _renderUrl.TextChanged += (_, _) => UpdateWebSocketFromRenderUrl();
        relay.AddView(_renderUrl);

        var renderButtons = ButtonRow();
        AddButton(renderButtons, T("เปิด Render", "OPEN RENDER"), OpenRender, primary: true);
        AddButton(renderButtons, T("คัดลอก WSS", "COPY WSS"), CopyWebSocket);
        relay.AddView(renderButtons);

        relay.AddView(InputLabel(T("WebSocket URL • สร้างอัตโนมัติ", "WebSocket URL • generated automatically")));
        _webSocketUrl = ReadOnly(T("ใส่ Render URL ด้านบน", "Enter Render URL above"));
        relay.AddView(_webSocketUrl);

        var backupButtons = ButtonRow();
        AddButton(backupButtons, T("จัดการ Relay สำรอง", "MANAGE BACKUP RELAYS"), OpenBackupRelays);
        relay.AddView(backupButtons);
        relay.AddView(Label(T("Relay สำรองไม่บังคับ หากไม่ใส่ระบบจะใช้ Primary เพียงตัวเดียว", "Backup relays are optional. Primary works by itself"), 11, Muted));
        body.AddView(relay, CardLayout());

        var identity = Card();
        identity.AddView(SectionTitle(T("2. เซิร์ฟเวอร์และ Secret", "2. Server & Secret"), Primary2));
        identity.AddView(InputLabel("Server ID"));
        _serverId = Input(ServerPreferences.GetBridgeServerId(this), InputTypes.ClassText);
        _serverId.Hint = "mcsv-main";
        _serverId.TextChanged += (_, _) => RefreshBridgePreview();
        identity.AddView(_serverId);

        identity.AddView(InputLabel("Bridge Secret"));
        _bridgeSecret = Input(ServerPreferences.GetBridgeSecret(this), InputTypes.ClassText | InputTypes.TextVariationPassword);
        _bridgeSecret.Hint = T("ต้องตรงกับ BRIDGE_SECRET บน Render", "Must match BRIDGE_SECRET on Render");
        _bridgeSecret.TextChanged += (_, _) => RefreshBridgePreview();
        identity.AddView(_bridgeSecret);

        var secretButtons = ButtonRow();
        AddButton(secretButtons, T("แสดง/ซ่อน Secret", "SHOW / HIDE SECRET"), ToggleBridgeSecret);
        AddButton(secretButtons, T("สร้าง Secret ใหม่", "GENERATE SECRET"), GenerateBridgeSecret);
        AddButton(secretButtons, T("คัดลอก Secret", "COPY SECRET"), CopyBridgeSecret, primary: true);
        identity.AddView(secretButtons);

        _readiness = Label(T("● ต้องตั้งค่าให้ครบก่อนเริ่ม", "● Setup required before start"), 13, Red, true);
        _readiness.SetPadding(0, Dp(14), 0, 0);
        identity.AddView(_readiness);
        body.AddView(identity, CardLayout());

        var config = Card(Tint2, Border);
        config.AddView(SectionTitle(T("3. Plugin Config", "3. Plugin Config"), Primary2));
        config.AddView(Label(T("Config ด้านล่างพร้อมนำไปวางใน Endstone โดย Secret จะแสดงแบบซ่อน", "This config is ready for Endstone; the on-screen secret stays masked"), 12, Muted));
        _configPreview = Label(string.Empty, 11, Ink);
        _configPreview.Typeface = Typeface.Monospace;
        _configPreview.SetTextIsSelectable(true);
        _configPreview.SetPadding(Dp(12), Dp(12), Dp(12), Dp(12));
        _configPreview.Background = Round(SurfaceSoft, 14, Border);
        config.AddView(_configPreview, Top(Dp(12)));
        var configButtons = ButtonRow();
        AddButton(configButtons, T("คัดลอก Config", "COPY CONFIG"), CopyPluginConfig, primary: true);
        AddButton(configButtons, T("คัดลอกข้อมูลตั้งค่าทั้งหมด", "COPY ALL SETUP"), CopyAllSetup);
        config.AddView(configButtons);
        body.AddView(config, CardLayout());

        var flow = Card();
        flow.AddView(SectionTitle(T("เส้นทางการเชื่อมต่อ", "Connection Flow"), Sky));
        flow.AddView(FlowStep("1", "Minecraft / MCSV", T("Endstone ติดตามผู้เล่น", "Endstone tracks players")));
        flow.AddView(FlowStep("2", "Render Relay", T("ส่งสถานะผ่าน WebSocket", "Forwards state over WebSocket")));
        flow.AddView(FlowStep("3", "Android Server", T("อัปเดตตำแหน่ง VoiceCraft", "Updates VoiceCraft positioning")));
        flow.AddView(Label(T("เสียงยังใช้ UDP ไปยัง Android โดยตรง", "Voice audio still uses UDP directly to Android"), 12, Muted));
        body.AddView(flow, CardLayout());
        return scroll;
    }

'''
    text = sub_required(
        text,
        r"    private ScrollView BuildBridge\(\)\n    \{.*?\n    \}\n\n(?=    private ScrollView BuildLogs\(\))",
        bridge_method,
        "Relay page")

    settings_method = '''    private ScrollView BuildSettings()
    {
        var (scroll, body) = NewPage(
            T("ตั้งค่า", "Settings"),
            T("รวมการตั้งค่าแอป บัญชี และเซิร์ฟเวอร์ไว้ในที่เดียว", "App, account and server options in one place"));
        _settingsScroll = scroll;

        var app = Card();
        app.AddView(SectionTitle(T("แอป", "App"), Primary));
        app.AddView(Label(T("ตัวเลือกที่เคยอยู่ด้านบนถูกย้ายมาไว้ที่นี่ เพื่อให้หน้าหลักดูสะอาดขึ้น", "Top-bar options live here now so the main screen stays clean"), 12, Muted));
        app.AddView(SettingsAction(
            T("ภาษา", "Language"),
            T(_thai ? "กำลังใช้ภาษาไทย" : "กำลังใช้ภาษาอังกฤษ", _thai ? "Thai is active" : "English is active"),
            _thai ? "ENGLISH" : "ไทย",
            ToggleLanguage), Top(Dp(10)));
        app.AddView(SettingsAction(
            T("ธีม", "Theme"),
            T(_dark ? "กำลังใช้ธีมมืด" : "กำลังใช้ธีมสว่าง", _dark ? "Dark theme is active" : "Light theme is active"),
            _dark ? T("สว่าง", "LIGHT") : T("มืด", "DARK"),
            ToggleTheme), Top(Dp(8)));
        app.AddView(SettingsAction(
            T("คู่มือและข้อมูล", "Help & Information"),
            T("ดูขั้นตอนติดตั้ง Render, Endstone และ VoiceCraft Client", "Setup guide for Render, Endstone and VoiceCraft Client"),
            T("เปิด", "OPEN"),
            ShowInformation), Top(Dp(8)));
        app.AddView(SettingsAction(
            T("บัญชี", "Account"),
            T("ดูบัญชีที่ใช้งาน สลับ Free Account หรือออกจากระบบ", "View the active account, switch to Free Account, or log out"),
            T("จัดการ", "MANAGE"),
            OpenAccountCenter), Top(Dp(8)));
        body.AddView(app, CardLayout());

        var server = Card();
        server.AddView(SectionTitle(T("เซิร์ฟเวอร์เสียง", "Voice Server"), Primary));
        server.AddView(Label(T("ปกติไม่ต้องเปลี่ยน Port หากไม่มีโปรแกรมอื่นใช้งานเลขเดียวกัน", "Usually you can keep the default port unless another app already uses it"), 12, Muted));
        server.AddView(InputLabel("Voice / McHttp Port"));
        _port = Input(ServerPreferences.GetVoicePort(this).ToString(), InputTypes.ClassNumber);
        _port.TextChanged += (_, _) => ApplyValidationHighlights();
        server.AddView(_port);
        var portButtons = ButtonRow();
        AddButton(portButtons, T("คัดลอก Port", "COPY PORT"), CopyPort);
        AddButton(portButtons, T("คัดลอก IP:Port", "COPY IP:PORT"), CopyAddress, primary: true);
        server.AddView(portButtons);
        body.AddView(server, CardLayout());

        var security = Card();
        security.AddView(SectionTitle(T("Legacy McHttp Server Key", "Legacy McHttp Server Key"), Primary2));
        security.AddView(Label(T("ค่านี้เป็นคนละส่วนกับ Bridge Secret เปลี่ยนเฉพาะเมื่อจำเป็น", "This is separate from Bridge Secret. Change it only when needed"), 12, Muted));
        _serverKey = Input(ServerPreferences.GetServerKey(this), InputTypes.ClassText | InputTypes.TextVariationPassword);
        security.AddView(_serverKey, Top(Dp(10)));
        var keyButtons = ButtonRow();
        AddButton(keyButtons, T("แสดง/ซ่อน Key", "SHOW / HIDE KEY"), ToggleServerKey);
        AddButton(keyButtons, T("สร้าง Key ใหม่", "GENERATE KEY"), GenerateServerKey);
        AddButton(keyButtons, T("คัดลอก Key", "COPY KEY"), CopyServerKey, primary: true);
        security.AddView(keyButtons);
        body.AddView(security, CardLayout());

        var save = Card(SuccessFill, Green);
        save.AddView(SectionTitle(T("บันทึก", "Save"), Green));
        save.AddView(Label(T("การเปลี่ยนภาษาและธีมบันทึกทันที ส่วน Port และ Key ให้กดปุ่มด้านล่าง", "Language and theme save instantly. Use the button below after changing Port or Key"), 12, Muted));
        var saveButton = MakeButton(T("บันทึกการตั้งค่าเซิร์ฟเวอร์", "SAVE SERVER SETTINGS"), primary: true);
        WireButton(saveButton, () => SavePreferences(true));
        save.AddView(saveButton, Top(Dp(12)));
        body.AddView(save, CardLayout());
        return scroll;
    }

    private View SettingsAction(string title, string subtitle, string actionText, Action action)
    {
        var row = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            Background = Round(SurfaceSoft, 16, Border)
        };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(Dp(14), Dp(11), Dp(10), Dp(11));

        var copy = new LinearLayout(this) { Orientation = Orientation.Vertical };
        copy.AddView(Label(title, 14, Ink, true));
        var detail = Label(subtitle, 11, Muted);
        detail.SetPadding(0, Dp(2), Dp(8), 0);
        copy.AddView(detail);
        row.AddView(copy, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        var button = MakeButton(actionText, compact: true);
        WireButton(button, action);
        row.AddView(button, new LinearLayout.LayoutParams(Dp(108), Dp(44)));
        return row;
    }

'''
    text = sub_required(
        text,
        r"    private ScrollView BuildSettings\(\)\n    \{.*?\n    \}\n\n(?=    private \(ScrollView Scroll, LinearLayout Body\) NewPage)",
        settings_method,
        "Settings page")

    text = text.replace(
        'body.SetPadding(Dp(18), Dp(18), Dp(18), Dp(34));',
        'body.SetPadding(Dp(16), Dp(20), Dp(16), Dp(42));',
        1)
    text = text.replace(
        'sub.SetPadding(0, Dp(2), 0, Dp(16));',
        'sub.SetPadding(0, Dp(3), 0, Dp(20));',
        1)
    text = text.replace(
        'card.SetPadding(Dp(16), Dp(16), Dp(16), Dp(16));',
        'card.SetPadding(Dp(18), Dp(18), Dp(18), Dp(18));',
        1)

    add_button = '''    private void AddButton(LinearLayout row, string text, Action action, bool primary = false)
    {
        var button = MakeButton(text, primary);
        WireButton(button, action);

        if (row.Orientation == Orientation.Horizontal && row.ChildCount >= 2)
        {
            row.Orientation = Orientation.Vertical;
            for (var i = 0; i < row.ChildCount; i++)
            {
                var child = row.GetChildAt(i);
                if (child == null)
                    continue;
                child.LayoutParameters = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    Dp(50))
                {
                    TopMargin = Dp(6)
                };
            }
        }

        if (row.Orientation == Orientation.Vertical)
        {
            row.AddView(button, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                Dp(50))
            {
                TopMargin = Dp(6)
            });
        }
        else
        {
            row.AddView(button, new LinearLayout.LayoutParams(0, Dp(48), 1f)
            {
                LeftMargin = Dp(3),
                RightMargin = Dp(3)
            });
        }
    }

'''
    text = sub_required(
        text,
        r"    private void AddButton\(LinearLayout row, string text, Action action, bool primary = false\)\n    \{.*?\n    \}\n\n",
        add_button,
        "adaptive action buttons")

    text = text.replace(
        'private LinearLayout.LayoutParams Weight(int height = -2) => new(0, height, 1f);',
        'private LinearLayout.LayoutParams Weight(int height = -2) => new(0, height, 1f) { LeftMargin = Dp(3), RightMargin = Dp(3) };',
        1)
    text = text.replace(
        'private LinearLayout.LayoutParams CardLayout() => new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { BottomMargin = Dp(14) };',
        'private LinearLayout.LayoutParams CardLayout() => new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { BottomMargin = Dp(18) };',
        1)

    method_marker = "    private void ShowInformation()"
    method = '''    private void OpenAccountCenter()
    {
        SaveCurrentConfiguration();
        StartActivity(
            new Intent(
                this,
                typeof(AccountStatusActivity)));
    }

'''
    if "private void OpenAccountCenter()" not in text:
        if method_marker not in text:
            raise RuntimeError("Could not patch ModernMainActivity: account method marker")
        text = text.replace(method_marker, method + method_marker, 1)

    return text


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
        text = patch_modern_main(text)
        modern.write_text(text, encoding="utf-8")
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
    print("  6) main header is clean; language/theme/info/account live in Settings")

if __name__ == "__main__":
    main()
