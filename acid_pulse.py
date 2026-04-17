import argparse
import math
import signal
import time

from ambient_bar import AuraBarController, HID_PATH, HidApi
from effects_common import clamp_channel


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Acid pulse effect for the ASUS G513QY light bar.")
    parser.add_argument("--speed", type=float, default=0.12, help="Seconds per frame. Default: 0.12.")
    return parser.parse_args()


def make_frame(step: int) -> list[tuple[int, int, int]]:
    phases = [0.0, 0.6, 1.2, 1.8]
    colors = []
    for phase in phases:
        wave = (math.sin(step * 0.42 + phase) + 1.0) / 2.0
        red = clamp_channel(40 + wave * 70)
        green = clamp_channel(110 + wave * 145)
        blue = clamp_channel(10 + wave * 45)
        colors.append((red, green, blue))
    return colors


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
        print("Acid pulse active. Press Ctrl+C to stop.")
        step = 0
        while not stop:
            controller.apply_colors(make_frame(step))
            time.sleep(max(0.04, args.speed))
            step += 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
