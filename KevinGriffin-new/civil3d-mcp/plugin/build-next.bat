@echo off
REM Build the next hot-reload version of the Civil 3D MCP bridge.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-next.ps1"
echo.
pause
