import argparse
import signal
import time

from ambient_bar import AuraBarController, HID_PATH, HidApi, ZONE_COUNT
from effects_common import apply_intensity_to_frame


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Police light effect for the ASUS G513QY light bar.")
    parser.add_argument("--speed", type=float, default=0.18, help="Seconds per step. Default: 0.18.")
    parser.add_argument("--pause", type=float, default=0.08, help="Blackout pause between flashes. Default: 0.08.")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    stop = False

    def handle_stop(_signum, _frame) -> None:
        nonlocal stop
        stop = True

    signal.signal(signal.SIGINT, handle_stop)
    signal.signal(signal.SIGTERM, handle_stop)

    off = apply_intensity_to_frame([(0, 0, 0)] * ZONE_COUNT)
    left_red_right_blue = apply_intensity_to_frame([(255, 0, 0), (255, 0, 0), (0, 0, 255), (0, 0, 255)])
    left_blue_right_red = apply_intensity_to_frame([(0, 0, 255), (0, 0, 255), (255, 0, 0), (255, 0, 0)])

    hid_api = HidApi()
    with AuraBarController(hid_api, HID_PATH) as controller:
        print("Police effect active. Press Ctrl+C to stop.")
        while not stop:
            controller.apply_colors(left_red_right_blue)
            time.sleep(max(0.03, args.speed))
            if stop:
                break

            controller.apply_colors(off)
            time.sleep(max(0.02, args.pause))
            if stop:
                break

            controller.apply_colors(left_blue_right_red)
            time.sleep(max(0.03, args.speed))
            if stop:
                break

            controller.apply_colors(off)
            time.sleep(max(0.02, args.pause))

        controller.apply_colors(off)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
