param(
    [switch]$Apply,
    [switch]$IncludeReleases
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = (Resolve-Path $root).Path

$directoryPatterns = @(
    "__pycache__",
    "bin",
    "obj",
    ".vs"
)

if ($IncludeReleases) {
    $directoryPatterns += "dist"
    $directoryPatterns += "release"
}

$filePatterns = @(
    "*.pyc",
    "*.pyo",
    "*.pyd",
    "*.tmp",
    "*.log",
    "rgb_effect_pids.txt",
    "rgb_intensity.txt"
)

$targets = New-Object System.Collections.Generic.List[string]

foreach ($pattern in $directoryPatterns) {
    Get-ChildItem -LiteralPath $root -Recurse -Force -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq $pattern } |
        ForEach-Object { $targets.Add($_.FullName) }
}

foreach ($pattern in $filePatterns) {
    Get-ChildItem -LiteralPath $root -Recurse -Force -File -Filter $pattern -ErrorAction SilentlyContinue |
        ForEach-Object { $targets.Add($_.FullName) }
}

$safeTargets = $targets |
    Sort-Object -Unique |
    Where-Object {
        $full = (Resolve-Path -LiteralPath $_).Path
        $full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -and
        $full.IndexOf("\.git\", [StringComparison]::OrdinalIgnoreCase) -lt 0 -and
        ($IncludeReleases -or (
            $full.IndexOf("\release\", [StringComparison]::OrdinalIgnoreCase) -lt 0 -and
            $full.IndexOf("\dist\", [StringComparison]::OrdinalIgnoreCase) -lt 0
        ))
    }

$safeTargets = @($safeTargets | ForEach-Object {
    $candidate = $_.TrimEnd("\")
    $coveredByParent = $false
    foreach ($other in $safeTargets) {
        $parent = $other.TrimEnd("\")
        if ($candidate.Length -gt $parent.Length -and
            $candidate.StartsWith($parent + "\", [StringComparison]::OrdinalIgnoreCase)) {
            $coveredByParent = $true
            break
        }
    }

    if (-not $coveredByParent) {
        $_
    }
})

if (-not $safeTargets) {
    Write-Host "Rien a nettoyer." -ForegroundColor Green
    exit 0
}

if (-not $Apply) {
    Write-Host "Apercu nettoyage. Rien n'est supprime." -ForegroundColor Cyan
    Write-Host "Relance avec -Apply pour supprimer. Ajoute -IncludeReleases pour enlever dist/ et release/." -ForegroundColor DarkCyan
    $safeTargets | ForEach-Object { Write-Host "  $_" }
    exit 0
}

foreach ($target in $safeTargets) {
    try {
        if (Test-Path -LiteralPath $target -PathType Container) {
            Remove-Item -LiteralPath $target -Recurse -Force
            Write-Host "Supprime dossier: $target" -ForegroundColor Yellow
        }
        elseif (Test-Path -LiteralPath $target -PathType Leaf) {
            Remove-Item -LiteralPath $target -Force
            Write-Host "Supprime fichier: $target" -ForegroundColor Yellow
        }
    }
    catch {
        Write-Host "Ignore verrouille: $target" -ForegroundColor DarkYellow
        Write-Host "  $($_.Exception.Message)" -ForegroundColor DarkGray
    }
}

Write-Host "Nettoyage termine." -ForegroundColor Green
