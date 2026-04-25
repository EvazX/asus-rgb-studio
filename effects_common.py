from __future__ import annotations

import os
from typing import Iterable

Color = tuple[int, int, int]
STATE_FILE = os.environ.get("RGB_STATE_FILE", r"D:\asus-ambient-led\rgb_intensity.txt")
ZONE_COUNT = 4


def current_intensity() -> float:
    try:
        if os.path.exists(STATE_FILE):
            with open(STATE_FILE, "r", encoding="utf-8") as handle:
                return max(0.0, min(1.0, float(handle.read().strip() or "1.0")))
        return max(0.0, min(1.0, float(os.environ.get("RGB_INTENSITY", "1.0"))))
    except ValueError:
        return 1.0


def clamp_channel(value: float) -> int:
    return max(0, min(255, int(value)))


def scale(color: Color, factor: float) -> Color:
    return (
        clamp_channel(color[0] * factor),
        clamp_channel(color[1] * factor),
        clamp_channel(color[2] * factor),
    )


def apply_intensity(color: Color) -> Color:
    return scale(color, current_intensity())


def apply_intensity_to_frame(colors: Iterable[Color]) -> list[Color]:
    intensity = current_intensity()
    return [scale(color, intensity) for color in colors]


def blend(left: Color, right: Color, ratio: float) -> Color:
    r = max(0.0, min(1.0, ratio))
    return (
        clamp_channel(left[0] + (right[0] - left[0]) * r),
        clamp_channel(left[1] + (right[1] - left[1]) * r),
        clamp_channel(left[2] + (right[2] - left[2]) * r),
    )


def mirror_positions() -> list[int]:
    return list(range(ZONE_COUNT)) + list(range(ZONE_COUNT - 2, 0, -1))


def expand_cycle(colors: Iterable[Color]) -> list[Color]:
    palette = list(colors)
    if not palette:
        return [(0, 0, 0)]
    return palette
