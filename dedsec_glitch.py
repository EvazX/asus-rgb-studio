import argparse
import random
import signal
import time

from ambient_bar import AuraBarController, HID_PATH, HidApi
from effects_common import blend


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Watch Dogs Legion inspired DedSec glitch effect.")
    parser.add_argument("--speed", type=float, default=0.11, help="Seconds per frame. Default: 0.11.")
    return parser.parse_args()


def make_frame() -> list[tuple[int, int, int]]:
    cyan = (0, 255, 240)
    red = (255, 40, 40)
    black = (0, 0, 0)
    if random.random() < 0.22:
        return [red, black, cyan, black] if random.random() < 0.5 else [cyan, black, red, black]
    return [
        blend(cyan, red, 0.08),
        cyan,
        blend(red, cyan, 0.12),
        red,
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
        print("DedSec glitch active. Press Ctrl+C to stop.")
        while not stop:
            controller.apply_colors(make_frame())
            time.sleep(max(0.04, args.speed))

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
