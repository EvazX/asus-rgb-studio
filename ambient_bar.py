import argparse
import ctypes
import colorsys
import signal
import sys
import time
from ctypes import POINTER, Structure, byref, c_char_p, c_int, c_size_t, c_ubyte, c_void_p, c_wchar_p

from effects_common import current_intensity


HID_PATH = rb"\\?\HID#VID_0B05&PID_1866&Col05#7&289d55ad&0&0004#{4d1e55b2-f16f-11cf-88cb-001111000030}"
ZONE_COUNT = 4
STATIC_MODE = 0x00
SM_XVIRTUALSCREEN = 76
SM_YVIRTUALSCREEN = 77
SM_CXVIRTUALSCREEN = 78
SM_CYVIRTUALSCREEN = 79


class HidApi:
    def __init__(self) -> None:
        self._hid = ctypes.WinDLL(r"C:\Program Files\OpenRGB\hidapi.dll")
        self._hid.hid_init.restype = c_int
        self._hid.hid_open_path.argtypes = [c_char_p]
        self._hid.hid_open_path.restype = c_void_p
        self._hid.hid_write.argtypes = [c_void_p, POINTER(c_ubyte), c_size_t]
        self._hid.hid_write.restype = c_int
        self._hid.hid_error.argtypes = [c_void_p]
        self._hid.hid_error.restype = c_wchar_p
        self._hid.hid_close.argtypes = [c_void_p]

    def init(self) -> None:
        result = self._hid.hid_init()
        if result != 0:
            raise RuntimeError(f"hid_init failed: {result}")

    def open(self, path: bytes) -> c_void_p:
        handle = self._hid.hid_open_path(path)
        if not handle:
            raise RuntimeError("Unable to open ASUS HID device.")
        return handle

    def write(self, handle: c_void_p, payload: list[int]) -> None:
        buf = (c_ubyte * len(payload))(*payload)
        written = self._hid.hid_write(handle, buf, len(payload))
        if written < 0:
            raise RuntimeError(self._hid.hid_error(handle) or "hid_write failed")

    def close(self, handle: c_void_p) -> None:
        if handle:
            self._hid.hid_close(handle)


class ScreenSampler:
    def __init__(self) -> None:
        self._user32 = ctypes.WinDLL("user32", use_last_error=True)
        self._gdi32 = ctypes.WinDLL("gdi32", use_last_error=True)
        self._user32.GetDC.argtypes = [c_void_p]
        self._user32.GetDC.restype = c_void_p
        self._user32.ReleaseDC.argtypes = [c_void_p, c_void_p]
        self._user32.ReleaseDC.restype = c_int
        self._user32.GetSystemMetrics.argtypes = [c_int]
        self._user32.GetSystemMetrics.restype = c_int
        self._gdi32.GetPixel.argtypes = [c_void_p, c_int, c_int]
        self._gdi32.GetPixel.restype = ctypes.c_uint32

    def _virtual_bounds(self) -> tuple[int, int, int, int]:
        left = self._user32.GetSystemMetrics(SM_XVIRTUALSCREEN)
        top = self._user32.GetSystemMetrics(SM_YVIRTUALSCREEN)
        width = self._user32.GetSystemMetrics(SM_CXVIRTUALSCREEN)
        height = self._user32.GetSystemMetrics(SM_CYVIRTUALSCREEN)
        return left, top, width, height

    def capture_zones(
        self,
        samples_x: int,
        samples_y: int,
        vertical_bias: float,
        neutral_threshold: int,
        color_bias: float,
    ) -> list[tuple[int, int, int]]:
        left, top, width, height = self._virtual_bounds()
        if width <= 0 or height <= 0:
            raise RuntimeError("Unable to read virtual screen bounds.")

        dc = self._user32.GetDC(0)
        if not dc:
            raise RuntimeError("Unable to access desktop DC.")

        zones: list[tuple[int, int, int]] = []
        try:
            zone_width = width / ZONE_COUNT
            step_x = max(1, int(zone_width / max(1, samples_x)))
            sampled_height = max(1, int(height * max(0.1, min(1.0, vertical_bias))))
            start_y = top + height - sampled_height
            step_y = max(1, int(sampled_height / max(1, samples_y)))

            for zone_index in range(ZONE_COUNT):
                x0 = int(left + (zone_index * zone_width))
                x1 = int(left + ((zone_index + 1) * zone_width))
                r_total = 0
                g_total = 0
                b_total = 0
                weight_total = 0.0
                fallback_r_total = 0
                fallback_g_total = 0
                fallback_b_total = 0
                fallback_count = 0

                for y in range(start_y, top + height, step_y):
                    for x in range(x0, x1, step_x):
                        color = self._gdi32.GetPixel(dc, x, y)
                        if color == 0xFFFFFFFF:
                            continue
                        red = color & 0xFF
                        green = (color >> 8) & 0xFF
                        blue = (color >> 16) & 0xFF
                        fallback_r_total += red
                        fallback_g_total += green
                        fallback_b_total += blue
                        fallback_count += 1
                        color_span = max(red, green, blue) - min(red, green, blue)

                        if color_span < neutral_threshold:
                            continue

                        weight = 1.0 + (color_span / 255.0) * max(0.0, color_bias)
                        r_total += red * weight
                        g_total += green * weight
                        b_total += blue * weight
                        weight_total += weight

                if weight_total == 0:
                    if fallback_count == 0:
                        zones.append((0, 0, 0))
                    else:
                        zones.append(
                            (
                                fallback_r_total // fallback_count,
                                fallback_g_total // fallback_count,
                                fallback_b_total // fallback_count,
                            )
                        )
                else:
                    zones.append(
                        (
                            int(r_total / weight_total),
                            int(g_total / weight_total),
                            int(b_total / weight_total),
                        )
                    )
        finally:
            self._user32.ReleaseDC(0, dc)

        return zones


class AuraBarController:
    def __init__(self, hid_api: HidApi, path: bytes) -> None:
        self._hid_api = hid_api
        self._path = path
        self._handle = None

    def __enter__(self) -> "AuraBarController":
        self._hid_api.init()
        self._handle = self._hid_api.open(self._path)
        return self

    def __exit__(self, exc_type, exc, tb) -> None:
        self._hid_api.close(self._handle)
        self._handle = None

    def apply_colors(self, colors: list[tuple[int, int, int]]) -> None:
        if self._handle is None:
            raise RuntimeError("HID device is not open.")

        intensity = current_intensity()
        for index, (red, green, blue) in enumerate(colors, start=1):
            scaled_red = max(0, min(255, int(red * intensity)))
            scaled_green = max(0, min(255, int(green * intensity)))
            scaled_blue = max(0, min(255, int(blue * intensity)))
            packet = [0x5D, 0xB3, index, STATIC_MODE, scaled_red, scaled_green, scaled_blue, 0x00] + [0x00] * 9
            self._hid_api.write(self._handle, packet)

        self._hid_api.write(self._handle, [0x5D, 0xB5] + [0x00] * 15)
        self._hid_api.write(self._handle, [0x5D, 0xB4] + [0x00] * 15)

    def solid_red(self) -> None:
        self.apply_colors([(255, 0, 0)] * ZONE_COUNT)


def color_distance(left: tuple[int, int, int], right: tuple[int, int, int]) -> int:
    return abs(left[0] - right[0]) + abs(left[1] - right[1]) + abs(left[2] - right[2])


def boost_color(color: tuple[int, int, int], saturation_boost: float, value_boost: float) -> tuple[int, int, int]:
    red = color[0] / 255.0
    green = color[1] / 255.0
    blue = color[2] / 255.0
    hue, saturation, value = colorsys.rgb_to_hsv(red, green, blue)
    saturation = max(0.0, min(1.0, saturation * saturation_boost))
    value = max(0.0, min(1.0, value * value_boost))
    boosted = colorsys.hsv_to_rgb(hue, saturation, value)
    return tuple(max(0, min(255, int(channel * 255))) for channel in boosted)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="ASUS ambient light-bar sync for G513QY.")
    parser.add_argument("--fps", type=float, default=16.0, help="Update rate. Default: 16.")
    parser.add_argument("--samples-x", type=int, default=8, help="Horizontal samples per zone. Default: 8.")
    parser.add_argument("--samples-y", type=int, default=5, help="Vertical samples per zone. Default: 5.")
    parser.add_argument("--threshold", type=int, default=8, help="Minimum total RGB change before update. Default: 8.")
    parser.add_argument("--vertical-bias", type=float, default=0.22, help="Bottom portion of the screen to sample. Default: 0.22.")
    parser.add_argument("--saturation-boost", type=float, default=3.0, help="Color saturation multiplier. Default: 3.0.")
    parser.add_argument("--value-boost", type=float, default=0.9, help="Brightness multiplier. Default: 0.9.")
    parser.add_argument("--neutral-threshold", type=int, default=30, help="Ignore near-gray pixels below this RGB spread. Default: 30.")
    parser.add_argument("--color-bias", type=float, default=3.5, help="Extra weight for highly saturated pixels. Default: 3.5.")
    parser.add_argument("--once", action="store_true", help="Apply one frame and exit.")
    parser.add_argument("--red", action="store_true", help="Force solid red and exit.")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    sampler = ScreenSampler()
    hid_api = HidApi()
    stop = False

    def handle_stop(_signum, _frame) -> None:
        nonlocal stop
        stop = True

    signal.signal(signal.SIGINT, handle_stop)
    signal.signal(signal.SIGTERM, handle_stop)

    frame_delay = max(0.02, 1.0 / max(0.1, args.fps))
    last_colors: list[tuple[int, int, int]] | None = None

    with AuraBarController(hid_api, HID_PATH) as controller:
        if args.red:
            controller.solid_red()
            print("Applied solid red.")
            return 0

        print("Ambient sync active. Press Ctrl+C to stop.")

        while not stop:
            sampled_colors = sampler.capture_zones(
                args.samples_x,
                args.samples_y,
                args.vertical_bias,
                args.neutral_threshold,
                args.color_bias,
            )
            colors = [boost_color(color, args.saturation_boost, args.value_boost) for color in sampled_colors]

            should_update = last_colors is None
            if last_colors is not None:
                for previous, current in zip(last_colors, colors):
                    if color_distance(previous, current) >= args.threshold:
                        should_update = True
                        break

            if should_update:
                controller.apply_colors(colors)
                last_colors = colors
                print("Applied:", colors)

            if args.once:
                break

            time.sleep(frame_delay)

    return 0


if __name__ == "__main__":
    sys.exit(main())
