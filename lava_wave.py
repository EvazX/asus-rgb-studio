import argparse
import math
import signal
import time

from ambient_bar import AuraBarController, HID_PATH, HidApi
from effects_common import apply_intensity_to_frame, blend


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Lava wave effect.")
    parser.add_argument("--speed", type=float, default=0.12, help="Seconds per frame. Default: 0.12.")
    return parser.parse_args()


def lava_color(step: int, phase: float) -> tuple[int, int, int]:
    wave = (math.sin(step * 0.28 + phase) + 1.0) / 2.0
    ember = (255, 70, 30)
    amber = (255, 170, 30)
    white_hot = (255, 245, 180)
    return blend(blend(ember, amber, wave), white_hot, 0.18 + wave * 0.16)


def frame(step: int) -> list[tuple[int, int, int]]:
    return apply_intensity_to_frame([lava_color(step, phase) for phase in (0.0, 0.7, 1.4, 2.1)])


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
        step = 0
        while not stop:
            controller.apply_colors(frame(step))
            time.sleep(max(0.04, args.speed))
            step += 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
