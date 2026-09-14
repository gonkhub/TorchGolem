# Builds Torch Golem, deploys it to the "TorchGolem Dev" r2modman profile, and launches Valheim with that profile.
# Same effect as r2modman's "Start modded", without opening r2modman.
#
#   powershell -ExecutionPolicy Bypass -File tools\launch-dev.ps1 [-NoBuild]
param(
    [switch]$NoBuild,
    [string]$ValheimDir = "C:\Program Files (x86)\Steam\steamapps\common\Valheim",
    [string]$ProfileName = "TorchGolem Dev"
)
$ErrorActionPreference = "Stop"
$profile = Join-Path $env:APPDATA "r2modmanPlus-local\Valheim\profiles\$ProfileName"

if (Get-Process valheim -ErrorAction SilentlyContinue) {
    Write-Host "Valheim is already running; close it so the new DLL can be copied." -ForegroundColor Yellow
    exit 1
}

if (-not $NoBuild) {
    dotnet build (Join-Path $PSScriptRoot "..\TorchGolem.csproj") -c Release -nologo -v q
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# -console enables the vanilla F5 console; the doorstop args point BepInEx at the profile instead of the game folder.
$preloader = Join-Path $profile "BepInEx\core\BepInEx.Preloader.dll"
Start-Process (Join-Path $ValheimDir "valheim.exe") -WorkingDirectory $ValheimDir -ArgumentList @(
    "-console",
    "--doorstop-enabled", "true",
    "--doorstop-target-assembly", "`"$preloader`""
)
Write-Host "Launched Valheim with profile '$ProfileName'. Log: $profile\BepInEx\LogOutput.log"
