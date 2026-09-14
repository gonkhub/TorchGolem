# Creates (or refreshes) an r2modman profile for testing Torch Golem.
# Downloads dev-tool mods from Thunderstore into r2modman's cache, installs them the way r2modman does,
# and writes mods.yml so the profile shows up normally in r2modman.
#
#   powershell -ExecutionPolicy Bypass -File tools\setup-dev-profile.ps1
param(
    [string]$ProfileName = "TorchGolem Dev"
)
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$r2 = Join-Path $env:APPDATA "r2modmanPlus-local\Valheim"
$cacheRoot = Join-Path $r2 "cache"
$profile = Join-Path $r2 "profiles\$ProfileName"

# Order matters only for mods.yml readability; dependencies come first.
$packages = @(
    "denikson/BepInExPack_Valheim",
    "JereKuusela/Server_devcommands",
    "JereKuusela/World_Edit_Commands",
    "JereKuusela/Infinity_Hammer",
    "Azumatt/Official_BepInEx_ConfigurationManager",
    "ValheimModding/UnityExplorer"
)

function Get-Package([string]$id) {
    $info = Invoke-RestMethod "https://thunderstore.io/api/experimental/package/$id/"
    $full = "$($info.namespace)-$($info.name)"
    $ver = $info.latest.version_number
    $dir = Join-Path $cacheRoot "$full\$ver"
    if (-not (Test-Path (Join-Path $dir "manifest.json"))) {
        Write-Host "Downloading $full $ver"
        $zip = Join-Path $env:TEMP "$full-$ver.zip"
        Invoke-WebRequest $info.latest.download_url -OutFile $zip
        New-Item -ItemType Directory -Force $dir | Out-Null
        Expand-Archive $zip -DestinationPath $dir -Force
        Remove-Item $zip
    } else {
        Write-Host "Cached      $full $ver"
    }
    [pscustomobject]@{ Info = $info; Full = $full; Version = $ver; Dir = $dir }
}

function Copy-Into([string]$from, [string]$to) {
    New-Item -ItemType Directory -Force $to | Out-Null
    Copy-Item (Join-Path $from "*") $to -Recurse -Force
}

# Mirrors r2modman's BepInEx install rules for Valheim.
function Install-Package($pkg) {
    if ($pkg.Full -eq "denikson-BepInExPack_Valheim") {
        Copy-Into (Join-Path $pkg.Dir "BepInExPack_Valheim") $profile
        return
    }
    $bep = Join-Path $profile "BepInEx"
    $pluginDir = Join-Path $bep "plugins\$($pkg.Full)"
    New-Item -ItemType Directory -Force $pluginDir | Out-Null

    $src = $pkg.Dir
    if (Test-Path (Join-Path $src "BepInEx")) {
        foreach ($f in Get-ChildItem $src -File) { Copy-Item $f.FullName $pluginDir -Force }
        $src = Join-Path $src "BepInEx"
    }
    foreach ($item in Get-ChildItem $src) {
        switch ($item.Name.ToLower()) {
            "plugins"  { Copy-Into $item.FullName $pluginDir }
            "config"   { Copy-Into $item.FullName (Join-Path $bep "config") }
            "patchers" { Copy-Into $item.FullName (Join-Path $bep "patchers\$($pkg.Full)") }
            "core"     { Copy-Into $item.FullName (Join-Path $bep "core") }
            default    { Copy-Item $item.FullName $pluginDir -Recurse -Force }
        }
    }
}

function Quote([string]$s) { "'" + ($s -replace "'", "''" -replace "\r?\n", " ") + "'" }

function Get-ModsYmlEntry($pkg) {
    $i = $pkg.Info
    $parts = $pkg.Version.Split('.')
    $deps = $i.latest.dependencies
    $depYaml = if ($deps.Count -gt 0) { "`n" + (($deps | ForEach-Object { "    - $_" }) -join "`n") } else { " []" }
    $now = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
@"
- manifestVersion: 1
  name: $($pkg.Full)
  authorName: $($i.namespace)
  websiteUrl: $($i.package_url)
  displayName: $($i.name)
  description: $(Quote $i.latest.description)
  gameVersion: '0'
  networkMode: both
  packageType: other
  installMode: managed
  installedAtTime: $now
  loaders: []
  dependencies:$depYaml
  incompatibilities: []
  optionalDependencies: []
  versionNumber:
    major: $($parts[0])
    minor: $($parts[1])
    patch: $($parts[2])
  enabled: true
  onlineSource: true
  trustedPackage: false
"@
}

New-Item -ItemType Directory -Force $profile | Out-Null
$entries = @()
foreach ($id in $packages) {
    $pkg = Get-Package $id
    Install-Package $pkg
    $entries += Get-ModsYmlEntry $pkg
}
Set-Content (Join-Path $profile "mods.yml") ($entries -join "`n") -Encoding UTF8

New-Item -ItemType Directory -Force (Join-Path $profile "_state") | Out-Null
$state = Join-Path $profile "_state\installation_state.yml"
if (-not (Test-Path $state)) { Set-Content $state "currentState: []" -Encoding UTF8 }

New-Item -ItemType Directory -Force (Join-Path $profile "BepInEx\plugins\TorchGolem") | Out-Null

Write-Host "`nProfile ready: $profile"
