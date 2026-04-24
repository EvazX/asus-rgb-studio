param(
    [string]$RepoName = "asus-keyboard-fx-ambilight",
    [ValidateSet("public", "private")]
    [string]$Visibility = "public",
    [string]$GitUserName = "",
    [string]$GitUserEmail = "",
    [switch]$SkipAuth
)

$ErrorActionPreference = "Stop"

$ProjectRoot = "D:\asus-ambient-led"
$GhPath = "C:\Program Files\GitHub CLI\gh.exe"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Get-GhCommand {
    if (Test-Path -LiteralPath $GhPath) {
        return $GhPath
    }

    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if ($gh) {
        return $gh.Source
    }

    throw "GitHub CLI n'est pas installe. Installe-le d'abord avec: winget install --id GitHub.cli"
}

function Ensure-GitConfig {
    param(
        [string]$Name,
        [string]$Email
    )

    $currentName = (git -C $ProjectRoot config user.name) 2>$null
    $currentEmail = (git -C $ProjectRoot config user.email) 2>$null

    if (-not $currentName) {
        if (-not $Name) {
            $Name = Read-Host "Nom Git a utiliser pour ce repo"
        }
        git -C $ProjectRoot config user.name $Name
    }

    if (-not $currentEmail) {
        if (-not $Email) {
            $Email = Read-Host "Email Git a utiliser pour ce repo"
        }
        git -C $ProjectRoot config user.email $Email
    }
}

Write-Step "Preparation du repo local"

git config --global --add safe.directory $ProjectRoot | Out-Null

if (-not (Test-Path -LiteralPath (Join-Path $ProjectRoot ".git"))) {
    git -C $ProjectRoot init | Out-Host
}

Ensure-GitConfig -Name $GitUserName -Email $GitUserEmail

Write-Step "Verification GitHub CLI"
$gh = Get-GhCommand

if (-not $SkipAuth) {
    Write-Step "Verification de l'authentification GitHub"
    try {
        & $gh auth status | Out-Host
    }
    catch {
        Write-Host "Connexion GitHub requise, ouverture du login..." -ForegroundColor Yellow
        & $gh auth login --hostname github.com --git-protocol https --web
    }
}

Write-Step "Preparation du commit"

$status = git -C $ProjectRoot status --porcelain
if ($status) {
    git -C $ProjectRoot add .
    git -C $ProjectRoot commit -m "Update ASUS Keyboard FX"
}
else {
    Write-Host "Aucun changement local a commit." -ForegroundColor DarkGray
}

Write-Step "Passage sur main"
git -C $ProjectRoot branch -M main

$remote = (git -C $ProjectRoot remote) 2>$null

if (-not $remote) {
    Write-Step "Creation du repo GitHub et push initial"
    & $gh repo create $RepoName --$Visibility --source $ProjectRoot --remote origin --push | Out-Host
}
else {
    Write-Step "Push vers le remote existant"
    git -C $ProjectRoot push -u origin main | Out-Host
}

Write-Step "Termine"
Write-Host "Le projet devrait maintenant etre publie sur GitHub." -ForegroundColor Green
