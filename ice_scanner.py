import argparse
import signal
import time

from ambient_bar import AuraBarController, HID_PATH, HidApi, ZONE_COUNT
from effects_common import scale


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Ice scanner effect for the ASUS G513QY light bar.")
    parser.add_argument("--speed", type=float, default=0.15, help="Seconds per step. Default: 0.15.")
    parser.add_argument("--tail", type=float, default=0.3, help="Tail intensity from 0 to 1. Default: 0.3.")
    return parser.parse_args()


def frame_for_position(position: int, head: tuple[int, int, int], tail_factor: float) -> list[tuple[int, int, int]]:
    colors: list[tuple[int, int, int]] = []
    for index in range(ZONE_COUNT):
        distance = abs(index - position)
        if distance == 0:
            colors.append(head)
        elif distance == 1:
            colors.append(scale(head, tail_factor))
        else:
            colors.append((0, 0, 0))
    return colors


def main() -> int:
    args = parse_args()
    stop = False

    def handle_stop(_signum, _frame) -> None:
        nonlocal stop
        stop = True

    signal.signal(signal.SIGINT, handle_stop)
    signal.signal(signal.SIGTERM, handle_stop)

    positions = list(range(ZONE_COUNT)) + list(range(ZONE_COUNT - 2, 0, -1))
    head = (140, 220, 255)
    tail_factor = max(0.0, min(1.0, args.tail))

    hid_api = HidApi()
    with AuraBarController(hid_api, HID_PATH) as controller:
        print("Ice scanner active. Press Ctrl+C to stop.")
        while not stop:
            for position in positions:
                controller.apply_colors(frame_for_position(position, head, tail_factor))
                time.sleep(max(0.04, args.speed))
                if stop:
                    break

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
