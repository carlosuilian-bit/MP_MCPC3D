[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Root "src\Civil3D.HybridMcp.Server\Civil3D.HybridMcp.Server.csproj"
dotnet run --project $Project -- @Arguments

