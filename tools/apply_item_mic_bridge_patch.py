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


def patch_android_bridge(root: Path) -> Path:
    path = root / "VoiceCraft.Server.Android" / "EndstoneBridgeController.cs"
    text = path.read_text(encoding="utf-8")

    text = replace_once(
        text,
        "    private const int PeerTimeoutSeconds = 30;\n",
        "    private const int PeerTimeoutSeconds = 30;\n"
        "    private const string ItemMicMutedProperty = \"voicecraft:item_mic_muted\";\n",
        "Item Mic property constant",
    )

    text = replace_once(
        text,
        "        entity.Rotation = Vector2.Zero;\n        entity.SetDescription($\"Welcome! Your binding key is {key}\");\n",
        "        entity.Rotation = Vector2.Zero;\n"
        "        // A VoiceCraft entity can be recycled for a later binding. Never\n"
        "        // allow a stale Item Mic mute state to leak into that next player.\n"
        "        entity.SetProperty(ItemMicMutedProperty, false);\n"
        "        entity.SetDescription($\"Welcome! Your binding key is {key}\");\n",
        "reset Item Mic state on unbound entity",
    )

    text = replace_once(
        text,
        "        entity.Rotation = new Vector2(state.Pitch, state.Yaw);\n",
        "        entity.Rotation = new Vector2(state.Pitch, state.Yaw);\n"
        "\n"
        "        // Item Mic travels as an optional protocol-1 player_state extension.\n"
        "        // Keep it independent from VoiceCraft's own client mute (Muted) and\n"
        "        // administrator/server mute (ServerMuted), so the item can never\n"
        "        // override either of those safety controls.\n"
        "        if (state.MicOn.HasValue)\n"
        "        {\n"
        "            var itemMicMuted = !state.MicOn.Value;\n"
        "            var changed =\n"
        "                !entity.TryGetProperty<bool>(ItemMicMutedProperty, out var currentItemMicMuted) ||\n"
        "                currentItemMicMuted != itemMicMuted;\n"
        "            entity.SetProperty(ItemMicMutedProperty, itemMicMuted);\n"
        "            if (changed)\n"
        "            {\n"
        "                AndroidRuntimeLog.Append(\n"
        "                    \"BRIDGE\",\n"
        "                    $\"ITEM MIC player={state.Name} entity={entity.Id} mic={(state.MicOn.Value ? \"ON\" : \"OFF\")}\");\n"
        "            }\n"
        "        }\n",
        "apply independent Item Mic state",
    )

    text = replace_once(
        text,
        "        float Z,\n        float Yaw,\n        float Pitch)\n",
        "        float Z,\n        float Yaw,\n        float Pitch,\n        bool? MicOn)\n",
        "BridgePlayerState MicOn field",
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

    final = path.read_text(encoding="utf-8")
    required = [
        'ItemMicMutedProperty = "voicecraft:item_mic_muted"',
        "bool? MicOn",
        "state.MicOn.HasValue",
        "entity.SetProperty(ItemMicMutedProperty, itemMicMuted)",
        "entity.SetProperty(ItemMicMutedProperty, false)",
        'root.TryGetProperty("micOn"',
        "new BridgePlayerState(name, xuid, uuid, dimension, x, y, z, yaw, pitch, micOn)",
    ]
    forbidden = ["entity.ServerMuted = serverMuted"]
    missing = [value for value in required if value not in final]
    present_forbidden = [value for value in forbidden if value in final]
    if missing or present_forbidden:
        raise RuntimeError(
            f"Item Mic bridge patch validation failed: missing={missing}, forbidden={present_forbidden}"
        )
    return path


def patch_voicecraft_audio_gate(root: Path) -> Path:
    path = (
        root
        / "VoiceCraft.Upstream"
        / "VoiceCraft.Network"
        / "Servers"
        / "VoiceCraftServer.cs"
    )
    text = path.read_text(encoding="utf-8")

    old = (
        "        if (networkEntity.Muted || networkEntity.ServerMuted) return;\n"
        "        networkEntity.ReceiveAudio(packet.Buffer, packet.Timestamp, packet.FrameLoudness);\n"
    )
    new = (
        "        // VoiceCraft Item Mic adds a third, independent server-side gate.\n"
        "        // It deliberately does not overwrite Muted or ServerMuted.\n"
        "        var itemMicMuted =\n"
        "            networkEntity.TryGetProperty<bool>(\"voicecraft:item_mic_muted\", out var itemMuted)\n"
        "            && itemMuted;\n"
        "        if (networkEntity.Muted || networkEntity.ServerMuted || itemMicMuted) return;\n"
        "        networkEntity.ReceiveAudio(packet.Buffer, packet.Timestamp, packet.FrameLoudness);\n"
    )
    text = replace_once(text, old, new, "VoiceCraft audio Item Mic gate")
    path.write_text(text, encoding="utf-8")

    final = path.read_text(encoding="utf-8")
    required = [
        'TryGetProperty<bool>("voicecraft:item_mic_muted"',
        "networkEntity.Muted || networkEntity.ServerMuted || itemMicMuted",
    ]
    missing = [value for value in required if value not in final]
    if missing:
        raise RuntimeError(f"VoiceCraft Item Mic audio gate validation failed: {missing}")
    return path


def main() -> None:
    root = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
    bridge = patch_android_bridge(root)
    audio = patch_voicecraft_audio_gate(root)
    print(f"Applied VoiceCraft Item Mic bridge patch to {bridge}")
    print(f"Applied VoiceCraft Item Mic audio gate patch to {audio}")


if __name__ == "__main__":
    main()
