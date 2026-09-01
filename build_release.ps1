param(
    [string]$Version = "v0.1.1"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ReleaseRoot = Join-Path $RepoRoot "release"
$Stage = Join-Path $ReleaseRoot "asus-rgb-studio-$Version"
$Zip = Join-Path $ReleaseRoot "asus-rgb-studio-$Version.zip"
$UiPublish = Join-Path $RepoRoot "rgb-control-ui\bin\Release\net8.0-windows\win-x64\publish"
$AmbientPublish = Join-Path $RepoRoot "csharp-ambient\bin\Release\net8.0-windows\win-x64\publish"
$AudioPublish = Join-Path $RepoRoot "csharp-audio\bin\Release\net8.0-windows\win-x64\publish"

function New-CleanDirectory {
    param([string]$Path)
    if (Test-Path $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Path | Out-Null
}

dotnet publish (Join-Path $RepoRoot "rgb-control-ui\RgbControlUI.csproj") -c Release -r win-x64 --self-contained true
dotnet publish (Join-Path $RepoRoot "csharp-ambient\AmbientBar.csproj") -c Release -r win-x64 --self-contained true
dotnet publish (Join-Path $RepoRoot "csharp-audio\AudioReactive.csproj") -c Release -r win-x64 --self-contained true

New-CleanDirectory $Stage
New-Item -ItemType Directory -Path (Join-Path $Stage "app") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $Stage "csharp-ambient\bin\Release\net8.0-windows") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $Stage "csharp-audio\bin\Release\net8.0-windows") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $Stage "docs") -Force | Out-Null

Copy-Item -Path (Join-Path $UiPublish "*") -Destination (Join-Path $Stage "app") -Recurse -Force
Copy-Item -Path (Join-Path $AmbientPublish "*") -Destination (Join-Path $Stage "csharp-ambient\bin\Release\net8.0-windows") -Recurse -Force
Copy-Item -Path (Join-Path $AudioPublish "*") -Destination (Join-Path $Stage "csharp-audio\bin\Release\net8.0-windows") -Recurse -Force
Copy-Item -Path (Join-Path $RepoRoot "*.py") -Destination $Stage -Force
Copy-Item -Path (Join-Path $RepoRoot "*.html") -Destination $Stage -Force
Copy-Item -Path (Join-Path $RepoRoot "README.md"),(Join-Path $RepoRoot "SUPPORT.md"),(Join-Path $RepoRoot "PRESENTATION_FR.md"),(Join-Path $RepoRoot "install.ps1") -Destination $Stage -Force
Copy-Item -Path (Join-Path $RepoRoot "docs\github-preview.svg") -Destination (Join-Path $Stage "docs") -Force

@'
@echo off
cd /d "%~dp0"
start "" "%~dp0app\AsusKeyboardFx.exe"
'@ | Set-Content -Path (Join-Path $Stage "START_ASUS_KEYBOARD_FX.cmd") -Encoding ASCII

@'
@echo off
setlocal
cd /d "%~dp0"
set "RGB_STATE_FILE=%~dp0rgb_intensity.txt"
if exist ".\csharp-ambient\bin\Release\net8.0-windows\AmbientBar.exe" (
  ".\csharp-ambient\bin\Release\net8.0-windows\AmbientBar.exe" --profile daily
) else (
  dotnet ".\csharp-ambient\bin\Release\net8.0-windows\AmbientBar.dll" --profile daily
)
'@ | Set-Content -Path (Join-Path $Stage "START_DAILY_AMBILIGHT.cmd") -Encoding ASCII

@"
ASUS Keyboard FX + Ambilight $Version

Quick start:
1. Extract this ZIP anywhere.
2. Install OpenRGB or make sure hidapi.dll exists at C:\Program Files\OpenRGB\hidapi.dll.
3. Double-click START_ASUS_KEYBOARD_FX.cmd for the control panel, or START_DAILY_AMBILIGHT.cmd for the daily mirror profile.

One-command install from GitHub:
powershell -ExecutionPolicy Bypass -NoProfile -Command "irm https://raw.githubusercontent.com/EvazX/asus-rgb-studio/master/install.ps1 | iex"

Notes:
- This is an enthusiast tool, not an official ASUS utility.
- Close Armoury Crate lighting effects if another controller fights the LEDs.
- Tested around an ASUS G513QY-style 4-zone keyboard/lightbar setup.
"@ | Set-Content -Path (Join-Path $Stage "README_RELEASE.txt") -Encoding UTF8

if (Test-Path $Zip) {
    Remove-Item -LiteralPath $Zip -Force
}

Compress-Archive -Path $Stage -DestinationPath $Zip -Force
Get-Item $Zip
