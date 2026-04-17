import argparse
import sys
import time
import tkinter as tk

from ambient_bar import AuraBarController, HID_PATH, HidApi


SEQUENCE = [
    ("RED", (255, 0, 0)),
    ("GREEN", (0, 255, 0)),
    ("BLUE", (0, 80, 255)),
    ("WHITE", (255, 255, 255)),
    ("BLACK", (0, 0, 0)),
]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Synchronized screen + LED latency probe.")
    parser.add_argument("--hold", type=float, default=1.2, help="Seconds to hold each color. Default: 1.2.")
    parser.add_argument("--loops", type=int, default=3, help="Number of sequence loops. Default: 3.")
    return parser.parse_args()


def rgb_to_hex(color: tuple[int, int, int]) -> str:
    return "#{:02x}{:02x}{:02x}".format(*color)


def main() -> int:
    args = parse_args()

    root = tk.Tk()
    root.title("LED Latency Probe")
    root.configure(bg="black")
    root.attributes("-fullscreen", True)
    root.bind("<Escape>", lambda _event: root.destroy())

    label = tk.Label(
        root,
        text="Preparing...",
        font=("Segoe UI", 48, "bold"),
        fg="white",
        bg="black",
    )
    label.pack(expand=True)

    hid_api = HidApi()
    controller = AuraBarController(hid_api, HID_PATH)
    controller.__enter__()

    events: list[tuple[str, float]] = []
    step_index = 0
    total_steps = max(1, args.loops) * len(SEQUENCE)

    def cleanup() -> None:
        try:
            controller.apply_colors([(0, 0, 0)] * 4)
        except Exception:
            pass
        controller.__exit__(None, None, None)

    def apply_step() -> None:
        nonlocal step_index

        if step_index >= total_steps:
            print()
            print("Done.")
            print("Software timestamps:")
            for name, timestamp in events:
                print(f"{timestamp:.6f} {name}")
            cleanup()
            root.after(250, root.destroy)
            return

        name, color = SEQUENCE[step_index % len(SEQUENCE)]
        now = time.perf_counter()
        events.append((name, now))

        root.configure(bg=rgb_to_hex(color))
        label.configure(
            text=f"{name}\n{now:.6f}",
            bg=rgb_to_hex(color),
            fg="black" if sum(color) > 500 else "white",
        )
        controller.apply_colors([color] * 4)
        print(f"{now:.6f} {name}")

        step_index += 1
        root.after(int(max(0.2, args.hold) * 1000), apply_step)

    def on_close() -> None:
        cleanup()
        root.destroy()

    root.protocol("WM_DELETE_WINDOW", on_close)
    root.after(300, apply_step)
    root.mainloop()
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        sys.exit(130)
