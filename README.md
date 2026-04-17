# ASUS RGB Studio

Custom RGB effects for ASUS laptops, built around a lightweight Windows app and a set of effect engines for the `ASUS G513QY` family and similar Aura-compatible setups.

This project exists because the default ASUS / Armoury Crate experience can feel limiting for advanced effects. The goal here is simple: keep the hardware control practical, make the effects look better, and provide a cleaner interface for real daily use.

## What it does

- Runs custom RGB effects for keyboard + light bar
- Includes a compact right-side Windows control panel
- Provides live previews for each effect
- Stores a global intensity value and applies it live
- Watches the running effect process and restarts it if it dies
- Includes ambient / mirror / audio-reactive modes plus handcrafted presets

## Current highlights

- `Ambilight Reactif`
- `Mirror`
- `Audio Pulse`
- `K2000`
- `Police`
- `Cyberpunk`
- `Aurora Drift`
- `Prism Flow`
- `Deep Ocean`
- `Stack Fall`

## Project structure

- [`rgb-control-ui/`](D:/asus-ambient-led/rgb-control-ui)  
  Windows Forms app used as the main control surface
- [`csharp-ambient/`](D:/asus-ambient-led/csharp-ambient)  
  C# ambient / mirror engine
- [`csharp-audio/`](D:/asus-ambient-led/csharp-audio)  
  C# audio-reactive engine
- `*.py` effect scripts  
  Custom effect library and hardware experiments
- [`test_patterns.html`](D:/asus-ambient-led/test_patterns.html)  
  Local pattern page for visual tuning and testing

## Running the app

Requirements:

- Windows
- .NET 8 Desktop Runtime
- An ASUS Aura-compatible laptop setup
- `hidapi.dll` available through the local OpenRGB install path currently used by the project

Run:

```powershell
cd D:\asus-ambient-led\rgb-control-ui
dotnet .\bin\Release\net8.0-windows\AsusRgbStudio.dll
```

Build:

```powershell
cd D:\asus-ambient-led\rgb-control-ui
dotnet build RgbControlUI.csproj -c Release
```

## Status

This project is an enthusiast tool, not an official ASUS utility.

It works best when:

- ASUS lighting services are healthy
- only one RGB controller is driving the device at a time
- the laptop model behaves similarly to the hardware tested during development

## Important note

This project interacts with proprietary ASUS lighting behavior. Some models may react differently. If the official ASUS lighting stack is already unstable, repair that first before using custom effects.

## Roadmap

- More polished Fluent-style UI
- Better effect categorization and favorites
- More native C# effect ports
- Cinema mode
- FPS mode
- Better hardware mapping for keyboards with richer internal interpolation

## Support the project

If you like the project and want to support future improvements, see [`SUPPORT.md`](./SUPPORT.md).

## French pitch

A ready-to-share French presentation is available in [`PRESENTATION_FR.md`](./PRESENTATION_FR.md).
