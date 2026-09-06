from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True, slots=True)
class PlayerState:
    name: str
    xuid: str
    uuid: str
    dimension: str
    x: float
    y: float
    z: float
    yaw: float
    pitch: float

    def position_changed(self, other: "PlayerState", epsilon: float) -> bool:
        return (
            abs(self.x - other.x) >= epsilon
            or abs(self.y - other.y) >= epsilon
            or abs(self.z - other.z) >= epsilon
        )

    def rotation_changed(self, other: "PlayerState", epsilon: float) -> bool:
        return abs(self.yaw - other.yaw) >= epsilon or abs(self.pitch - other.pitch) >= epsilon

    def compact(self) -> str:
        return (
            f"{self.name} xuid={self.xuid} uuid={self.uuid} dim={self.dimension} "
            f"pos=({self.x:.2f},{self.y:.2f},{self.z:.2f}) "
            f"rot=({self.yaw:.1f},{self.pitch:.1f})"
        )
