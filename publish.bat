
@echo off
setlocal

cd /d "%~dp0"

echo Publishing Monitor Brightness...
dotnet publish .\MonitorBrightness.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

if errorlevel 1 (
    echo.
    echo Publish failed.
    pause
    exit /b 1
)

set "APP=%~dp0bin\Release\net10.0-windows\win-x64\publish\MonitorBrightness.exe"
set "SHORTCUT=%USERPROFILE%\Desktop\Monitor Brightness.lnk"

if not exist "%APP%" (
    echo.
    echo Could not find the published EXE:
    echo %APP%
    pause
    exit /b 1
)

echo Creating desktop shortcut...

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
 "$ws = New-Object -ComObject WScript.Shell; " ^
 "$shortcut = $ws.CreateShortcut('%SHORTCUT%'); " ^
 "$shortcut.TargetPath = '%APP%'; " ^
 "$shortcut.WorkingDirectory = Split-Path '%APP%'; " ^
 "$shortcut.Description = 'Adjust external monitor brightness'; " ^
 "$shortcut.Save()"

if errorlevel 1 (
    echo Failed to create shortcut.
    pause
    exit /b 1
)

echo.
echo Publish complete!
echo Desktop shortcut created: Monitor Brightness
pause
