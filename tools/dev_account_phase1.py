from pathlib import Path

root = Path(__file__).resolve().parents[1]
modern = root / "VoiceCraft.Server.Android" / "ModernMainActivity.cs"
csproj = root / "VoiceCraft.Server.Android" / "VoiceCraft.Server.Android.csproj"

text = modern.read_text(encoding="utf-8")
old = '    MainLauncher = true,\n    Exported = true)]\npublic sealed class ModernMainActivity : Activity'
new = '    MainLauncher = false,\n    Exported = true)]\npublic sealed class ModernMainActivity : Activity'
if old not in text:
    raise SystemExit("ModernMainActivity launcher marker not found")
text = text.replace(old, new, 1)

old = '        AddHeaderTool(tools, "INFO", ShowInformation);\n'
new = '        AddHeaderTool(tools, "INFO", ShowInformation);\n        AddHeaderTool(tools, T("บัญชี", "ACCOUNT"), () => AccountActivity.LaunchManage(this));\n'
if old not in text:
    raise SystemExit("header tools marker not found")
text = text.replace(old, new, 1)
modern.write_text(text, encoding="utf-8")

p = csproj.read_text(encoding="utf-8")
p = p.replace(
    '<!-- UI4.4: multi-relay failover, real /vc unbind and in-game alerts; Endstone companion 0.2.6 -->',
    '<!-- UI4.5: Supabase account login + UI4.4 multi-relay/unbind runtime; Endstone companion 0.2.6 -->',
)
p = p.replace('<ApplicationVersion>10</ApplicationVersion>', '<ApplicationVersion>11</ApplicationVersion>')
p = p.replace(
    '<ApplicationDisplayVersion>1.7.1-android-phase2-ui4.4</ApplicationDisplayVersion>',
    '<ApplicationDisplayVersion>1.7.1-android-phase2-ui4.5</ApplicationDisplayVersion>',
)
if '<ApplicationVersion>11</ApplicationVersion>' not in p:
    raise SystemExit("csproj version bump failed")
csproj.write_text(p, encoding="utf-8")
