import argparse
import signal
import time

from ambient_bar import AuraBarController, HID_PATH, HidApi, ZONE_COUNT
from effects_common import apply_intensity_to_frame, clamp_channel


HEAD = (255, 255, 255)
CYAN = (0, 234, 255)
MAGENTA = (255, 44, 240)
VIOLET = (124, 58, 237)


def scale(color: tuple[int, int, int], factor: float) -> tuple[int, int, int]:
    return (
        clamp_channel(color[0] * factor),
        clamp_channel(color[1] * factor),
        clamp_channel(color[2] * factor),
    )


def mix(left: tuple[int, int, int], right: tuple[int, int, int], ratio: float) -> tuple[int, int, int]:
    r = max(0.0, min(1.0, ratio))
    return (
        clamp_channel(left[0] + (right[0] - left[0]) * r),
        clamp_channel(left[1] + (right[1] - left[1]) * r),
        clamp_channel(left[2] + (right[2] - left[2]) * r),
    )


def build_frame(step: int) -> list[tuple[int, int, int]]:
    sweep = list(range(ZONE_COUNT)) + list(range(ZONE_COUNT - 2, 0, -1))
    head_index = sweep[step % len(sweep)]
    tail_color = mix(CYAN, MAGENTA, (step % len(sweep)) / max(1, len(sweep) - 1))
    frame: list[tuple[int, int, int]] = []

    for index in range(ZONE_COUNT):
        distance = abs(index - head_index)
        if distance == 0:
            frame.append(HEAD)
        elif distance == 1:
            frame.append(scale(tail_color, 0.55))
        else:
            frame.append(scale(VIOLET, 0.16))

    return apply_intensity_to_frame(frame)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Neon comet RGB effect.")
    parser.add_argument("--speed", type=float, default=0.085, help="Seconds per frame. Default: 0.085.")
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
