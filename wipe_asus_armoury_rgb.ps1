$ErrorActionPreference = "Continue"

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

function Stop-RelatedProcesses {
    Write-Step "Stopping ASUS / Armoury / Codex RGB processes"
    $processNames = @(
        "ArmouryCrate",
        "ArmourySocketServer",
        "ArmouryHtmlDebugServer",
        "AacAmbientLighting",
        "python",
        "dotnet"
    )

    foreach ($name in $processNames) {
        Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
            try {
                if ($name -in @("python", "dotnet")) {
                    $cmd = (Get-CimInstance Win32_Process -Filter "ProcessId = $($_.Id)" -ErrorAction SilentlyContinue).CommandLine
                    if ($cmd -notmatch "asus-ambient-led|AsusRgbStudio|AmbientBar|AudioReactive") {
                        return
                    }
                }

                Stop-Process -Id $_.Id -Force -ErrorAction Stop
                Write-Host "Stopped $name (PID $($_.Id))"
            }
            catch {
                Write-Warning "Unable to stop $name (PID $($_.Id))"
            }
        }
    }
}

function Stop-RelatedServices {
    Write-Step "Stopping ASUS / Armoury services"
    $serviceNames = @(
        "LightingService",
        "ArmouryCrateControlInterface",
        "ArmouryCrateService",
        "AsusAppService",
        "ASUSOptimization"
    )

    foreach ($serviceName in $serviceNames) {
        $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
        if (-not $service) {
            continue
        }

        try {
            if ($service.Status -ne "Stopped") {
                Stop-Service -Name $serviceName -Force -ErrorAction Stop
                Write-Host "Stopped service $serviceName"
            }
        }
        catch {
            Write-Warning "Unable to stop service $serviceName : $($_.Exception.Message)"
        }
    }
}

function Remove-AmbientHal {
    Write-Step "Removing ASUS Ambient HAL Appx package"
    try {
        Get-AppxPackage -Name *ASUSAmbientHAL64* | Remove-AppxPackage -ErrorAction Continue
    }
    catch {
        Write-Warning "Ambient HAL remove failed: $($_.Exception.Message)"
    }
}

function Run-ArmouryUninstallTool {
    Write-Step "Launching official Armoury Crate uninstall tool"
    $toolPath = "C:\Program Files\ASUS\Armoury Crate Service\UninstallTool\Armoury Crate Uninstall Tool.exe"
    if (-not (Test-Path $toolPath)) {
        Write-Warning "Official Armoury Crate uninstall tool not found."
        return
    }

    try {
        $proc = Start-Process -FilePath $toolPath -Wait -PassThru
        Write-Host "Armoury Crate uninstall tool exited with code $($proc.ExitCode)"
    }
    catch {
        Write-Warning "Unable to launch Armoury Crate uninstall tool: $($_.Exception.Message)"
    }
}

function Remove-Leftovers {
    Write-Step "Removing leftover ASUS RGB folders"
    $paths = @(
        "C:\Program Files\ASUS\AacAmbientHal",
        "C:\Program Files\ASUS\AURA lighting effect add-on x64",
        "C:\Program Files\ASUS\AuraSDK",
        "C:\Program Files\ASUS\Armoury Crate Service"
    )

    foreach ($path in $paths) {
        if (-not (Test-Path $path)) {
            continue
        }

        try {
            Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop
            Write-Host "Removed $path"
        }
        catch {
            Write-Warning "Unable to remove $path : $($_.Exception.Message)"
        }
    }
}

function Show-NextSteps {
    Write-Step "Next steps"
    Write-Host "1. Reboot Windows."
    Write-Host "2. Reinstall Armoury Crate from ASUS."
    Write-Host "3. Reinstall / repair ASUS System Control Interface if needed."
    Write-Host "4. Check whether keyboard and light bar come back before relaunching any custom RGB tools."
}

Ensure-Admin

Write-Host "ASUS / Armoury RGB wipe script starting..." -ForegroundColor Yellow
Stop-RelatedProcesses
Stop-RelatedServices
Remove-AmbientHal
Run-ArmouryUninstallTool
Stop-RelatedProcesses
Stop-RelatedServices
Remove-Leftovers
Show-NextSteps

Write-Host ""
Write-Host "Wipe script finished." -ForegroundColor Green
