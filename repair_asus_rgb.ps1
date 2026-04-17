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

function Write-Step([string]$message) {
    Write-Host ""
    Write-Host "==> $message" -ForegroundColor Cyan
}

function Stop-CodexRgbProcesses {
    Write-Step "Stopping lingering ASUS RGB test processes"
    $targets = Get-CimInstance Win32_Process | Where-Object {
        ($_.Name -in @("python.exe", "dotnet.exe")) -and
        $_.CommandLine -match "asus-ambient-led|AsusRgbStudio|AudioReactive|AmbientBar"
    }

    foreach ($process in $targets) {
        try {
            Stop-Process -Id $process.ProcessId -Force -ErrorAction Stop
            Write-Host "Stopped PID $($process.ProcessId): $($process.Name)"
        }
        catch {
            Write-Warning "Unable to stop PID $($process.ProcessId): $($_.Exception.Message)"
        }
    }
}

function Stop-AsusLightingApps {
    Write-Step "Stopping ASUS lighting user applications"
    $names = @(
        "AacAmbientLighting",
        "ArmouryCrate",
        "ArmourySocketServer",
        "ArmouryHtmlDebugServer"
    )

    foreach ($name in $names) {
        $processes = Get-Process -Name $name -ErrorAction SilentlyContinue
        foreach ($process in $processes) {
            try {
                Stop-Process -Id $process.Id -Force -ErrorAction Stop
                Write-Host "Stopped $name (PID $($process.Id))"
            }
            catch {
                Write-Warning "Unable to stop $name (PID $($process.Id)) : $($_.Exception.Message)"
            }
        }
    }
}

function Restart-AsusServices {
    Write-Step "Restarting ASUS / Armoury services"
    $serviceNames = @(
        "LightingService",
        "ArmouryCrateService",
        "ArmouryCrateControlInterface",
        "AsusAppService",
        "ASUSOptimization"
    )

    foreach ($serviceName in $serviceNames) {
        $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
        if (-not $service) {
            Write-Warning "Service not found: $serviceName"
            continue
        }

        try {
            Restart-Service -Name $serviceName -Force -ErrorAction Stop
            Write-Host "Restarted $serviceName"
        }
        catch {
            try {
                Start-Service -Name $serviceName -ErrorAction Stop
                Write-Host "Started $serviceName"
            }
            catch {
                Write-Warning "Unable to restart/start $serviceName : $($_.Exception.Message)"
            }
        }
    }
}

function Reinstall-AmbientHal {
    $scriptPath = "C:\Program Files\ASUS\AacAmbientHal\installpackage.ps1"
    if (-not (Test-Path $scriptPath)) {
        Write-Warning "Ambient HAL install script not found."
        return
    }

    Write-Step "Reinstalling ASUS Ambient HAL package"
    try {
        $package = Get-AppxPackage *AmbientHAL* -ErrorAction SilentlyContinue
        if ($package) {
            try {
                Remove-AppxPackage -Package $package.PackageFullName -ErrorAction Stop
                Write-Host "Removed existing Ambient HAL package: $($package.PackageFullName)"
            }
            catch {
                Write-Warning "Ambient HAL package removal skipped: $($_.Exception.Message)"
            }
        }

        Add-AppxPackage "$Env:ProgramW6432\ASUS\AacAmbientHal\AacAmbientLightingPkg.msix" -ExternalLocation "$Env:ProgramW6432\ASUS\AacAmbientHal"
        Write-Host "Ambient HAL package install attempted."
    }
    catch {
        Write-Warning "Ambient HAL reinstall failed: $($_.Exception.Message)"
    }
}

function Reinstall-AsusSci {
    $infPath = "C:\Windows\System32\DriverStore\FileRepository\asussci2.inf_amd64_83c27eae980df155\asussci2.inf"
    if (-not (Test-Path $infPath)) {
        Write-Warning "ASUS System Control Interface INF not found at expected path."
        return
    }

    Write-Step "Reinstalling ASUS System Control Interface"
    pnputil /add-driver $infPath /install
}

function Show-ServiceState {
    Write-Step "Current ASUS service state"
    Get-Service | Where-Object {
        $_.Name -in @("LightingService", "ArmouryCrateService", "ArmouryCrateControlInterface", "AsusAppService", "ASUSOptimization")
    } | Select-Object Status, Name, DisplayName | Format-Table -AutoSize
}

Ensure-Admin

Write-Host "ASUS RGB repair script starting..." -ForegroundColor Green
Stop-CodexRgbProcesses
Stop-AsusLightingApps
Restart-AsusServices
Reinstall-AmbientHal
Reinstall-AsusSci
Restart-AsusServices
Show-ServiceState

Write-Host ""
Write-Host "Repair steps completed." -ForegroundColor Green
Write-Host "Recommended next step: reboot Windows, then open Armoury Crate and check the keyboard lighting." -ForegroundColor Yellow
