from ambient_bar import AuraBarController, HID_PATH, HidApi, ZONE_COUNT


def main() -> int:
    hid_api = HidApi()
    with AuraBarController(hid_api, HID_PATH) as controller:
        controller.apply_colors([(255, 255, 255)] * ZONE_COUNT)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
