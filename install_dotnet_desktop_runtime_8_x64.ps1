$ErrorActionPreference = "Stop"

function Ensure-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    $isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if ($isAdmin) {
        return
    }

    $quotedPath = '"' + $PSCommandPath + '"'
    Start-Process powershell.exe -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File $quotedPath"
    exit
}

Ensure-Admin

$downloadUrl = "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe"
$installerPath = Join-Path $env:TEMP "windowsdesktop-runtime-8-x64.exe"

Write-Host ""
Write-Host "Downloading .NET Desktop Runtime 8 x64 from Microsoft..." -ForegroundColor Cyan
Invoke-WebRequest -Uri $downloadUrl -OutFile $installerPath

Write-Host ""
Write-Host "Running installer..." -ForegroundColor Cyan
Start-Process -FilePath $installerPath -ArgumentList "/install", "/quiet", "/norestart" -Wait

Write-Host ""
Write-Host "Installed / repaired .NET Desktop Runtime 8 x64." -ForegroundColor Green
Write-Host "Recommended next step: reboot Windows, then retry Armoury Crate / ASUS lighting." -ForegroundColor Yellow
