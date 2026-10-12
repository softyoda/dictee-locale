@echo off
cd /d "%~dp0"
if exist "app\DicteeLocale.exe" (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1" -SkipBuild
) else (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1"
)
if errorlevel 1 (
    echo.
    echo Installation failed. Read the error above, then retry.
    pause
    exit /b 1
)
echo.
echo Installation complete. Double-click Start.vbs to launch.
pause
