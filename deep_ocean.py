import argparse
import math
import signal
import time

from ambient_bar import AuraBarController, HID_PATH, HidApi
from effects_common import apply_intensity_to_frame, blend


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Deep ocean effect.")
    parser.add_argument("--speed", type=float, default=0.16, help="Seconds per frame. Default: 0.16.")
    return parser.parse_args()


def ocean_color(step: int, phase: float) -> tuple[int, int, int]:
    wave = (math.sin(step * 0.16 + phase) + 1.0) / 2.0
    deep_blue = (20, 70, 180)
    aqua = (40, 180, 230)
    moon = (210, 240, 255)
    base = blend(deep_blue, aqua, wave)
    return blend(base, moon, 0.1 + wave * 0.12)


def frame(step: int) -> list[tuple[int, int, int]]:
    return apply_intensity_to_frame([ocean_color(step, phase) for phase in (0.0, 0.9, 1.8, 2.7)])


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
            time.sleep(max(0.05, args.speed))
            step += 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
