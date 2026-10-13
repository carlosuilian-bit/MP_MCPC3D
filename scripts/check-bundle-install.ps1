[CmdletBinding()]
param(
    [string]$BundlePath = (Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\Civil3D.HybridMcp.bundle"),

    [switch]$AsJson
)

$ErrorActionPreference = "Stop"

$Issues = New-Object System.Collections.Generic.List[string]
$Warnings = New-Object System.Collections.Generic.List[string]

function Add-Issue {
    param([string]$Message)
    $Issues.Add($Message) | Out-Null
}

function Test-ManifestAttribute {
    param(
        [System.Xml.XmlNode]$Node,
        [string]$Name,
        [string]$Expected
    )

    $actual = $Node.$Name
    if ($actual -ne $Expected) {
        Add-Issue "$($Node.Name).$Name expected '$Expected' but found '$actual'."
    }
}

function Get-RelativePath {
    param(
        [string]$Root,
        [string]$Path
    )

    $rootUri = New-Object System.Uri(($Root.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar))
    $pathUri = New-Object System.Uri($Path)
    return [System.Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString()).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
}

$RequiredContentFiles = @(
    "Civil3D.HybridMcp.Bridge.dll",
    "Civil3D.HybridMcp.Bridge.deps.json",
    "Civil3D.HybridMcp.Bridge.runtimeconfig.json",
    "Civil3D.HybridMcp.Core.dll",
    "Civil3D.HybridMcp.Diagnostics.dll",
    "Civil3D.HybridMcp.Protocol.dll",
    "Civil3D.HybridMcp.Sdk.dll",
    "Civil3D.HybridMcp.Tools.AutoCAD.dll",
    "Civil3D.HybridMcp.Tools.Civil.dll",
    "Civil3D.HybridMcp.Tools.Survey.dll",
    "Civil3D.HybridMcp.Tools.Roadway.dll"
)

$resolvedBundlePath = [System.IO.Path]::GetFullPath($BundlePath)
$manifestPath = Join-Path $resolvedBundlePath "PackageContents.xml"
$contentsPath = Join-Path $resolvedBundlePath "Contents\Win64"

if (-not (Test-Path -LiteralPath $resolvedBundlePath -PathType Container)) {
    Add-Issue "Bundle directory not found: $resolvedBundlePath"
}

$manifest = $null
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    Add-Issue "PackageContents.xml not found: $manifestPath"
}
else {
    try {
        [xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
    }
    catch {
        Add-Issue "PackageContents.xml could not be parsed as XML: $($_.Exception.Message)"
    }
}

if ($manifest -ne $null) {
    $package = $manifest.ApplicationPackage
    if ($package -eq $null) {
        Add-Issue "PackageContents.xml root ApplicationPackage node was not found."
    }
    else {
        Test-ManifestAttribute $package "AutodeskProduct" "Civil 3D"
        Test-ManifestAttribute $package "ProductType" "Application"
        Test-ManifestAttribute $package "Name" "Civil3D.HybridMcp"
    }

    $runtime = $manifest.SelectSingleNode("/ApplicationPackage/Components/RuntimeRequirements")
    if ($runtime -eq $null) {
        Add-Issue "RuntimeRequirements node was not found."
    }
    else {
        Test-ManifestAttribute $runtime "OS" "Win64"
        Test-ManifestAttribute $runtime "Platform" "AutoCAD*"
        Test-ManifestAttribute $runtime "SeriesMin" "R25.1"
        Test-ManifestAttribute $runtime "SeriesMax" "R25.1"
    }

    $entry = $manifest.SelectSingleNode("/ApplicationPackage/Components/ComponentEntry[@AppName='Civil3D.HybridMcp.Bridge']")
    if ($entry -eq $null) {
        Add-Issue "ComponentEntry for Civil3D.HybridMcp.Bridge was not found."
    }
    else {
        Test-ManifestAttribute $entry "AppType" ".NET"
        Test-ManifestAttribute $entry "LoadOnAutoCADStartup" "True"

        $moduleName = [string]$entry.ModuleName
        if ([string]::IsNullOrWhiteSpace($moduleName)) {
            Add-Issue "ComponentEntry.ModuleName is empty."
        }
        else {
            $normalizedModuleName = $moduleName.Replace('/', [System.IO.Path]::DirectorySeparatorChar).TrimStart('.', [System.IO.Path]::DirectorySeparatorChar)
            $modulePath = [System.IO.Path]::GetFullPath((Join-Path $resolvedBundlePath $normalizedModuleName))
            if (-not $modulePath.StartsWith($resolvedBundlePath, [System.StringComparison]::OrdinalIgnoreCase)) {
                Add-Issue "ComponentEntry.ModuleName points outside the bundle: $moduleName"
            }
            elseif (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
                Add-Issue "ComponentEntry.ModuleName target not found: $modulePath"
            }
        }
    }
}

if (-not (Test-Path -LiteralPath $contentsPath -PathType Container)) {
    Add-Issue "Bundle contents folder not found: $contentsPath"
}
else {
    foreach ($fileName in $RequiredContentFiles) {
        $filePath = Join-Path $contentsPath $fileName
        if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
            Add-Issue "Required bundle file not found: $filePath"
        }
    }

    $forbiddenFiles = Get-ChildItem -LiteralPath $contentsPath -File -Recurse |
        Where-Object {
            $_.Name -in @("acmgd.dll", "acdbmgd.dll", "accoremgd.dll", "AecBaseMgd.dll") -or
            $_.Name -like "Aecc*.dll"
        }

    foreach ($file in $forbiddenFiles) {
        Add-Issue ("Autodesk managed assembly should not be packaged: " + (Get-RelativePath $resolvedBundlePath $file.FullName))
    }
}

$result = [ordered]@{
    success = ($Issues.Count -eq 0)
    bundlePath = $resolvedBundlePath
    manifestPath = $manifestPath
    contentsPath = $contentsPath
    checkedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    issues = @($Issues.ToArray())
    warnings = @($Warnings.ToArray())
    requiredFiles = @($RequiredContentFiles)
}

if ($AsJson) {
    $result | ConvertTo-Json -Depth 5
}
else {
    if ($result.success) {
        Write-Host "Bundle diagnostic passed: $resolvedBundlePath"
    }
    else {
        Write-Host "Bundle diagnostic failed: $resolvedBundlePath"
        foreach ($issue in $Issues) {
            Write-Host " - $issue"
        }
    }
}

if ($result.success) {
    exit 0
}

exit 2
