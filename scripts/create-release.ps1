# Build BoatTracking and package Thunderstore + GitHub release artifacts.
#
# Usage:
#   .\scripts\create-release.ps1
#   .\scripts\create-release.ps1 -GitHubRelease

param(
  [switch]$GitHubRelease
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path $PSScriptRoot -Parent
$Project = Join-Path $RepoRoot "BoatTracking\BoatTracking.csproj"
$PluginCs = Join-Path $RepoRoot "BoatTracking\BoatTracking.cs"
$OutDll = Join-Path $RepoRoot "BoatTracking\bin\Release\net4.8\BoatTracking.dll"
$Icon = Join-Path $RepoRoot "resources\icon.png"
$PackageReadme = Join-Path $RepoRoot "publish\README.md"
$Changelog = Join-Path $RepoRoot "CHANGELOG.md"

if (-not (Test-Path $Icon)) { throw "Missing Thunderstore icon: $Icon (256x256 PNG)" }
if (-not (Test-Path $PackageReadme)) { throw "Missing package README: $PackageReadme" }
if (-not (Test-Path $Changelog)) { throw "Missing changelog: $Changelog" }

$versionLine = Select-String -Path $PluginCs -Pattern 'public const string VERSION = "([^"]+)"' | Select-Object -First 1
if (-not $versionLine) { throw "Could not parse VERSION from BoatTracking.cs" }
$Version = $versionLine.Matches[0].Groups[1].Value

$ReleaseDir = Join-Path $RepoRoot "release\$Version"
$TempDir = Join-Path $RepoRoot "release\temp"

Write-Host "Building BoatTracking $Version ..."
dotnet build $Project -c Release
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
if (-not (Test-Path $OutDll)) { throw "Missing build output: $OutDll" }

Remove-Item -Recurse -Force $ReleaseDir, $TempDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $ReleaseDir | Out-Null

Copy-Item $OutDll (Join-Path $ReleaseDir "BoatTracking.dll") -Force

$Ts = Join-Path $TempDir "Thunderstore"
$TsPlugins = Join-Path $Ts "BepInEx\plugins"
New-Item -ItemType Directory -Force -Path $TsPlugins | Out-Null
Copy-Item $Icon (Join-Path $Ts "icon.png") -Force
Copy-Item $PackageReadme (Join-Path $Ts "README.md") -Force
Copy-Item $Changelog (Join-Path $Ts "CHANGELOG.md") -Force
Copy-Item $OutDll (Join-Path $TsPlugins "BoatTracking.dll") -Force

$manifest = @{
  name            = "BoatTracking"
  version_number  = $Version
  website_url     = "https://github.com/MattHB1/valheim-boat-tracking"
  description     = "Name your ships with hold E and see them on the map anywhere - works on dedicated servers."
  dependencies    = @("denikson-BepInExPack_Valheim-5.4.2350")
} | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $Ts "manifest.json"), $manifest + "`n")

$TsZip = Join-Path $ReleaseDir "Thunderstore.zip"
if (Test-Path $TsZip) { Remove-Item $TsZip -Force }
Compress-Archive -Path (Join-Path $Ts "*") -DestinationPath $TsZip -Force

Remove-Item -Recurse -Force $TempDir -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Release artifacts in $ReleaseDir"
Get-ChildItem $ReleaseDir | Format-Table Name, Length
Write-Host "Upload Thunderstore.zip at https://thunderstore.io/c/valheim/create/package/"

if ($GitHubRelease) {
  $tag = "v$Version"
  $dllAsset = Join-Path $ReleaseDir "BoatTracking.dll"
  $notes = "Name your ships with hold E and see them on the map anywhere - works on dedicated servers. Install on server and clients."
  gh release view $tag -R MattHB1/valheim-boat-tracking 2>$null
  if ($LASTEXITCODE -eq 0) {
    Write-Host "GitHub release $tag already exists; uploading assets..."
    gh release upload $tag $dllAsset $TsZip -R MattHB1/valheim-boat-tracking --clobber
  } else {
    gh release create $tag $dllAsset $TsZip -R MattHB1/valheim-boat-tracking --title "BoatTracking $Version" --notes $notes
  }
  Write-Host "GitHub release: https://github.com/MattHB1/valheim-boat-tracking/releases/tag/$tag"
}
