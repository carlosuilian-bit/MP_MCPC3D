[CmdletBinding(SupportsShouldProcess = $true)]
param()

$ErrorActionPreference = "Stop"
$Target = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\Civil3D.HybridMcp.bundle"

if (Test-Path $Target) {
    if ($PSCmdlet.ShouldProcess($Target, "Uninstall Civil3D Hybrid MCP bundle")) {
        Remove-Item -LiteralPath $Target -Recurse -Force
        Write-Host "Removed bundle: $Target"
    }
} else {
    Write-Host "Bundle is not installed: $Target"
}

