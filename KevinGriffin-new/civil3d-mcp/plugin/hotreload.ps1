# Sidecar hot-reload builder for Civil3dMcpBridge.
#
# Produces a version-suffixed copy of the plugin that can be NETLOADed
# alongside an already-loaded copy without restarting Civil 3D. Each
# version gets its own assembly name, namespace, class, and command
# names so nothing collides.
#
# Usage:
#   .\hotreload.ps1 -Version 6
#   (in Civil 3D)  MCPSTOP          # stop currently-running bridge (frees port 8270)
#                  NETLOAD          # pick plugin\hotreload\v6\Civil3dMcpBridge_v6.dll
#                  MCPSTART6        # start new bridge on same port
#
# After testing, bump the version for the next iteration.

param(
    [Parameter(Mandatory = $true)]
    [int]$Version
)

$ErrorActionPreference = "Stop"

$scriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcCs      = Join-Path $scriptDir "Civil3dMcpBridge.cs"
$srcCsproj  = Join-Path $scriptDir "Civil3dMcpBridge.csproj"
$dstRoot    = Join-Path $scriptDir "hotreload"
$dstDir     = Join-Path $dstRoot  "v$Version"
$dstCs      = Join-Path $dstDir   "Civil3dMcpBridge_v$Version.cs"
$dstCsproj  = Join-Path $dstDir   "Civil3dMcpBridge_v$Version.csproj"

if (-not (Test-Path $srcCs)) { throw "Source not found: $srcCs" }
if (-not (Test-Path $srcCsproj)) { throw "Source csproj not found: $srcCsproj" }

New-Item -ItemType Directory -Force $dstDir | Out-Null

# --- Transform the .cs file ---
# Rename namespace, class, and command attributes so nothing collides
# with a previously-loaded version in the same AutoCAD AppDomain.
$content = Get-Content $srcCs -Raw

$content = $content -replace '\[CommandMethod\("MCPSTART"\)\]',  "[CommandMethod(`"MCPSTART$Version`")]"
$content = $content -replace '\[CommandMethod\("MCPSTOP"\)\]',   "[CommandMethod(`"MCPSTOP$Version`")]"
$content = $content -replace '\[CommandMethod\("MCPSTATUS"\)\]', "[CommandMethod(`"MCPSTATUS$Version`")]"
$content = $content -replace 'namespace Civil3dMcpBridge',       "namespace Civil3dMcpBridgeV$Version"
$content = $content -replace '\[assembly: CommandClass\(typeof\(Civil3dMcpBridge\.BridgeCommands\)\)\]', `
                              "[assembly: CommandClass(typeof(Civil3dMcpBridgeV$Version.BridgeCommands))]"

Set-Content -Path $dstCs -Value $content -NoNewline -Encoding UTF8

# --- Build csproj by taking the main one and swapping AssemblyName + Compile include ---
$csproj = Get-Content $srcCsproj -Raw
$csproj = $csproj -replace '<AssemblyName>Civil3dMcpBridge</AssemblyName>', "<AssemblyName>Civil3dMcpBridge_v$Version</AssemblyName>"
$csproj = $csproj -replace '<RootNamespace>Civil3dMcpBridge</RootNamespace>', "<RootNamespace>Civil3dMcpBridgeV$Version</RootNamespace>"

# Force SDK glob to NOT pick up the main plugin directory and add explicit Compile Include
$compileOverride = @"

  <ItemGroup>
    <Compile Remove="**/*.cs" />
    <Compile Include="Civil3dMcpBridge_v$Version.cs" />
  </ItemGroup>
</Project>
"@
$csproj = $csproj -replace '</Project>', $compileOverride

Set-Content -Path $dstCsproj -Value $csproj -NoNewline -Encoding UTF8

Write-Host "Generated sidecar sources:"
Write-Host "  $dstCs"
Write-Host "  $dstCsproj"
Write-Host ""
Write-Host "Building..."

$buildOut = & dotnet build $dstCsproj -c Release -o $dstDir 2>&1
$buildExit = $LASTEXITCODE

if ($buildExit -ne 0) {
    Write-Host "BUILD FAILED:" -ForegroundColor Red
    $buildOut | ForEach-Object { Write-Host $_ }
    exit 1
}

$outDll = Join-Path $dstDir "Civil3dMcpBridge_v$Version.dll"
if (-not (Test-Path $outDll)) {
    Write-Host "Build reported success but DLL not found at $outDll" -ForegroundColor Red
    exit 1
}

$fi = Get-Item $outDll
Write-Host ""
Write-Host "OK -> $outDll ($($fi.Length) bytes, $($fi.LastWriteTime))" -ForegroundColor Green
Write-Host ""
Write-Host "In Civil 3D command line, run:" -ForegroundColor Cyan
Write-Host "  MCPSTOP                  (stop old bridge, frees port 8270)"
Write-Host "  NETLOAD                  (pick $outDll)"
Write-Host "  MCPSTART$Version         (start this version's bridge)"
