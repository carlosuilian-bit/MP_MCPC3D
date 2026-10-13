# =============================================================================
# Configure Claude Desktop for Civil 3D MCP Bridge
# =============================================================================
# Run this AFTER installing the plugin if Claude Desktop config wasn't
# set up by the installer, or if you need to reconfigure.
#
# USAGE:
#   powershell -ExecutionPolicy Bypass -File configure-claude.ps1
#   powershell -ExecutionPolicy Bypass -File configure-claude.ps1 -Port 8271 -AppName MicroSurveyCAD
# =============================================================================

param(
    [string]$Port = "8270",
    [string]$AppName = "Civil3D",
    [string]$ServerName = "civil3d",
    [string]$McpServerPath = ""
)

$ErrorActionPreference = "Stop"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Claude Desktop — MCP Server Configuration" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# --- Find Python ---
$PythonPath = $null
$PythonCandidates = @(
    "$env:LOCALAPPDATA\Programs\Python\Python312\python.exe",
    "$env:LOCALAPPDATA\Programs\Python\Python311\python.exe",
    "$env:LOCALAPPDATA\Programs\Python\Python310\python.exe",
    "C:\Python312\python.exe",
    "C:\Python311\python.exe"
)

foreach ($candidate in $PythonCandidates) {
    if (Test-Path $candidate) {
        $PythonPath = $candidate
        break
    }
}

if (-not $PythonPath) {
    # Try PATH
    $PythonPath = (Get-Command python -ErrorAction SilentlyContinue).Source
}

if (-not $PythonPath) {
    Write-Host "ERROR: Python not found. Install Python 3.10+ first." -ForegroundColor Red
    exit 1
}

Write-Host "  Python: $PythonPath" -ForegroundColor Green

# --- Find MCP server script ---
if (-not $McpServerPath) {
    # Check common install locations
    $candidates = @(
        "${env:ProgramFiles}\Civil3dMcp\mcp-server\server.py",
        "${env:ProgramFiles(x86)}\Civil3dMcp\mcp-server\server.py",
        "$PSScriptRoot\..\mcp-server\server.py",
        ".\mcp-server\server.py"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) {
            $McpServerPath = (Resolve-Path $c).Path
            break
        }
    }
}

if (-not $McpServerPath -or -not (Test-Path $McpServerPath)) {
    Write-Host "ERROR: server.py not found. Specify path with -McpServerPath" -ForegroundColor Red
    exit 1
}

Write-Host "  MCP Server: $McpServerPath" -ForegroundColor Green
Write-Host "  Bridge URL: http://localhost:$Port" -ForegroundColor Green
Write-Host "  App Name: $AppName" -ForegroundColor Green
Write-Host ""

# --- Locate Claude Desktop config ---
$ClaudeConfigDir = "$env:LOCALAPPDATA\Claude"
$ClaudeConfigFile = "$ClaudeConfigDir\claude_desktop_config.json"

# --- Build the MCP server entry ---
# Use forward slashes in paths for JSON
$PythonPathJson = $PythonPath.Replace('\', '/')
$McpServerPathJson = $McpServerPath.Replace('\', '/')

$McpEntry = @{
    command = $PythonPathJson
    args = @($McpServerPathJson)
    env = @{
        CAD_BRIDGE_URL = "http://localhost:$Port"
        CAD_APP_NAME = $AppName
    }
}

# --- Read or create config ---
if (Test-Path $ClaudeConfigFile) {
    Write-Host "  Found existing Claude config: $ClaudeConfigFile" -ForegroundColor Yellow
    $config = Get-Content $ClaudeConfigFile -Raw | ConvertFrom-Json

    # Ensure mcpServers exists
    if (-not $config.mcpServers) {
        $config | Add-Member -NotePropertyName "mcpServers" -NotePropertyValue @{}
    }
} else {
    Write-Host "  Creating new Claude config: $ClaudeConfigFile" -ForegroundColor Yellow
    if (-not (Test-Path $ClaudeConfigDir)) {
        New-Item -ItemType Directory -Path $ClaudeConfigDir -Force | Out-Null
    }
    $config = @{
        mcpServers = @{}
    }
}

# --- Add or update the MCP server entry ---
if ($config.mcpServers -is [PSCustomObject]) {
    # Convert PSCustomObject to hashtable for easier manipulation
    $servers = @{}
    $config.mcpServers.PSObject.Properties | ForEach-Object {
        $servers[$_.Name] = $_.Value
    }
    $servers[$ServerName] = $McpEntry
    $config.mcpServers = $servers
} else {
    $config.mcpServers[$ServerName] = $McpEntry
}

# --- Write config ---
$jsonOutput = $config | ConvertTo-Json -Depth 10
$jsonOutput | Set-Content -Path $ClaudeConfigFile -Encoding UTF8

Write-Host ""
Write-Host "  Config written to: $ClaudeConfigFile" -ForegroundColor Green
Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Done! Restart Claude Desktop to apply." -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Quick test:" -ForegroundColor White
Write-Host "    1. Open Civil 3D and type: MCPSTART" -ForegroundColor Gray
Write-Host "    2. Open Claude Desktop" -ForegroundColor Gray
Write-Host "    3. Ask: 'List all COGO points in my drawing'" -ForegroundColor Gray
Write-Host ""

# --- Also install pip dependencies if not already present ---
Write-Host "Checking Python dependencies..." -ForegroundColor Yellow
& $PythonPath -c "import fastmcp; import httpx" 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "  Installing fastmcp and httpx..." -ForegroundColor Yellow
    & $PythonPath -m pip install fastmcp httpx --quiet
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  Dependencies installed." -ForegroundColor Green
    } else {
        Write-Host "  WARNING: pip install failed. Run manually:" -ForegroundColor Red
        Write-Host "    $PythonPath -m pip install fastmcp httpx" -ForegroundColor Gray
    }
} else {
    Write-Host "  Dependencies already installed." -ForegroundColor Green
}
