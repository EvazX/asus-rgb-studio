import argparse
import signal
import time

from ambient_bar import HID_PATH, AuraBarController, HidApi, STATIC_MODE
from effects_common import apply_intensity, clamp_channel


PALETTE = [
    (255, 48, 48),
    (255, 160, 32),
    (255, 230, 64),
    (64, 220, 96),
    (48, 180, 255),
    (140, 96, 255),
    (255, 80, 200),
    (255, 255, 255),
]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Scan ASUS RGB logical indices to detect hidden LED groups.")
    parser.add_argument("--max-index", type=int, default=16, help="Highest logical index to test. Default: 16.")
    parser.add_argument("--hold", type=float, default=1.3, help="Seconds to hold each test index. Default: 1.3.")
    parser.add_argument("--cycles", type=int, default=1, help="Number of scan passes. Default: 1.")
    parser.add_argument("--clear-range", type=int, default=4, help="How many known indices to clear between tests. Default: 4.")
    parser.add_argument("--keep-last", action="store_true", help="Keep the final lit index instead of clearing at the end.")
    return parser.parse_args()


def dim(color: tuple[int, int, int], factor: float) -> tuple[int, int, int]:
    return (
        clamp_channel(color[0] * factor),
        clamp_channel(color[1] * factor),
        clamp_channel(color[2] * factor),
    )


class IndexScanner(AuraBarController):
    def apply_index_map(self, colors_by_index: dict[int, tuple[int, int, int]]) -> None:
        if self._handle is None:
            raise RuntimeError("HID device is not open.")

        for index, color in sorted(colors_by_index.items()):
            red, green, blue = apply_intensity(color)
            packet = [0x5D, 0xB3, index, STATIC_MODE, red, green, blue, 0x00] + [0x00] * 9
            self._hid_api.write(self._handle, packet)

        self._hid_api.write(self._handle, [0x5D, 0xB5] + [0x00] * 15)
        self._hid_api.write(self._handle, [0x5D, 0xB4] + [0x00] * 15)


def build_frame(active_index: int) -> dict[int, tuple[int, int, int]]:
    palette_color = PALETTE[(active_index - 1) % len(PALETTE)]
    return {active_index: palette_color}


def clear_frame(max_index: int) -> dict[int, tuple[int, int, int]]:
    return {logical_index: (0, 0, 0) for logical_index in range(1, max_index + 1)}


def main() -> int:
    args = parse_args()
    stop = False

    def handle_stop(_signum, _frame) -> None:
        nonlocal stop
        stop = True

    signal.signal(signal.SIGINT, handle_stop)
    signal.signal(signal.SIGTERM, handle_stop)

    hid_api = HidApi()
    with IndexScanner(hid_api, HID_PATH) as controller:
        print("LED logical index scan active.")
        print(f"Testing indices 1..{args.max_index}. Press Ctrl+C to stop.")

        controller.apply_index_map(clear_frame(max(1, args.clear_range)))
        time.sleep(0.4)

        for cycle in range(args.cycles):
            for active_index in range(1, args.max_index + 1):
                if stop:
                    break
                controller.apply_index_map(clear_frame(max(1, args.clear_range)))
                time.sleep(0.08)
                frame = build_frame(active_index)
                controller.apply_index_map(frame)
                print(f"Index {active_index:02d} active -> {PALETTE[(active_index - 1) % len(PALETTE)]}")
                time.sleep(max(0.15, args.hold))

            if stop:
                break

        if not args.keep_last:
            controller.apply_index_map(clear_frame(max(1, args.clear_range)))

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
