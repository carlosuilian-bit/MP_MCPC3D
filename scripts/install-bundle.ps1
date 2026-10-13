[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot "package.ps1") -Configuration $Configuration

$Source = Join-Path $Root "artifacts\Civil3D.HybridMcp.bundle"
$TargetRoot = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins"
$Target = Join-Path $TargetRoot "Civil3D.HybridMcp.bundle"

if (-not (Test-Path (Join-Path $Source "PackageContents.xml"))) {
    throw "Bundle package was not found after packaging: $Source"
}

if ($PSCmdlet.ShouldProcess($Target, "Install Civil3D Hybrid MCP bundle")) {
    New-Item -ItemType Directory -Force -Path $TargetRoot | Out-Null
    if (Test-Path $Target) {
        Remove-Item -LiteralPath $Target -Recurse -Force
    }
    Copy-Item -LiteralPath $Source -Destination $TargetRoot -Recurse
    Write-Host "Installed bundle: $Target"
}
