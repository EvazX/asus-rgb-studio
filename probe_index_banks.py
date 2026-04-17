import argparse
import signal
import time

from ambient_bar import HID_PATH, AuraBarController, HidApi, STATIC_MODE
from effects_common import apply_intensity


BANK_PALETTES = [
    [(255, 48, 48), (255, 120, 48), (255, 196, 64), (255, 255, 255)],
    [(64, 220, 96), (48, 180, 255), (120, 96, 255), (255, 80, 200)],
    [(255, 64, 160), (255, 96, 96), (255, 180, 48), (255, 255, 96)],
    [(48, 255, 200), (48, 180, 255), (96, 128, 255), (180, 96, 255)],
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
    parser = argparse.ArgumentParser(description="Probe ASUS RGB logical index banks.")
    parser.add_argument("--bank-size", type=int, default=4, help="Number of logical indices per bank. Default: 4.")
    parser.add_argument("--banks", type=int, default=4, help="How many banks to test. Default: 4.")
    parser.add_argument("--start-index", type=int, default=1, help="First logical index. Default: 1.")
    parser.add_argument("--hold", type=float, default=4.0, help="Seconds to hold each bank. Default: 4.")
    parser.add_argument("--repeat", type=int, default=1, help="How many passes to make. Default: 1.")
    parser.add_argument("--clear-range", type=int, default=20, help="How many indices to clear between banks. Default: 20.")
    parser.add_argument("--keep-last", action="store_true", help="Keep the last bank visible.")
    return parser.parse_args()


def clear_frame(max_index: int) -> dict[int, tuple[int, int, int]]:
    return {logical_index: (0, 0, 0) for logical_index in range(1, max_index + 1)}


def bank_frame(start_index: int, bank_size: int, palette: list[tuple[int, int, int]]) -> dict[int, tuple[int, int, int]]:
    return {
        start_index + offset: palette[offset % len(palette)]
        for offset in range(bank_size)
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
        print("Logical bank probe active.")
        print(f"Testing {args.banks} banks of {args.bank_size} indices from {args.start_index}.")

        for _ in range(args.repeat):
            for bank_number in range(args.banks):
                if stop:
                    break

                bank_start = args.start_index + bank_number * args.bank_size
                palette = BANK_PALETTES[bank_number % len(BANK_PALETTES)]
                controller.apply_index_map(clear_frame(max(1, args.clear_range)))
                time.sleep(0.25)
                controller.apply_index_map(bank_frame(bank_start, args.bank_size, palette))
                print(f"Bank {bank_number + 1}: indices {bank_start}..{bank_start + args.bank_size - 1}")
                time.sleep(max(0.8, args.hold))

            if stop:
                break

        if not args.keep-last:
            controller.apply_index_map(clear_frame(max(1, args.clear_range)))

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
