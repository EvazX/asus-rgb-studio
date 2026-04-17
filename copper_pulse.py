import argparse
import math
import signal
import time

from ambient_bar import AuraBarController, HID_PATH, HidApi
from effects_common import clamp_channel


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Copper pulse effect for the ASUS G513QY light bar.")
    parser.add_argument("--speed", type=float, default=0.11, help="Seconds per frame. Default: 0.11.")
    return parser.parse_args()


def zone_color(step: int, phase: float) -> tuple[int, int, int]:
    wave = (math.sin(step * 0.36 + phase) + 1.0) / 2.0
    return (
        clamp_channel(70 + wave * 165),
        clamp_channel(26 + wave * 84),
        clamp_channel(8 + wave * 30),
    )


def make_frame(step: int) -> list[tuple[int, int, int]]:
    return [
        zone_color(step, 0.0),
        zone_color(step, 0.6),
        zone_color(step, 1.2),
        zone_color(step, 1.8),
    ]


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
        print("Copper pulse active. Press Ctrl+C to stop.")
        step = 0
        while not stop:
            controller.apply_colors(make_frame(step))
            time.sleep(max(0.04, args.speed))
            step += 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
