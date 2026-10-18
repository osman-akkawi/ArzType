@echo off
setlocal
rem Builds publish\ArzType-Setup-<version>.exe : the file you give to users.
cd /d "%~dp0.."
set VERSION=0.2.1

where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET 10 SDK is needed once. Installing with winget...
  winget install --id Microsoft.DotNet.SDK.10 -e --accept-source-agreements --accept-package-agreements
  echo Close this window and run make-installer.cmd again.
  pause
  exit /b 0
)

rem 1) Build the single-file ArzType.exe (runs the tests first)
powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\build-windows.ps1"
if errorlevel 1 ( echo Build failed. & pause & exit /b 1 )

rem 2) Find Inno Setup (free installer builder); install it if missing
set ISCC=
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not defined ISCC (
  echo Inno Setup not found. Installing it with winget...
  winget install --id JRSoftware.InnoSetup -e --accept-source-agreements --accept-package-agreements
  if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
  if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
)
if not defined ISCC (
  echo Please install Inno Setup 6 from https://jrsoftware.org/isdl.php and run this again.
  pause
  exit /b 1
)

rem 3) Build the installer
"%ISCC%" /Qp /DAppVersion=%VERSION% "installer\ArzType.iss"
if errorlevel 1 ( echo Installer build failed. & pause & exit /b 1 )

echo.
echo Done: publish\ArzType-Setup-%VERSION%.exe
explorer /select,"publish\ArzType-Setup-%VERSION%.exe"
pause
