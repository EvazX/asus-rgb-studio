import argparse
import math
import signal
import time

from ambient_bar import AuraBarController, HID_PATH, HidApi, ZONE_COUNT
from effects_common import apply_intensity_to_frame, clamp_channel


GREEN = (34, 255, 110)
LIME = (190, 255, 90)
DARK = (0, 18, 8)


def scale(color: tuple[int, int, int], factor: float) -> tuple[int, int, int]:
    return (
        clamp_channel(color[0] * factor),
        clamp_channel(color[1] * factor),
        clamp_channel(color[2] * factor),
    )


def build_frame(step: int) -> list[tuple[int, int, int]]:
    # The ASUS board exposes four horizontal zones, so the "rain" travels left/right.
    head = step % ZONE_COUNT
    echo = (head - 1) % ZONE_COUNT
    frame: list[tuple[int, int, int]] = []
    for index in range(ZONE_COUNT):
        wave = (math.sin(step * 0.7 - index * 1.1) + 1.0) / 2.0
        glitch = (step + index * 3) % 13 == 0
        if index == head:
            frame.append(LIME)
        elif index == echo:
            frame.append(scale(GREEN, 0.62))
        elif glitch:
            frame.append(scale(LIME, 0.8))
        else:
            frame.append(scale(GREEN, 0.12 + wave * 0.34))

    if step % 11 == 0:
        frame[(step + 2) % ZONE_COUNT] = DARK

    return apply_intensity_to_frame(frame)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Matrix rain RGB effect.")
    parser.add_argument("--speed", type=float, default=0.075, help="Seconds per frame. Default: 0.075.")
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
        step = 0
        while not stop:
            controller.apply_colors(build_frame(step))
            step += 1
            time.sleep(max(0.03, args.speed))

        controller.apply_colors([(0, 0, 0)] * ZONE_COUNT)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
