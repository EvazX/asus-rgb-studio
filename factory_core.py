import argparse
import math
import signal
import time

from ambient_bar import AuraBarController, HID_PATH, HidApi
from effects_common import blend


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Satisfactory inspired factory core effect.")
    parser.add_argument("--speed", type=float, default=0.12, help="Seconds per frame. Default: 0.12.")
    return parser.parse_args()


def make_frame(step: int) -> list[tuple[int, int, int]]:
    steel = (20, 40, 70)
    amber = (255, 145, 25)
    yellow = (255, 220, 60)
    pulse = (math.sin(step * 0.28) + 1.0) / 2.0
    return [
        blend(steel, amber, 0.25 + pulse * 0.2),
        blend(amber, yellow, 0.2 + pulse * 0.5),
        blend(amber, yellow, 0.1 + pulse * 0.35),
        blend(steel, amber, 0.18 + pulse * 0.16),
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
        print("Factory core active. Press Ctrl+C to stop.")
        step = 0
        while not stop:
            controller.apply_colors(make_frame(step))
            time.sleep(max(0.04, args.speed))
            step += 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
