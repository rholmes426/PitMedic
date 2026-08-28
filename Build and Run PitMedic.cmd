@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title PitMedic Development Build

set "PROJECT=%~dp0Source\PitMedic\PitMedic.csproj"
set "HELPER_PROJECT=%~dp0Source\PitMedic.RepairHelper\PitMedic.RepairHelper.csproj"
set "OUTPUT=%~dp0Output"

cls
echo.
echo   PITMEDIC DEVELOPMENT BUILD
echo   ==========================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] The .NET SDK was not found.
    echo.
    echo PitMedic requires the .NET 10 SDK to build.
    echo Install the SDK ^(not only the runtime^) with:
    echo   winget install Microsoft.DotNet.SDK.10
    echo.
    echo Or download Windows x64 SDK from:
    echo   https://dotnet.microsoft.com/download/dotnet/10.0
    echo.
    echo Close and reopen this window after installation, then run this file again.
    echo.
    pause
    exit /b 1
)

for /f "delims=" %%S in ('dotnet --list-sdks ^| findstr /R /B "10\."') do set "DOTNET10=%%S"
if not defined DOTNET10 (
    echo [ERROR] .NET 10 SDK was not found.
    echo.
    dotnet --list-sdks
    echo.
    echo Install the SDK ^(not only the runtime^) with:
    echo   winget install Microsoft.DotNet.SDK.10
    echo.
    echo Or download Windows x64 SDK from:
    echo   https://dotnet.microsoft.com/download/dotnet/10.0
    echo.
    echo Close and reopen this window after installation, then run this file again.
    echo.
    pause
    exit /b 1
)

tasklist /FI "IMAGENAME eq PitMedic.exe" 2>nul | find /I "PitMedic.exe" >nul
if not errorlevel 1 (
    echo [ACTION NEEDED] PitMedic is currently running.
    echo.
    echo Exit PitMedic from the tray, then run this file again.
    echo PitMedic will not be force-closed by the development builder.
    echo.
    pause
    exit /b 2
)

if exist "%OUTPUT%" (
    echo Cleaning previous build...
    rmdir /S /Q "%OUTPUT%" 2>nul
    if exist "%OUTPUT%" (
        echo [ERROR] The previous Output folder could not be removed.
        echo Close any Explorer windows or applications using it and retry.
        echo.
        pause
        exit /b 3
    )
)
mkdir "%OUTPUT%" >nul 2>nul

echo Using .NET 10 SDK:
echo   %DOTNET10%
echo.
echo Building PitMedic v0.4.4.0...
echo This can take a minute the first time because .NET may restore packages.
echo.

set DOTNET_CLI_TELEMETRY_OPTOUT=1
set DOTNET_NOLOGO=1

dotnet publish "%PROJECT%" -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=false -o "%OUTPUT%"
if errorlevel 1 goto :build_failed

dotnet publish "%HELPER_PROJECT%" -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=false -o "%OUTPUT%"
if errorlevel 1 goto :build_failed

if not exist "%OUTPUT%\PitMedic.exe" goto :build_failed
if not exist "%OUTPUT%\PitMedic.RepairHelper.exe" goto :build_failed

echo.
echo ============================================================
echo BUILD SUCCEEDED
echo ============================================================
echo.
echo Output:
echo   %OUTPUT%\PitMedic.exe
echo   %OUTPUT%\PitMedic.RepairHelper.exe
echo.
echo PitMedic is starting now without administrator rights.
echo Windows will ask for approval only when an allowlisted system repair needs it.
echo.
start "" "%OUTPUT%\PitMedic.exe"
exit /b 0

:build_failed
echo.
echo ============================================================
echo BUILD FAILED
echo ============================================================
echo.
echo Leave this window open and send a screenshot or copy the compiler errors
 echo into ChatGPT so the build can be corrected.
echo.
pause
exit /b 10
