import subprocess
import sys
from pathlib import Path


BASE_DIR = Path(__file__).resolve().parent


EFFECTS = {
    "1": {
        "label": "K2000 rouge",
        "command": [sys.executable, "k2000.py"],
    },
    "2": {
        "label": "Police rouge/bleu",
        "command": [sys.executable, "police.py"],
    },
    "3": {
        "label": "Ambilight ecran",
        "command": [sys.executable, "ambient_bar.py"],
    },
    "3R": {
        "label": "Ambilight reactif",
        "command": [
            sys.executable,
            "ambient_bar.py",
            "--fps",
            "28",
            "--threshold",
            "3",
            "--samples-x",
            "6",
            "--samples-y",
            "4",
            "--vertical-bias",
            "0.22",
            "--saturation-boost",
            "3.0",
            "--value-boost",
            "0.9",
            "--neutral-threshold",
            "30",
            "--color-bias",
            "3.5",
        ],
    },
    "3E": {
        "label": "Ambilight equilibre",
        "command": [
            sys.executable,
            "ambient_bar.py",
            "--fps",
            "16",
            "--threshold",
            "8",
        ],
    },
    "3C": {
        "label": "Ambilight cinematique",
        "command": [
            sys.executable,
            "ambient_bar.py",
            "--fps",
            "10",
            "--threshold",
            "14",
            "--samples-x",
            "10",
            "--samples-y",
            "6",
            "--vertical-bias",
            "0.28",
            "--saturation-boost",
            "2.4",
            "--value-boost",
            "0.85",
        ],
    },
    "4": {
        "label": "Rouge fixe",
        "command": [sys.executable, "ambient_bar.py", "--red"],
    },
    "5": {
        "label": "Mapping zones",
        "command": [sys.executable, "map_zones.py"],
    },
    "6": {
        "label": "VHS neon",
        "command": [sys.executable, "vhs_neon.py"],
    },
    "7": {
        "label": "Afterburner",
        "command": [sys.executable, "afterburner.py"],
    },
    "8": {
        "label": "Sunset drift",
        "command": [sys.executable, "sunset_drift.py"],
    },
    "9": {
        "label": "Acid pulse",
        "command": [sys.executable, "acid_pulse.py"],
    },
    "10": {
        "label": "Ice scanner",
        "command": [sys.executable, "ice_scanner.py"],
    },
    "11": {
        "label": "Storm mode",
        "command": [sys.executable, "storm_mode.py"],
    },
    "12": {
        "label": "Autumn glow",
        "command": [sys.executable, "autumn_glow.py"],
    },
    "13": {
        "label": "Copper pulse",
        "command": [sys.executable, "copper_pulse.py"],
    },
    "14": {
        "label": "Ember rain",
        "command": [sys.executable, "ember_rain.py"],
    },
    "15": {
        "label": "Cyberpunk",
        "command": [sys.executable, "cyberpunk.py"],
    },
    "16": {
        "label": "Death Stranding drift",
        "command": [sys.executable, "death_stranding_drift.py"],
    },
    "17": {
        "label": "Factory core",
        "command": [sys.executable, "factory_core.py"],
    },
    "18": {
        "label": "DedSec glitch",
        "command": [sys.executable, "dedsec_glitch.py"],
    },
    "19": {
        "label": "Frontier dust",
        "command": [sys.executable, "frontier_dust.py"],
    },
    "20": {
        "label": "Alter lab",
        "command": [sys.executable, "alter_lab.py"],
    },
    "21": {
        "label": "Ambilight C# reactif",
        "command": [
            "dotnet",
            r".\csharp-ambient\bin\Release\net8.0-windows\AmbientBar.dll",
        ],
    },
    "21B": {
        "label": "Ambilight C# reactif boost",
        "command": [
            "dotnet",
            r".\csharp-ambient\bin\Release\net8.0-windows\AmbientBar.dll",
            "--intensity-boost",
            "1.2",
            "--saturation-boost",
            "3.3",
        ],
    },
    "21X": {
        "label": "Ambilight C# reactif max",
        "command": [
            "dotnet",
            r".\csharp-ambient\bin\Release\net8.0-windows\AmbientBar.dll",
            "--intensity-boost",
            "1.5",
            "--saturation-boost",
            "3.8",
            "--value-boost",
            "1.05",
            "--threshold",
            "2",
        ],
    },
    "21R": {
        "label": "Ambilight C# rouge fixe",
        "command": [
            "dotnet",
            r".\csharp-ambient\bin\Release\net8.0-windows\AmbientBar.dll",
            "--red",
        ],
    },
    "22": {
        "label": "Latency probe ecran + LEDs",
        "command": [sys.executable, "latency_probe.py"],
    },
}


def print_menu() -> None:
    print()
    print("ASUS RGB Menu")
    print("==============")
    for key, effect in EFFECTS.items():
        print(f"{key}. {effect['label']}")
    print("Q. Quitter")
    print()


def launch_effect(command: list[str], label: str) -> None:
    print()
    print(f"Lancement: {label}")
    print("Arrete avec Ctrl+C pour revenir au menu.")
    print()

    process = subprocess.Popen(command, cwd=BASE_DIR)
    try:
        process.wait()
    except KeyboardInterrupt:
        process.terminate()
        try:
            process.wait(timeout=2)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()


def main() -> int:
    while True:
        print_menu()
        choice = input("Choix: ").strip().upper()

        if choice == "Q":
            return 0

        effect = EFFECTS.get(choice)
        if effect is None:
            print("Choix invalide.")
            continue

        launch_effect(effect["command"], effect["label"])


if __name__ == "__main__":
    raise SystemExit(main())
