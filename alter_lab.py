import argparse
import math
import signal
import time

from ambient_bar import AuraBarController, HID_PATH, HidApi
from effects_common import blend


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="The Alters inspired laboratory effect.")
    parser.add_argument("--speed", type=float, default=0.2, help="Seconds per frame. Default: 0.2.")
    return parser.parse_args()


def make_frame(step: int) -> list[tuple[int, int, int]]:
    lab_white = (220, 235, 255)
    ice = (120, 180, 255)
    steel = (35, 55, 85)
    violet = (100, 85, 170)
    phase = (math.sin(step * 0.16) + 1.0) / 2.0
    return [
        blend(steel, ice, 0.25 + phase * 0.15),
        blend(ice, lab_white, 0.3 + phase * 0.3),
        blend(violet, lab_white, 0.18 + phase * 0.18),
        blend(steel, violet, 0.2 + phase * 0.22),
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
        print("Alter lab active. Press Ctrl+C to stop.")
        step = 0
        while not stop:
            controller.apply_colors(make_frame(step))
            time.sleep(max(0.05, args.speed))
            step += 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
