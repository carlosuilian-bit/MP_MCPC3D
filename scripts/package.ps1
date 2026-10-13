[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$BundleSource = Join-Path $Root "packaging\Civil3D.HybridMcp.bundle"
$BridgeProject = Join-Path $Root "src\Civil3D.HybridMcp.Bridge\Civil3D.HybridMcp.Bridge.csproj"

# Read the target framework from the project rather than hardcoding it. The
# Civil 3D 2026 update of 2026-09-22 moved the Autodesk host from .NET 8 to
# .NET 10, and a hardcoded net8.0-windows path would then package an empty or
# stale bridge without failing.
$TargetFramework = ([xml](Get-Content -LiteralPath $BridgeProject -Raw)).Project.PropertyGroup.TargetFramework |
    Where-Object { $_ } |
    Select-Object -First 1
if (-not $TargetFramework) {
    throw "Could not read TargetFramework from $BridgeProject."
}

$BridgeOutput = Join-Path $Root "src\Civil3D.HybridMcp.Bridge\bin\$Configuration\$TargetFramework"
$Dist = Join-Path $Root "artifacts\Civil3D.HybridMcp.bundle"
$DistContents = Join-Path $Dist "Contents\Win64"

dotnet build $BridgeProject -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Bridge build failed; nothing was packaged."
}

if (-not (Test-Path -LiteralPath (Join-Path $BridgeOutput "Civil3D.HybridMcp.Bridge.dll"))) {
    throw "Bridge output not found at $BridgeOutput; nothing was packaged."
}

if (Test-Path $Dist) {
    Remove-Item -LiteralPath $Dist -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $DistContents | Out-Null
Copy-Item -LiteralPath (Join-Path $BundleSource "PackageContents.xml") -Destination (Join-Path $Dist "PackageContents.xml")
Get-ChildItem -LiteralPath $BridgeOutput -Include "Civil3D.HybridMcp.*.dll", "Civil3D.HybridMcp.*.pdb", "Civil3D.HybridMcp.*.deps.json", "Civil3D.HybridMcp.*.runtimeconfig.json" -File -Recurse |
    Copy-Item -Destination $DistContents

Write-Host "Packaged bundle: $Dist"
