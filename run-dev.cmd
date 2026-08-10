@echo off
setlocal
cd /d "%~dp0"

echo GrevUltraVNC - Development Launcher
echo ====================================
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
    echo .NET SDK was not found.
    echo.
    echo Install the .NET 10 SDK with:
    echo     winget install Microsoft.DotNet.SDK.10 --source winget
    echo.
    pause
    exit /b 1
)

echo Starting GrevUltraVNC...
echo.
dotnet run --project "src\GrevUltraVNC\GrevUltraVNC.csproj"

if errorlevel 1 (
    echo.
    echo GrevUltraVNC exited with a build or runtime error.
    echo Copy the error shown above back into ChatGPT and we can fix it.
    pause
)
