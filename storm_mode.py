import argparse
import random
import signal
import time

from ambient_bar import AuraBarController, HID_PATH, HidApi
from effects_common import blend


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Storm mode effect for the ASUS G513QY light bar.")
    parser.add_argument("--speed", type=float, default=0.16, help="Seconds per frame. Default: 0.16.")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    stop = False
    base_left = (20, 40, 110)
    base_right = (40, 80, 170)

    def handle_stop(_signum, _frame) -> None:
        nonlocal stop
        stop = True

    signal.signal(signal.SIGINT, handle_stop)
    signal.signal(signal.SIGTERM, handle_stop)

    hid_api = HidApi()
    with AuraBarController(hid_api, HID_PATH) as controller:
        print("Storm mode active. Press Ctrl+C to stop.")
        while not stop:
            if random.random() < 0.18:
                flash_strength = random.uniform(0.65, 1.0)
                flash = blend((255, 255, 255), (120, 170, 255), 1.0 - flash_strength)
                if random.random() < 0.5:
                    frame = [flash, flash, base_right, base_right]
                else:
                    frame = [base_left, base_left, flash, flash]
            else:
                frame = [base_left, blend(base_left, base_right, 0.35), blend(base_right, base_left, 0.2), base_right]

            controller.apply_colors(frame)
            time.sleep(max(0.04, args.speed))

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
