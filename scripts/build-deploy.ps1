# Build BoatTracking and copy the DLL into the r2modman profile plugins folder.
# Usage: .\scripts\build-deploy.ps1
# Optional: .\scripts\build-deploy.ps1 -Profile "Default"
# For Thunderstore / GitHub release zips, use: .\scripts\create-release.ps1

param(
  [string]$Profile = "testing"
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path $PSScriptRoot -Parent
$Project = Join-Path $RepoRoot "BoatTracking\BoatTracking.csproj"
$OutDll = Join-Path $RepoRoot "BoatTracking\bin\Release\net4.8\BoatTracking.dll"
$PluginDir = Join-Path $env:APPDATA "r2modmanPlus-local\Valheim\profiles\$Profile\BepInEx\plugins\MattHB1-BoatTracking"

dotnet build $Project -c Release
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

New-Item -ItemType Directory -Force -Path $PluginDir | Out-Null
Copy-Item $OutDll (Join-Path $PluginDir "BoatTracking.dll") -Force
Write-Host "Deployed to $PluginDir\BoatTracking.dll"
Write-Host "Launch Valheim via r2modman (profile: $Profile) to test."
