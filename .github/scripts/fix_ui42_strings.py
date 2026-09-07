from pathlib import Path

path = Path("VoiceCraft.Server.Android/ModernMainActivity.cs")
text = path.read_text(encoding="utf-8")
old = '''                    return $"{player.Name}
{binding}
{player.Dimension} • {player.X:0.0}, {player.Y:0.0}, {player.Z:0.0}";
                });
                var suffix = dashboard.Players.Count > 20
                    ? T($"

และอีก {dashboard.Players.Count - 20} คน", $"

+ {dashboard.Players.Count - 20} more")
                    : string.Empty;
                _playerList.Text = string.Join("

", rows) + suffix;'''
new = '''                    return $"{player.Name}\\n{binding}\\n{player.Dimension} • {player.X:0.0}, {player.Y:0.0}, {player.Z:0.0}";
                });
                var suffix = dashboard.Players.Count > 20
                    ? T($"\\n\\nและอีก {dashboard.Players.Count - 20} คน", $"\\n\\n+ {dashboard.Players.Count - 20} more")
                    : string.Empty;
                _playerList.Text = string.Join("\\n\\n", rows) + suffix;'''
if old not in text:
    if new in text:
        print("UI4.2 strings already fixed")
    else:
        raise SystemExit("Expected malformed UI4.2 string block was not found")
else:
    path.write_text(text.replace(old, new, 1), encoding="utf-8")
    print("Fixed UI4.2 C# escaped newline strings")
