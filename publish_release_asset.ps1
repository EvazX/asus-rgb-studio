param(
    [string]$Version = "v0.1.2"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$Zip = Join-Path $RepoRoot "release\asus-rgb-studio-$Version.zip"

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI is required for release upload. Install it, reopen PowerShell, then run this script again."
}

if (-not (Test-Path $Zip)) {
    throw "Release ZIP not found: $Zip. Run build_release.ps1 first."
}

$notes = @"
ASUS Keyboard FX + Ambilight $Version

Highlights:
- self-contained Windows package
- tray/flyout control app
- Ambilight, mirror, audio and handcrafted keyboard effects
- duplicate-instance protection

Install with one PowerShell command:
powershell -ExecutionPolicy Bypass -NoProfile -Command "irm https://raw.githubusercontent.com/EvazX/asus-rgb-studio/master/install.ps1 | iex"
"@

$existing = gh release view $Version 2>$null
if ($LASTEXITCODE -ne 0) {
    gh release create $Version $Zip --title "ASUS Keyboard FX + Ambilight $Version" --notes $notes
}
else {
    gh release upload $Version $Zip --clobber
}

Write-Host "GitHub release asset published: $Zip" -ForegroundColor Green
