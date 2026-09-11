from pathlib import Path

root = Path(__file__).resolve().parents[1]
modern = root / "VoiceCraft.Server.Android" / "ModernMainActivity.cs"
controller = root / "VoiceCraft.Server.Android" / "EndstoneBridgeController.cs"
csproj = root / "VoiceCraft.Server.Android" / "VoiceCraft.Server.Android.csproj"

text = modern.read_text(encoding="utf-8")
if 'MainLauncher = true,\n    Exported = true)]\npublic sealed class ModernMainActivity : Activity' in text:
    text = text.replace(
        'MainLauncher = true,\n    Exported = true)]\npublic sealed class ModernMainActivity : Activity',
        'MainLauncher = false,\n    Exported = true)]\npublic sealed class ModernMainActivity : Activity',
        1,
    )
if 'AccountActivity.LaunchManage(this)' not in text:
    marker = '        AddHeaderTool(tools, "INFO", ShowInformation);\n'
    if marker not in text:
        raise SystemExit("header tools marker not found")
    text = text.replace(
        marker,
        marker + '        AddHeaderTool(tools, T("บัญชี", "ACCOUNT"), () => AccountActivity.LaunchManage(this));\n',
        1,
    )
text = text.replace('1.7.1-android-phase2-ui4.4', '1.7.1-android-phase2-ui4.5')
modern.write_text(text, encoding="utf-8")

c = controller.read_text(encoding="utf-8")
c = c.replace('1.7.1-android-phase2-ui4.4', '1.7.1-android-phase2-ui4.5')
controller.write_text(c, encoding="utf-8")

p = csproj.read_text(encoding="utf-8")
p = p.replace(
    '<!-- UI4.4: multi-relay failover, real /vc unbind and in-game alerts; Endstone companion 0.2.6 -->',
    '<!-- UI4.5: Supabase account login + UI4.4 multi-relay/unbind runtime; Endstone companion 0.2.6 -->',
)
p = p.replace('<ApplicationVersion>10</ApplicationVersion>', '<ApplicationVersion>11</ApplicationVersion>')
p = p.replace('1.7.1-android-phase2-ui4.4', '1.7.1-android-phase2-ui4.5')
if '<ApplicationVersion>11</ApplicationVersion>' not in p:
    raise SystemExit("csproj version bump failed")
csproj.write_text(p, encoding="utf-8")
