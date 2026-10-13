# build-next.ps1
# One step: figure out the next hot-reload version number and build it by
# calling the existing hotreload.ps1. You normally don't run this directly -
# just double-click build-next.bat.

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

# 1) Make sure the .NET SDK is available.
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: the .NET SDK ('dotnet') was not found on your PATH." -ForegroundColor Red
    Write-Host "Install it from https://dotnet.microsoft.com/download, reopen the" -ForegroundColor Yellow
    Write-Host "terminal, and run this again." -ForegroundColor Yellow
    exit 1
}

# 2) Next version = highest existing hotreload\vN folder + 1.
$hr = Join-Path $here "hotreload"
$nums = @()
if (Test-Path $hr) {
    $nums = Get-ChildItem -Directory $hr -Filter "v*" |
        ForEach-Object { $_.Name -replace '^v','' } |
        Where-Object { $_ -match '^\d+$' } |
        ForEach-Object { [int]$_ }
}
$next = if ($nums) { (($nums | Measure-Object -Maximum).Maximum + 1) } else { 1 }

Write-Host ""
Write-Host "==> Auto-selected next version: v$next" -ForegroundColor Cyan
Write-Host ""

# 3) Build it. hotreload.ps1 compiles the current Civil3dMcpBridge.cs into a
#    version-isolated DLL and, on success, prints the MCPSTOP / NETLOAD /
#    MCPSTART steps to run inside Civil 3D. On failure it prints the compiler
#    errors and exits non-zero (Civil 3D is left untouched).
& (Join-Path $here "hotreload.ps1") -Version $next
