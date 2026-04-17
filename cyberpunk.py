import argparse
import math
import signal
import time

from ambient_bar import AuraBarController, HID_PATH, HidApi
from effects_common import apply_intensity_to_frame, blend, clamp_channel


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Cyberpunk effect for the ASUS G513QY light bar.")
    parser.add_argument("--speed", type=float, default=0.12, help="Seconds per frame. Default: 0.12.")
    return parser.parse_args()


def neon_shift(step: int, phase: float) -> tuple[int, int, int]:
    wave = (math.sin(step * 0.32 + phase) + 1.0) / 2.0
    cyan = (0, 255, 235)
    magenta = (255, 0, 180)
    violet = (130, 40, 255)
    base = blend(cyan, magenta, wave)
    return blend(base, violet, 0.28)


def make_frame(step: int) -> list[tuple[int, int, int]]:
    left = neon_shift(step, 0.0)
    mid_left = neon_shift(step, 0.7)
    mid_right = neon_shift(step, 1.4)
    right = neon_shift(step, 2.1)

    if step % 12 == 0:
        flash = (255, 255, 255)
        return apply_intensity_to_frame([
            blend(left, flash, 0.35),
            blend(mid_left, flash, 0.2),
            blend(mid_right, flash, 0.2),
            blend(right, flash, 0.35),
        ])

    return apply_intensity_to_frame([
        left,
        (
            clamp_channel(mid_left[0] * 0.95),
            clamp_channel(mid_left[1] * 0.9),
            clamp_channel(mid_left[2] * 1.0),
        ),
        (
            clamp_channel(mid_right[0] * 1.0),
            clamp_channel(mid_right[1] * 0.9),
            clamp_channel(mid_right[2] * 0.95),
        ),
        right,
    ])


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
        print("Cyberpunk active. Press Ctrl+C to stop.")
        step = 0
        while not stop:
            controller.apply_colors(make_frame(step))
            time.sleep(max(0.04, args.speed))
            step += 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
