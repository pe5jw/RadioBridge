@echo off
REM Build radiobridge_audio.dll for Windows x64
REM Run this from a "x64 Native Tools Command Prompt for VS" or Developer Command Prompt
REM Output: ..\native\win-x64\radiobridge_audio.dll

setlocal

set SRCDIR=%~dp0
set OUTDIR=%SRCDIR%..\RadioBridge\native\win-x64

REM Ensure output dir exists
if not exist "%OUTDIR%" mkdir "%OUTDIR%"

REM Try MSVC (cl.exe)
where cl >nul 2>&1
if %ERRORLEVEL%==0 (
    echo Bouwen met MSVC...
    cl.exe /O2 /LD /I"%SRCDIR%" ^
        "%SRCDIR%radiobridge_audio.c" ^
        /Fe:"%OUTDIR%\radiobridge_audio.dll" ^
        /link /OUT:"%OUTDIR%\radiobridge_audio.dll"
    if %ERRORLEVEL%==0 (
        echo Klaar: %OUTDIR%\radiobridge_audio.dll
        goto :done
    )
)

REM Try MinGW/MSYS2 gcc
where gcc >nul 2>&1
if %ERRORLEVEL%==0 (
    echo Bouwen met MinGW gcc...
    gcc -O2 -shared -I"%SRCDIR%" ^
        "%SRCDIR%radiobridge_audio.c" ^
        -o "%OUTDIR%\radiobridge_audio.dll" ^
        -lole32 -lwinmm
    if %ERRORLEVEL%==0 (
        echo Klaar: %OUTDIR%\radiobridge_audio.dll
        goto :done
    )
)

REM Try LLVM clang
where clang >nul 2>&1
if %ERRORLEVEL%==0 (
    echo Bouwen met clang...
    clang -O2 -shared -target x86_64-pc-windows-gnu -I"%SRCDIR%" ^
        "%SRCDIR%radiobridge_audio.c" ^
        -o "%OUTDIR%\radiobridge_audio.dll" ^
        -lole32 -lwinmm
    if %ERRORLEVEL%==0 (
        echo Klaar: %OUTDIR%\radiobridge_audio.dll
        goto :done
    )
)

echo FOUT: Geen C compiler gevonden (cl, gcc, of clang).
echo Installeer Visual Studio Build Tools of MinGW/MSYS2.
exit /b 1

:done
endlocal
