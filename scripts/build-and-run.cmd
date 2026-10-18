@echo off
setlocal
cd /d "%~dp0.."
where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET 10 SDK is needed once to build ArzType.
  where winget >nul 2>nul
  if not errorlevel 1 (
    echo Installing it with winget...
    winget install --id Microsoft.DotNet.SDK.10 -e --accept-source-agreements --accept-package-agreements
    echo.
    echo Done. Close this window and double-click build-and-run.cmd again.
    pause
    exit /b 0
  )
  echo Please install it from https://dotnet.microsoft.com/download/dotnet/10.0 and run this again.
  pause
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\build-windows.ps1"
if errorlevel 1 (
  echo Build failed. Check the error above.
  pause
  exit /b 1
)
start "" ".\publish\ArzType.exe"
