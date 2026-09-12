#!/usr/bin/env python3
from __future__ import annotations

from pathlib import Path
import sys


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count == 0:
        if new in text:
            return text
        raise RuntimeError(f"Item Mic patch anchor missing: {label}")
    if count != 1:
        raise RuntimeError(f"Item Mic patch anchor not unique ({count}): {label}")
    return text.replace(old, new, 1)


def main() -> None:
    root = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
    path = root / "VoiceCraft.Server.Android" / "EndstoneBridgeController.cs"
    text = path.read_text(encoding="utf-8")

    text = replace_once(
        text,
        "        float Z,\n        float Yaw,\n        float Pitch)\n",
        "        float Z,\n        float Yaw,\n        float Pitch,\n        bool? MicOn)\n",
        "BridgePlayerState MicOn field",
    )

    text = replace_once(
        text,
        "        entity.Rotation = new Vector2(state.Pitch, state.Yaw);\n",
        "        entity.Rotation = new Vector2(state.Pitch, state.Yaw);\n"
        "\n"
        "        // Item Mic is transported as an optional extension on the existing\n"
        "        // protocol-1 player_state message. Older Endstone plugins simply omit\n"
        "        // the field and retain normal VoiceCraft mute behaviour.\n"
        "        if (state.MicOn.HasValue)\n"
        "        {\n"
        "            var serverMuted = !state.MicOn.Value;\n"
        "            if (entity.ServerMuted != serverMuted)\n"
        "            {\n"
        "                entity.ServerMuted = serverMuted;\n"
        "                AndroidRuntimeLog.Append(\n"
        "                    \"BRIDGE\",\n"
        "                    $\"ITEM MIC player={state.Name} entity={entity.Id} mic={(state.MicOn.Value ? \"ON\" : \"OFF\")}\");\n"
        "            }\n"
        "        }\n",
        "apply Item Mic ServerMuted state",
    )

    text = replace_once(
        text,
        "            state = new BridgePlayerState(name, xuid, uuid, dimension, x, y, z, yaw, pitch);\n",
        "            bool? micOn = null;\n"
        "            if (root.TryGetProperty(\"micOn\", out var micElement) &&\n"
        "                micElement.ValueKind is JsonValueKind.True or JsonValueKind.False)\n"
        "                micOn = micElement.GetBoolean();\n"
        "\n"
        "            state = new BridgePlayerState(name, xuid, uuid, dimension, x, y, z, yaw, pitch, micOn);\n",
        "parse Item Mic player_state extension",
    )

    path.write_text(text, encoding="utf-8")

    # Fail loudly if future refactors partially break the patch. These guards are
    # intentionally semantic strings rather than line-number assumptions.
    final = path.read_text(encoding="utf-8")
    required = [
        "bool? MicOn",
        "state.MicOn.HasValue",
        "entity.ServerMuted = serverMuted",
        'root.TryGetProperty("micOn"',
        "new BridgePlayerState(name, xuid, uuid, dimension, x, y, z, yaw, pitch, micOn)",
    ]
    missing = [value for value in required if value not in final]
    if missing:
        raise RuntimeError(f"Item Mic bridge patch validation failed: {missing}")

    print(f"Applied VoiceCraft Item Mic bridge patch to {path}")


if __name__ == "__main__":
    main()
