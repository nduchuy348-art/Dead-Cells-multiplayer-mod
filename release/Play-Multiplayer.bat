@echo off
title Dead Cells Multiplayer Launcher
setlocal

cd /d "%~dp0"

:: 1. Check for bundled portable dotnet runtime
if exist "%~dp0dotnet\dotnet.exe" (
    "%~dp0dotnet\dotnet.exe" --list-runtimes 2>nul | findstr /i "Microsoft.NETCore.App 10." >nul
    if %errorLevel% equ 0 (
        set "DOTNET_ROOT=%~dp0dotnet"
        set "PATH=%~dp0dotnet;%PATH%"
        goto :LAUNCH
    )
)

:: 2. Check standard Program Files installation (official Microsoft installer)
if exist "%ProgramFiles%\dotnet\dotnet.exe" (
    "%ProgramFiles%\dotnet\dotnet.exe" --list-runtimes 2>nul | findstr /i "Microsoft.NETCore.App 10." >nul
    if %errorLevel% equ 0 (
        set "DOTNET_ROOT=%ProgramFiles%\dotnet"
        set "PATH=%ProgramFiles%\dotnet;%PATH%"
        goto :LAUNCH
    )
)

:: 3. Check dotnet on system PATH
where dotnet >nul 2>&1
if %errorLevel% equ 0 (
    dotnet --list-runtimes 2>nul | findstr /i "Microsoft.NETCore.App 10." >nul
    if %errorLevel% equ 0 (
        goto :LAUNCH
    )
)

:: 4. Check user profile .dotnet directory
if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
    "%USERPROFILE%\.dotnet\dotnet.exe" --list-runtimes 2>nul | findstr /i "Microsoft.NETCore.App 10." >nul
    if %errorLevel% equ 0 (
        set "DOTNET_ROOT=%USERPROFILE%\.dotnet"
        set "PATH=%USERPROFILE%\.dotnet;%PATH%"
        goto :LAUNCH
    )
)

echo [NOTICE] .NET 10 Runtime (x64) was not detected on your system!
echo Dead Cells Multiplayer Mod requires .NET 10.0 Desktop Runtime (x64) to run.
echo Opening the official Microsoft download page in your browser...
start https://dotnet.microsoft.com/download/dotnet/10.0
pause
exit /b

:LAUNCH
echo Starting Dead Cells Multiplayer Co-op...
if exist "coremod\core\host\startup\DeadCellsModding.exe" (
    start "" "coremod\core\host\startup\DeadCellsModding.exe" %*
) else (
    echo [ERROR] Could not find 'coremod\core\host\startup\DeadCellsModding.exe'!
    echo Please make sure you have extracted all mod files into your Dead Cells game directory.
    pause
)
