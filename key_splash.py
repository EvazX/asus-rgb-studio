import argparse
import ctypes
import signal
import time

from ambient_bar import AuraBarController, HID_PATH, HidApi, ZONE_COUNT
from effects_common import apply_intensity_to_frame, blend, clamp_channel


VK_TO_ZONE = {
    0x1B: 0,  # ESC
    0x09: 0,  # TAB
    0x14: 0,  # CAPS
    0x10: 0,  # SHIFT
    0x31: 0, 0x32: 0,  # 1 2
    0x51: 0, 0x57: 0,  # Q W
    0x41: 0, 0x53: 0,  # A S
    0x5A: 0, 0x58: 0,  # Z X

    0x33: 1, 0x34: 1, 0x35: 1,  # 3 4 5
    0x45: 1, 0x52: 1, 0x54: 1,  # E R T
    0x44: 1, 0x46: 1, 0x47: 1,  # D F G
    0x43: 1, 0x56: 1, 0x42: 1,  # C V B

    0x36: 2, 0x37: 2, 0x38: 2,  # 6 7 8
    0x59: 2, 0x55: 2, 0x49: 2,  # Y U I
    0x48: 2, 0x4A: 2, 0x4B: 2,  # H J K
    0x4E: 2, 0x4D: 2,           # N M

    0x39: 3, 0x30: 3, 0xBD: 3,  # 9 0 -
    0x4F: 3, 0x50: 3,           # O P
    0x4C: 3, 0xBA: 3, 0xDE: 3,  # L ; '
    0x08: 3, 0x0D: 3,           # BACKSPACE ENTER
    0x25: 3, 0x26: 3, 0x27: 3, 0x28: 3,  # arrows
}

DEFAULT_COLOR = (90, 180, 255)
HEAT_COLOR = (255, 130, 60)
ICE_COLOR = (90, 220, 255)
VIOLET_COLOR = (180, 110, 255)
USER32 = ctypes.WinDLL("user32", use_last_error=True)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Global keyboard splash effect test.")
    parser.add_argument("--speed", type=float, default=0.03, help="Frame delay in seconds. Default: 0.03.")
    parser.add_argument("--decay", type=float, default=0.86, help="Energy decay per frame. Default: 0.86.")
    parser.add_argument("--spread", type=float, default=0.38, help="Energy spill to neighboring zones. Default: 0.38.")
    parser.add_argument("--style", choices=["cool", "heat", "violet"], default="cool", help="Splash palette.")
    return parser.parse_args()


def palette(style: str) -> tuple[tuple[int, int, int], tuple[int, int, int]]:
    if style == "heat":
        return HEAT_COLOR, (255, 220, 180)
    if style == "violet":
        return VIOLET_COLOR, (255, 180, 255)
    return ICE_COLOR, (220, 245, 255)


def make_frame(energy: list[float], base: tuple[int, int, int], flash: tuple[int, int, int]) -> list[tuple[int, int, int]]:
    colors: list[tuple[int, int, int]] = []
    for index in range(ZONE_COUNT):
        e = max(0.0, min(1.0, energy[index]))
        dim_base = tuple(clamp_channel(channel * max(0.08, e * 0.72)) for channel in base)
        color = blend(dim_base, flash, e * 0.58)
        colors.append(color)
    return apply_intensity_to_frame(colors)


def pressed_now(vk_code: int) -> bool:
    return (USER32.GetAsyncKeyState(vk_code) & 0x8000) != 0


def main() -> int:
    args = parse_args()
    stop = False

    def handle_stop(_signum, _frame) -> None:
        nonlocal stop
        stop = True

    signal.signal(signal.SIGINT, handle_stop)
    signal.signal(signal.SIGTERM, handle_stop)

    base, flash = palette(args.style)
    key_states = {vk: False for vk in VK_TO_ZONE}
    energy = [0.0] * ZONE_COUNT

    hid_api = HidApi()
    with AuraBarController(hid_api, HID_PATH) as controller:
        print("Keyboard splash test active. Press keys, then Ctrl+C to stop.")
        while not stop:
            for zone in range(ZONE_COUNT):
                energy[zone] *= max(0.0, min(0.98, args.decay))
                if energy[zone] < 0.01:
                    energy[zone] = 0.0

            for vk_code, zone in VK_TO_ZONE.items():
                current = pressed_now(vk_code)
                previous = key_states[vk_code]
                if current and not previous:
                    energy[zone] = 1.0
                    if zone > 0:
                        energy[zone - 1] = max(energy[zone - 1], args.spread)
                    if zone < ZONE_COUNT - 1:
                        energy[zone + 1] = max(energy[zone + 1], args.spread)
                key_states[vk_code] = current

            controller.apply_colors(make_frame(energy, base, flash))
            time.sleep(max(0.01, args.speed))

        controller.apply_colors([(255, 255, 255)] * ZONE_COUNT)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
