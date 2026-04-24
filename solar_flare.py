import argparse
import math
import signal
import time

from ambient_bar import AuraBarController, HID_PATH, HidApi, ZONE_COUNT
from effects_common import apply_intensity_to_frame, clamp_channel


GOLD = (255, 206, 84)
ORANGE = (255, 104, 31)
RED = (185, 28, 28)
WHITE = (255, 246, 200)


def scale(color: tuple[int, int, int], factor: float) -> tuple[int, int, int]:
    return (
        clamp_channel(color[0] * factor),
        clamp_channel(color[1] * factor),
        clamp_channel(color[2] * factor),
    )


def blend(left: tuple[int, int, int], right: tuple[int, int, int], ratio: float) -> tuple[int, int, int]:
    r = max(0.0, min(1.0, ratio))
    return (
        clamp_channel(left[0] + (right[0] - left[0]) * r),
        clamp_channel(left[1] + (right[1] - left[1]) * r),
        clamp_channel(left[2] + (right[2] - left[2]) * r),
    )


def build_frame(step: int) -> list[tuple[int, int, int]]:
    frame: list[tuple[int, int, int]] = []
    flare = 1.0 if step % 24 in (0, 1, 2) else 0.0

    for index in range(ZONE_COUNT):
        wave = (math.sin(step * 0.36 + index * 0.9) + 1.0) / 2.0
        color = blend(RED, GOLD, wave)
        factor = 0.34 + wave * 0.58
        if flare and index in (1, 2):
            color = blend(color, WHITE, 0.72)
            factor = 1.0
        frame.append(scale(color, factor))

    if step % 31 == 0:
        frame[step % ZONE_COUNT] = ORANGE

    return apply_intensity_to_frame(frame)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Solar flare RGB effect.")
    parser.add_argument("--speed", type=float, default=0.08, help="Seconds per frame. Default: 0.08.")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    stop = False

    def handle_stop(_signum, _frame) -> None:
        nonlocal stop
        stop = True

    signal.signal(signal.SIGINT, handle_stop)
    signal.signal(signal.SIGTERM, handle_stop)

    hid_api = HidApi()
    with AuraBarController(hid_api, HID_PATH) as controller:
        step = 0
        while not stop:
            controller.apply_colors(build_frame(step))
            step += 1
            time.sleep(max(0.03, args.speed))

        controller.apply_colors([(0, 0, 0)] * ZONE_COUNT)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
