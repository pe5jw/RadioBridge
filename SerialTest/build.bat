@echo off
echo ============================================
echo  SerialTest - Build script
echo ============================================
echo.

dotnet --version >nul 2>&1
if errorlevel 1 (
    echo FOUT: .NET 10 SDK niet gevonden.
    pause
    exit /b 1
)

cd /d "%~dp0"

echo Packages downloaden...
dotnet restore
if errorlevel 1 ( echo FOUT bij restore. && pause && exit /b 1 )

echo.
echo Bouwen...
dotnet build -c Release
if errorlevel 1 ( echo FOUT bij bouwen. && pause && exit /b 1 )

echo.
echo ============================================
echo  Klaar! Applicatie staat in:
echo  bin\Release\net10.0-windows\SerialTest.exe
echo ============================================
echo.
pause
