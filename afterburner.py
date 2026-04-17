import argparse
import signal
import time

from ambient_bar import AuraBarController, HID_PATH, HidApi
from effects_common import blend


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Afterburner effect for the ASUS G513QY light bar.")
    parser.add_argument("--speed", type=float, default=0.14, help="Seconds per frame. Default: 0.14.")
    return parser.parse_args()


def make_frame(step: int) -> list[tuple[int, int, int]]:
    deep_red = (120, 0, 0)
    hot_red = (255, 30, 0)
    orange = (255, 120, 0)
    white_hot = (255, 240, 180)
    pattern = [
        [deep_red, orange, white_hot, hot_red],
        [hot_red, white_hot, orange, deep_red],
        [deep_red, hot_red, white_hot, orange],
        [orange, white_hot, hot_red, deep_red],
        [blend(deep_red, hot_red, 0.5), orange, blend(white_hot, orange, 0.3), hot_red],
        [hot_red, blend(white_hot, orange, 0.2), orange, blend(deep_red, hot_red, 0.5)],
    ]
    return pattern[step % len(pattern)]


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
        print("Afterburner active. Press Ctrl+C to stop.")
        step = 0
        while not stop:
            controller.apply_colors(make_frame(step))
            time.sleep(max(0.04, args.speed))
            step += 1

        controller.apply_colors([(0, 0, 0)] * 4)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
