import time

from ambient_bar import AuraBarController, HidApi


def main() -> int:
    steps = [
        ("zone 1 red", [(255, 0, 0), (0, 0, 0), (0, 0, 0), (0, 0, 0)]),
        ("pause", [(0, 0, 0)] * 4),
        ("zone 2 orange", [(0, 0, 0), (255, 128, 0), (0, 0, 0), (0, 0, 0)]),
        ("pause", [(0, 0, 0)] * 4),
        ("zone 3 green", [(0, 0, 0), (0, 0, 0), (0, 255, 0), (0, 0, 0)]),
        ("pause", [(0, 0, 0)] * 4),
        ("zone 4 blue", [(0, 0, 0), (0, 0, 0), (0, 0, 0), (0, 128, 255)]),
        ("pause", [(0, 0, 0)] * 4),
        ("rainbow order", [(255, 0, 0), (255, 128, 0), (0, 255, 0), (0, 128, 255)]),
        ("all white", [(255, 255, 255)] * 4),
        ("all red", [(255, 0, 0)] * 4),
    ]

    hid_api = HidApi()
    with AuraBarController(hid_api, hid_api_path()) as controller:
        for label, colors in steps:
            print("Applying", label, colors)
            controller.apply_colors(colors)
            time.sleep(5)

    return 0


def hid_api_path() -> bytes:
    return rb"\\?\HID#VID_0B05&PID_1866&Col05#7&289d55ad&0&0004#{4d1e55b2-f16f-11cf-88cb-001111000030}"


if __name__ == "__main__":
    raise SystemExit(main())
