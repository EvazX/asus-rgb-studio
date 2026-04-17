import argparse
import math
import signal
import time

from ambient_bar import AuraBarController, HID_PATH, HidApi
from effects_common import apply_intensity_to_frame, blend


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Aurora drift effect.")
    parser.add_argument("--speed", type=float, default=0.14, help="Seconds per frame. Default: 0.14.")
    return parser.parse_args()


def aurora_color(step: int, phase: float) -> tuple[int, int, int]:
    wave = (math.sin(step * 0.18 + phase) + 1.0) / 2.0
    cyan = (70, 220, 255)
    mint = (80, 255, 170)
    violet = (150, 110, 255)
    base = blend(cyan, mint, wave)
    return blend(base, violet, 0.25 + (1.0 - wave) * 0.2)


def frame(step: int) -> list[tuple[int, int, int]]:
    return apply_intensity_to_frame([aurora_color(step, phase) for phase in (0.0, 0.8, 1.6, 2.4)])


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
