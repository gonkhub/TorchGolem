# Builds a Thunderstore-format zip in dist/ (works with r2modman "Import local mod" and Thunderstore upload).
#
#   powershell -ExecutionPolicy Bypass -File tools\package.ps1
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")

# Version lives in Plugin.cs; the manifest copy is stamped from it so they can't drift.
$pluginSource = Get-Content (Join-Path $root "Plugin.cs") -Raw
if ($pluginSource -notmatch 'Version\s*=\s*"(\d+\.\d+\.\d+)"') { throw "Couldn't find Version in Plugin.cs" }
$version = $Matches[1]

dotnet build (Join-Path $root "TorchGolem.csproj") -c Release -nologo -v q
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$staging = Join-Path $root "dist\staging"
if (Test-Path $staging) { Get-ChildItem $staging | Remove-Item -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $staging "plugins") | Out-Null

$manifest = Get-Content (Join-Path $root "thunderstore\manifest.json") -Raw | ConvertFrom-Json
$manifest.version_number = $version
# Thunderstore rejects a BOM, so write UTF-8 without one.
[IO.File]::WriteAllText((Join-Path $staging "manifest.json"), ($manifest | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding $false))

Copy-Item (Join-Path $root "thunderstore\icon.png") $staging
Copy-Item (Join-Path $root "README.md") $staging
Copy-Item (Join-Path $root "CHANGELOG.md") $staging
Copy-Item (Join-Path $root "bin\Release\TorchGolem.dll") (Join-Path $staging "plugins")

$zip = Join-Path $root "dist\TorchGolem-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
# Compress-Archive in Windows PowerShell writes "plugins\x.dll" entry names; Thunderstore and Linux
# servers need forward slashes, so build the archive by hand.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem $staging -Recurse -File) {
        $entryName = $file.FullName.Substring($staging.Length + 1).Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entryName)
    }
} finally {
    $archive.Dispose()
}
Write-Host "Packaged $zip"
