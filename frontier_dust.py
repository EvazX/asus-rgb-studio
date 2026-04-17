import argparse
import math
import signal
import time

from ambient_bar import AuraBarController, HID_PATH, HidApi
from effects_common import blend


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Red Dead Redemption 2 inspired frontier dust effect.")
    parser.add_argument("--speed", type=float, default=0.17, help="Seconds per frame. Default: 0.17.")
    return parser.parse_args()


def make_frame(step: int) -> list[tuple[int, int, int]]:
    dust = (160, 95, 50)
    ember = (210, 55, 22)
    sand = (215, 165, 100)
    dusk = (80, 24, 18)
    breathe = (math.sin(step * 0.19) + 1.0) / 2.0
    return [
        blend(dusk, dust, 0.4 + breathe * 0.22),
        blend(dust, sand, 0.2 + breathe * 0.35),
        blend(ember, dust, 0.45 + breathe * 0.15),
        blend(dusk, ember, 0.28 + breathe * 0.25),
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
        print("Frontier dust active. Press Ctrl+C to stop.")
        step = 0
        while not stop:
            controller.apply_colors(make_frame(step))
            time.sleep(max(0.05, args.speed))
            step += 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
