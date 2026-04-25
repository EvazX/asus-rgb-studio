param(
    [string]$InstallDir = "$env:LOCALAPPDATA\AsusKeyboardFx"
)

$ErrorActionPreference = "Stop"
$Repo = "EvazX/asus-rgb-studio"
$ApiUrl = "https://api.github.com/repos/$Repo/releases/latest"

function Write-Step {
    param([string]$Message)
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Test-DotNetDesktopRuntime {
    try {
        $runtimes = & dotnet --list-runtimes 2>$null
        return [bool]($runtimes | Where-Object { $_ -match "Microsoft.WindowsDesktop.App 8\." })
    }
    catch {
        return $false
    }
}

Write-Step "Reading latest GitHub release"
$release = Invoke-RestMethod -Uri $ApiUrl -Headers @{ "User-Agent" = "AsusKeyboardFxInstaller" }
$asset = $release.assets | Where-Object { $_.name -like "*.zip" } | Select-Object -First 1

if (-not $asset) {
    throw "No ZIP asset found on the latest GitHub release."
}

$tempRoot = Join-Path $env:TEMP "AsusKeyboardFxInstall"
$zipPath = Join-Path $tempRoot $asset.name
$extractPath = Join-Path $tempRoot "extract"

if (Test-Path $tempRoot) {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $tempRoot | Out-Null
New-Item -ItemType Directory -Path $extractPath | Out-Null

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

$launcher = Join-Path $InstallDir "START_ASUS_KEYBOARD_FX.cmd"
if (-not (Test-Path $launcher)) {
    throw "Launcher not found after install: $launcher"
}

Write-Step "Creating desktop shortcut"
$desktop = [Environment]::GetFolderPath("Desktop")
$shortcutPath = Join-Path $desktop "ASUS Keyboard FX.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = "cmd.exe"
$shortcut.Arguments = "/c `"$launcher`""
$shortcut.WorkingDirectory = $InstallDir
$shortcut.IconLocation = Join-Path $InstallDir "rgb-control-ui\bin\Release\net8.0-windows\app.ico"
$shortcut.Save()

Write-Host ""
Write-Host "Installed ASUS Keyboard FX to:" -ForegroundColor Green
Write-Host "  $InstallDir"
Write-Host ""
Write-Host "Desktop shortcut created:" -ForegroundColor Green
Write-Host "  $shortcutPath"
Write-Host ""

if (-not (Test-DotNetDesktopRuntime)) {
    Write-Host "Important: .NET 8 Desktop Runtime x64 is required." -ForegroundColor Yellow
    Write-Host "Download: https://dotnet.microsoft.com/en-us/download/dotnet/8.0"
}

if (-not (Test-Path "C:\Program Files\OpenRGB\hidapi.dll")) {
    Write-Host "Important: hidapi.dll was not found at C:\Program Files\OpenRGB\hidapi.dll." -ForegroundColor Yellow
    Write-Host "Install OpenRGB or place hidapi.dll there before using hardware effects."
}

Write-Host ""
Write-Host "You can now launch ASUS Keyboard FX from the desktop shortcut." -ForegroundColor Green
