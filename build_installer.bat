@echo off
echo ===================================================
echo Building FocusFade Standalone Installer
echo ===================================================
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

:: 1. Compile Main Application
taskkill /f /im FocusFade.exe >nul 2>&1
taskkill /f /im FocusFade_Setup.exe >nul 2>&1

echo [1/2] Compiling FocusFade.exe...
"%CSC%" /nologo /target:winexe /optimize /win32icon:app.ico /out:FocusFade.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /recurse:Core\*.cs /recurse:Mixing\*.cs /recurse:Models\*.cs /recurse:UI\*.cs Program.cs

if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Failed to compile FocusFade.exe!
    pause
    exit /b 1
)

:: 2. Compile Standalone Setup Executable with Embedded Assets
echo [2/2] Compiling FocusFade_Setup.exe with embedded payload...
"%CSC%" /nologo /target:winexe /optimize /win32icon:app.ico /out:FocusFade_Setup.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:Microsoft.CSharp.dll /resource:FocusFade.exe,FocusFade.exe /resource:app.ico,app.ico /resource:GUIDE.txt,GUIDE.txt Installer\*.cs

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ===================================================
    echo [SUCCESS] Standalone Installer Created:
    echo --^> FocusFade_Setup.exe
    echo ===================================================
) else (
    echo [ERROR] Failed to compile FocusFade_Setup.exe!
)
pause
