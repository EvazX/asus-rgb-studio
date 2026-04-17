import argparse
import signal
import time

from ambient_bar import AuraBarController, HID_PATH, HidApi, ZONE_COUNT
from effects_common import apply_intensity_to_frame, clamp_channel


PALETTE = [
    (34, 197, 94),
    (250, 204, 21),
    (251, 113, 133),
    (56, 189, 248),
]


def dim(color: tuple[int, int, int], factor: float) -> tuple[int, int, int]:
    return (
        clamp_channel(color[0] * factor),
        clamp_channel(color[1] * factor),
        clamp_channel(color[2] * factor),
    )


def build_frame(step: int, low_glow: float) -> list[tuple[int, int, int]]:
    phase = (step // ZONE_COUNT) % 2
    fill_count = (step % ZONE_COUNT) + 1
    frame: list[tuple[int, int, int]] = []
    for index in range(ZONE_COUNT):
        active = index < fill_count if phase == 0 else index >= ZONE_COUNT - fill_count
        base = PALETTE[(index + step) % len(PALETTE)]
        frame.append(base if active else dim(base, low_glow))
    return apply_intensity_to_frame(frame)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Vertical stack / Tetris-like RGB effect.")
    parser.add_argument("--speed", type=float, default=0.22, help="Seconds per stack step. Default: 0.22.")
    parser.add_argument("--low-glow", type=float, default=0.14, help="Brightness for inactive zones. Default: 0.14.")
    return parser.parse_args()


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
        print("Stack fall active. Press Ctrl+C to stop.")
        step = 0
        while not stop:
            controller.apply_colors(build_frame(step, max(0.0, min(0.9, args.low_glow))))
            step = (step + 1) % (ZONE_COUNT * 2)
            time.sleep(max(0.05, args.speed))

        controller.apply_colors([(0, 0, 0)] * ZONE_COUNT)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
