import argparse
import signal
import time

from ambient_bar import HID_PATH, AuraBarController, HidApi, STATIC_MODE
from effects_common import apply_intensity


RAINBOW = [
    (255, 48, 48),
    (255, 140, 32),
    (255, 215, 64),
    (64, 220, 96),
    (48, 180, 255),
    (120, 96, 255),
    (255, 80, 200),
]


class LogicalProbe(AuraBarController):
    def apply_index_map(self, colors_by_index: dict[int, tuple[int, int, int]]) -> None:
        if self._handle is None:
            raise RuntimeError("HID device is not open.")

        for index, color in sorted(colors_by_index.items()):
            red, green, blue = apply_intensity(color)
            packet = [0x5D, 0xB3, index, STATIC_MODE, red, green, blue, 0x00] + [0x00] * 9
            self._hid_api.write(self._handle, packet)

        self._hid_api.write(self._handle, [0x5D, 0xB5] + [0x00] * 15)
        self._hid_api.write(self._handle, [0x5D, 0xB4] + [0x00] * 15)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Experimental per-index rainbow probe for the keyboard top row.")
    parser.add_argument("--start-index", type=int, default=1, help="First logical index to probe. Default: 1.")
    parser.add_argument("--count", type=int, default=7, help="How many logical indices to color. Default: 7.")
    parser.add_argument("--hold", type=float, default=5.0, help="Seconds to hold the rainbow pattern. Default: 5.")
    parser.add_argument("--repeat", type=int, default=2, help="How many times to replay the pattern. Default: 2.")
    parser.add_argument("--clear-range", type=int, default=8, help="How many indices to clear before the test. Default: 8.")
    parser.add_argument("--keep-last", action="store_true", help="Keep the final rainbow on screen at the end.")
    return parser.parse_args()


def clear_frame(max_index: int) -> dict[int, tuple[int, int, int]]:
    return {logical_index: (0, 0, 0) for logical_index in range(1, max_index + 1)}


def rainbow_frame(start_index: int, count: int) -> dict[int, tuple[int, int, int]]:
    return {
        start_index + offset: RAINBOW[offset % len(RAINBOW)]
        for offset in range(count)
    }


def main() -> int:
    args = parse_args()
    stop = False

    def handle_stop(_signum, _frame) -> None:
        nonlocal stop
        stop = True

    signal.signal(signal.SIGINT, handle_stop)
    signal.signal(signal.SIGTERM, handle_stop)

    hid_api = HidApi()
    with LogicalProbe(hid_api, HID_PATH) as controller:
        print("Top-row logical probe active.")
        print(f"Applying rainbow on logical indices {args.start_index}..{args.start_index + args.count - 1}.")
        for _ in range(args.repeat):
            if stop:
                break
            controller.apply_index_map(clear_frame(max(1, args.clear_range)))
            time.sleep(0.2)
            controller.apply_index_map(rainbow_frame(args.start_index, args.count))
            time.sleep(max(0.5, args.hold))

        if not args.keep_last:
            controller.apply_index_map(clear_frame(max(1, args.clear_range)))

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
