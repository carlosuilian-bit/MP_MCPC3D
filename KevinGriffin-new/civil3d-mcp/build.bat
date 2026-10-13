@echo off
REM =============================================================================
REM Civil 3D MCP Bridge — Build Script
REM =============================================================================
REM This script:
REM   1. Compiles the .NET plugin DLL
REM   2. Builds the Inno Setup installer
REM
REM PREREQUISITES:
REM   - Visual Studio 2022 Build Tools (or full VS) with .NET Framework 4.8
REM   - Inno Setup 6 installed (https://jrsoftware.org/isinfo.php)
REM   - Civil 3D installed (for reference DLLs)
REM
REM USAGE:
REM   build.bat              — build plugin + installer
REM   build.bat plugin       — build plugin only
REM   build.bat installer    — build installer only (assumes DLL exists)
REM =============================================================================

setlocal enabledelayedexpansion

REM --- Configuration ---
set CIVIL3D_PATH=C:\Program Files\Autodesk\AutoCAD 2026
set "INNO_COMPILER=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
set MSBUILD=msbuild

REM Check for dotnet CLI as alternative
where dotnet >nul 2>&1
if %ERRORLEVEL% equ 0 (
    set BUILD_TOOL=dotnet
) else (
    set BUILD_TOOL=msbuild
)

REM --- Parse arguments ---
set BUILD_TARGET=%1
if "%BUILD_TARGET%"=="" set BUILD_TARGET=all

echo ============================================
echo  Civil 3D MCP Bridge — Build
echo ============================================
echo.

REM =============================================================================
REM STEP 1: Check prerequisites
REM =============================================================================

echo [1/3] Checking prerequisites...

if not exist "%CIVIL3D_PATH%\AcMgd.dll" (
    echo WARNING: Civil 3D not found at %CIVIL3D_PATH%
    echo          Edit CIVIL3D_PATH in this script to match your installation.
    echo          Looking for alternative paths...

    REM Try common paths
    for %%V in (2026 2025 2024) do (
        if exist "C:\Program Files\Autodesk\AutoCAD %%V\AcMgd.dll" (
            set CIVIL3D_PATH=C:\Program Files\Autodesk\AutoCAD %%V
            echo Found Civil 3D at: !CIVIL3D_PATH!
            goto :found_civil3d
        )
    )
    echo ERROR: Cannot find Civil 3D installation. Install Civil 3D or edit CIVIL3D_PATH.
    exit /b 1
)
:found_civil3d
echo   Civil 3D: %CIVIL3D_PATH%

REM =============================================================================
REM STEP 2: Verify .csproj exists
REM =============================================================================

if not exist "plugin\Civil3dMcpBridge.csproj" (
    echo ERROR: plugin\Civil3dMcpBridge.csproj not found.
    echo        This file should have been included with the project.
    echo        Check that you're running from the civil3d-mcp directory.
    exit /b 1
)
echo   .csproj: plugin\Civil3dMcpBridge.csproj

REM =============================================================================
REM STEP 3: Build the plugin DLL
REM =============================================================================

if "%BUILD_TARGET%"=="all" goto :build_plugin
if "%BUILD_TARGET%"=="plugin" goto :build_plugin
goto :skip_plugin

:build_plugin
echo.
echo [2/3] Building .NET plugin...

REM Use full absolute path to avoid issues with folder names containing hyphens
set CSPROJ_PATH=%CD%\plugin\Civil3dMcpBridge.csproj
set OUTPUT_PATH=%CD%\plugin\bin\Release

REM Clean previous build artifacts to avoid stale/ref assembly issues
if exist "plugin\bin" rmdir /s /q "plugin\bin"
if exist "plugin\obj" rmdir /s /q "plugin\obj"

if "%BUILD_TOOL%"=="dotnet" (
    dotnet publish "%CSPROJ_PATH%" -c Release -o "%OUTPUT_PATH%" --no-self-contained
) else (
    %MSBUILD% "%CSPROJ_PATH%" /p:Configuration=Release /p:OutputPath=bin\Release
)

if %ERRORLEVEL% neq 0 (
    echo ERROR: Plugin build failed!
    exit /b 1
)

if exist "plugin\bin\Release\Civil3dMcpBridge.dll" (
    echo   SUCCESS: plugin\bin\Release\Civil3dMcpBridge.dll
) else (
    echo ERROR: DLL not found after build
    exit /b 1
)

:skip_plugin

REM =============================================================================
REM STEP 4: Build the Inno Setup installer
REM =============================================================================

if "%BUILD_TARGET%"=="all" goto :build_installer
if "%BUILD_TARGET%"=="installer" goto :build_installer
goto :skip_installer

:build_installer
echo.
echo [3/3] Building Inno Setup installer...

if not exist "!INNO_COMPILER!" (
    echo WARNING: Inno Setup not found at !INNO_COMPILER!
    echo          Install from https://jrsoftware.org/isinfo.php
    echo          Or edit INNO_COMPILER path in this script.
    echo          Skipping installer build.
    goto :skip_installer
)

if not exist "plugin\bin\Release\Civil3dMcpBridge.dll" (
    echo ERROR: Plugin DLL not found. Run 'build.bat plugin' first.
    exit /b 1
)

"!INNO_COMPILER!" installer\Civil3dMcpInstaller.iss

if %ERRORLEVEL% neq 0 (
    echo ERROR: Installer build failed!
    exit /b 1
)

echo   SUCCESS: installer\Output\Civil3dMcpBridge-Setup-1.0.0.exe

:skip_installer

REM =============================================================================
REM Done
REM =============================================================================

echo.
echo ============================================
echo  Build complete!
echo ============================================

if "%BUILD_TARGET%"=="all" (
    echo.
    echo To test without the installer:
    echo   1. Open Civil 3D
    echo   2. NETLOAD -^> plugin\bin\Release\Civil3dMcpBridge.dll
    echo   3. MCPSTART
    echo   4. In a terminal: python mcp-server\server.py
    echo   5. curl http://localhost:8270 -d "{\"action\":\"ping\",\"params\":{}}"
)

endlocal
