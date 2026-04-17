import argparse
import math
import signal
import time

from ambient_bar import AuraBarController, HID_PATH, HidApi
from effects_common import blend


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Death Stranding inspired drift effect.")
    parser.add_argument("--speed", type=float, default=0.18, help="Seconds per frame. Default: 0.18.")
    return parser.parse_args()


def make_frame(step: int) -> list[tuple[int, int, int]]:
    abyss = (4, 10, 26)
    cold_blue = (20, 70, 150)
    frost = (155, 220, 255)
    pulse = (math.sin(step * 0.17) + 1.0) / 2.0
    return [
        blend(abyss, cold_blue, 0.25 + pulse * 0.25),
        blend(cold_blue, frost, 0.18 + pulse * 0.28),
        blend(cold_blue, frost, 0.1 + pulse * 0.2),
        blend(abyss, cold_blue, 0.18 + pulse * 0.22),
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
        print("Death Stranding drift active. Press Ctrl+C to stop.")
        step = 0
        while not stop:
            controller.apply_colors(make_frame(step))
            time.sleep(max(0.05, args.speed))
            step += 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
