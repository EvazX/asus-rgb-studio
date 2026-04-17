import argparse
import signal
import time

from ambient_bar import AuraBarController, HID_PATH, HidApi, ZONE_COUNT
from effects_common import blend


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="VHS neon effect for the ASUS G513QY light bar.")
    parser.add_argument("--speed", type=float, default=0.18, help="Seconds per frame. Default: 0.18.")
    return parser.parse_args()


def make_frame(step: int) -> list[tuple[int, int, int]]:
    cyan = (0, 255, 220)
    magenta = (255, 0, 170)
    white = (255, 255, 255)
    frames = [
        [cyan, blend(cyan, magenta, 0.35), blend(magenta, cyan, 0.35), magenta],
        [blend(cyan, white, 0.25), magenta, cyan, blend(magenta, white, 0.25)],
        [magenta, blend(magenta, cyan, 0.4), blend(cyan, magenta, 0.4), cyan],
        [(0, 0, 0)] * ZONE_COUNT,
    ]
    return frames[step % len(frames)]


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
        print("VHS neon active. Press Ctrl+C to stop.")
        step = 0
        while not stop:
            controller.apply_colors(make_frame(step))
            time.sleep(max(0.04, args.speed))
            step += 1

        controller.apply_colors([(0, 0, 0)] * ZONE_COUNT)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
