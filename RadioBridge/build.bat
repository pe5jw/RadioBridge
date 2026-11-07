@echo off
echo ============================================
echo  RadioBridge - Build script
echo ============================================
echo.

:: Controleer of .NET 8 aanwezig is
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo FOUT: .NET 8 SDK niet gevonden.
    echo Download via: https://dotnet.microsoft.com/download/dotnet/10.0
    pause
    exit /b 1
)

echo .NET SDK gevonden:
dotnet --version
echo.

:: Ga naar de projectmap
cd /d "%~dp0"

:: Herstel NuGet packages (NAudio)
echo Packages downloaden (NAudio)...
dotnet restore
if errorlevel 1 (
    echo FOUT bij restore. Controleer internetverbinding.
    pause
    exit /b 1
)

:: Bouw de applicatie
echo.
echo Bouwen...
dotnet build -c Release
if errorlevel 1 (
    echo FOUT bij bouwen.
    pause
    exit /b 1
)

echo.
echo ============================================
echo  Klaar! Applicatie staat in:
echo  bin\Release\net10.0-windows\RadioBridge.exe
echo ============================================
echo.
pause
