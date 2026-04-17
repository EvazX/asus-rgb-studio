import argparse
import math
import signal
import time

from ambient_bar import AuraBarController, HID_PATH, HidApi
from effects_common import apply_intensity_to_frame, blend


PALETTE = [
    (255, 70, 70),
    (255, 180, 60),
    (255, 235, 90),
    (70, 220, 120),
    (70, 180, 255),
    (130, 100, 255),
]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Prism flow effect.")
    parser.add_argument("--speed", type=float, default=0.11, help="Seconds per frame. Default: 0.11.")
    return parser.parse_args()


def prism_color(step: int, phase: float) -> tuple[int, int, int]:
    pos = (step * 0.25 + phase) % len(PALETTE)
    left = int(math.floor(pos)) % len(PALETTE)
    right = (left + 1) % len(PALETTE)
    ratio = pos - math.floor(pos)
    return blend(PALETTE[left], PALETTE[right], ratio)


def frame(step: int) -> list[tuple[int, int, int]]:
    return apply_intensity_to_frame([prism_color(step, phase) for phase in (0.0, 0.8, 1.6, 2.4)])


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
            controller.apply_colors(frame(step))
            time.sleep(max(0.04, args.speed))
            step += 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
