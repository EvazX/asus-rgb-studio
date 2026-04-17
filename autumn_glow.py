import argparse
import math
import signal
import time

from ambient_bar import AuraBarController, HID_PATH, HidApi
from effects_common import apply_intensity_to_frame, blend, clamp_channel


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Autumn glow effect for the ASUS G513QY light bar.")
    parser.add_argument("--speed", type=float, default=0.16, help="Seconds per frame. Default: 0.16.")
    return parser.parse_args()


def make_frame(step: int) -> list[tuple[int, int, int]]:
    amber = (255, 140, 35)
    copper = (185, 88, 30)
    maroon = (95, 20, 22)
    gold = (255, 190, 70)

    breathe = (math.sin(step * 0.18) + 1.0) / 2.0
    left = blend(copper, amber, breathe)
    mid_left = blend(maroon, copper, 0.55 + breathe * 0.2)
    mid_right = blend(copper, gold, 0.35 + breathe * 0.25)
    right = blend(maroon, amber, 0.25 + breathe * 0.35)

    return apply_intensity_to_frame([
        left,
        mid_left,
        mid_right,
        (
            clamp_channel(right[0] * 0.95),
            clamp_channel(right[1] * 0.9),
            clamp_channel(right[2] * 0.85),
        ),
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
        print("Autumn glow active. Press Ctrl+C to stop.")
        step = 0
        while not stop:
            controller.apply_colors(make_frame(step))
            time.sleep(max(0.04, args.speed))
            step += 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
