import argparse
import signal
import time

from ambient_bar import AuraBarController, HID_PATH, HidApi, ZONE_COUNT
from effects_common import apply_intensity_to_frame


def clamp_channel(value: float) -> int:
    return max(0, min(255, int(value)))


def mix(base: tuple[int, int, int], factor: float) -> tuple[int, int, int]:
    return (
        clamp_channel(base[0] * factor),
        clamp_channel(base[1] * factor),
        clamp_channel(base[2] * factor),
    )


def frame_for_position(position: int, head: tuple[int, int, int], tail: tuple[int, int, int]) -> list[tuple[int, int, int]]:
    colors: list[tuple[int, int, int]] = []
    for index in range(ZONE_COUNT):
        distance = abs(index - position)
        if distance == 0:
            colors.append(head)
        elif distance == 1:
            colors.append(tail)
        else:
            colors.append((0, 0, 0))
    return apply_intensity_to_frame(colors)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="K2000 effect for the ASUS G513QY light bar.")
    parser.add_argument("--speed", type=float, default=0.16, help="Seconds per step. Default: 0.16.")
    parser.add_argument("--tail", type=float, default=0.22, help="Tail intensity from 0 to 1. Default: 0.22.")
    parser.add_argument("--red", type=int, default=255, help="Red channel. Default: 255.")
    parser.add_argument("--green", type=int, default=0, help="Green channel. Default: 0.")
    parser.add_argument("--blue", type=int, default=0, help="Blue channel. Default: 0.")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    stop = False

    def handle_stop(_signum, _frame) -> None:
        nonlocal stop
        stop = True

    signal.signal(signal.SIGINT, handle_stop)
    signal.signal(signal.SIGTERM, handle_stop)

    head = (
        clamp_channel(args.red),
        clamp_channel(args.green),
        clamp_channel(args.blue),
    )
    tail = mix(head, max(0.0, min(1.0, args.tail)))
    positions = list(range(ZONE_COUNT)) + list(range(ZONE_COUNT - 2, 0, -1))

    hid_api = HidApi()
    with AuraBarController(hid_api, HID_PATH) as controller:
        print("K2000 active. Press Ctrl+C to stop.")
        while not stop:
            for position in positions:
                controller.apply_colors(frame_for_position(position, head, tail))
                time.sleep(max(0.03, args.speed))
                if stop:
                    break

        controller.apply_colors([(0, 0, 0)] * ZONE_COUNT)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
