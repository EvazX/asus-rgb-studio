import argparse
import signal
import time

from ambient_bar import AuraBarController, HID_PATH, HidApi
from effects_common import blend


PALETTE = [
    (255, 90, 0),
    (255, 140, 40),
    (255, 70, 90),
    (200, 30, 120),
]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Sunset drift effect for the ASUS G513QY light bar.")
    parser.add_argument("--speed", type=float, default=0.24, help="Seconds per frame. Default: 0.24.")
    return parser.parse_args()


def make_frame(step: int) -> list[tuple[int, int, int]]:
    offset = step % len(PALETTE)
    shifted = PALETTE[offset:] + PALETTE[:offset]
    return [
        shifted[0],
        blend(shifted[0], shifted[1], 0.45),
        blend(shifted[2], shifted[1], 0.45),
        shifted[2],
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
        print("Sunset drift active. Press Ctrl+C to stop.")
        step = 0
        while not stop:
            controller.apply_colors(make_frame(step))
            time.sleep(max(0.05, args.speed))
            step += 1

        controller.apply_colors([(0, 0, 0)] * 4)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
