import argparse
import random
import signal
import time

from ambient_bar import AuraBarController, HID_PATH, HidApi
from effects_common import blend


BASE = [
    (35, 8, 4),
    (55, 16, 8),
    (42, 10, 5),
    (28, 6, 3),
]

SPARK = (255, 150, 40)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Ember rain effect for the ASUS G513QY light bar.")
    parser.add_argument("--speed", type=float, default=0.12, help="Seconds per frame. Default: 0.12.")
    return parser.parse_args()


def make_frame() -> list[tuple[int, int, int]]:
    frame = list(BASE)
    if random.random() < 0.45:
        idx = random.randrange(4)
        frame[idx] = blend(frame[idx], SPARK, random.uniform(0.55, 1.0))
    if random.random() < 0.18:
        idx = random.randrange(4)
        frame[idx] = blend(frame[idx], (255, 220, 120), random.uniform(0.35, 0.7))
    return frame


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
        print("Ember rain active. Press Ctrl+C to stop.")
        while not stop:
            controller.apply_colors(make_frame())
            time.sleep(max(0.04, args.speed))

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
