param(
    [string]$InstallDir = "$env:LOCALAPPDATA\AsusKeyboardFx",
    [switch]$NoLaunch,
    [switch]$NoDesktopShortcut
)

$ErrorActionPreference = "Stop"
$Repo = "EvazX/asus-rgb-studio"
$ApiUrl = "https://api.github.com/repos/$Repo/releases/latest"

function Write-Step {
    param([string]$Message)
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function New-AppShortcut {
    param(
        [string]$ShortcutPath,
        [string]$TargetPath,
        [string]$WorkingDirectory
    )

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.Arguments = ""
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.IconLocation = $TargetPath
    $shortcut.Description = "ASUS Keyboard FX + Ambilight"
    $shortcut.Save()
}

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

Write-Step "Reading latest GitHub release"
$release = Invoke-RestMethod -Uri $ApiUrl -Headers @{ "User-Agent" = "AsusKeyboardFxInstaller" }
$asset = $release.assets |
    Where-Object { $_.name -like "asus-rgb-studio-*.zip" } |
    Sort-Object -Property name -Descending |
    Select-Object -First 1

if (-not $asset) {
    throw "No packaged ZIP asset found on the latest GitHub release. The project owner must attach the release ZIP before this one-command installer can work."
}

$tempRoot = Join-Path $env:TEMP "AsusKeyboardFxInstall"
$zipPath = Join-Path $tempRoot $asset.name
$extractPath = Join-Path $tempRoot "extract"

if (Test-Path $tempRoot) {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $tempRoot | Out-Null
New-Item -ItemType Directory -Path $extractPath | Out-Null

Write-Step "Stopping previous app instance"
Get-Process AsusKeyboardFx -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Step "Downloading $($asset.name)"
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zipPath -UseBasicParsing

Write-Step "Extracting package"
Expand-Archive -Path $zipPath -DestinationPath $extractPath -Force

$packageRoot = Get-ChildItem -Path $extractPath -Directory | Select-Object -First 1
if (-not $packageRoot) {
    throw "The downloaded ZIP does not contain a package folder."
}

if (Test-Path $InstallDir) {
    Write-Step "Replacing previous install"
    Remove-Item -LiteralPath $InstallDir -Recurse -Force
}

New-Item -ItemType Directory -Path (Split-Path $InstallDir -Parent) -Force | Out-Null
Move-Item -Path $packageRoot.FullName -Destination $InstallDir

$appExe = Join-Path $InstallDir "app\AsusKeyboardFx.exe"
$launcher = Join-Path $InstallDir "START_ASUS_KEYBOARD_FX.cmd"
if (-not (Test-Path $appExe)) {
    throw "Application executable not found after install: $appExe"
}

if (-not $NoDesktopShortcut) {
    Write-Step "Creating desktop shortcut"
    $desktop = [Environment]::GetFolderPath("Desktop")
    $desktopShortcut = Join-Path $desktop "ASUS Keyboard FX.lnk"
    New-AppShortcut -ShortcutPath $desktopShortcut -TargetPath $appExe -WorkingDirectory $InstallDir
}

Write-Step "Creating Start Menu shortcut"
$startMenu = Join-Path ([Environment]::GetFolderPath("StartMenu")) "Programs"
$startMenuShortcut = Join-Path $startMenu "ASUS Keyboard FX.lnk"
New-AppShortcut -ShortcutPath $startMenuShortcut -TargetPath $appExe -WorkingDirectory $InstallDir

Write-Host ""
Write-Host "Installed ASUS Keyboard FX to:" -ForegroundColor Green
Write-Host "  $InstallDir"
Write-Host ""
Write-Host "Shortcut created:" -ForegroundColor Green
Write-Host "  $startMenuShortcut"
Write-Host ""

if (-not (Test-Path "C:\Program Files\OpenRGB\hidapi.dll")) {
    Write-Host "Important: hidapi.dll was not found at C:\Program Files\OpenRGB\hidapi.dll." -ForegroundColor Yellow
    Write-Host "Install OpenRGB or place hidapi.dll there before using hardware effects."
}

Write-Host ""
if (-not $NoLaunch) {
    Write-Step "Launching ASUS Keyboard FX"
    Start-Process -FilePath $appExe -WorkingDirectory $InstallDir
}

if (Test-Path $tempRoot) {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force
}

Write-Host "ASUS Keyboard FX is ready." -ForegroundColor Green
