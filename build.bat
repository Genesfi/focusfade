@echo off
echo ===================================================
echo Building FocusFade (with Embedded App Icon)
echo ===================================================
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

taskkill /f /im FocusFade.exe >nul 2>&1

"%CSC%" /nologo /target:winexe /optimize /win32icon:app.ico /out:FocusFade.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /recurse:Core\*.cs /recurse:Mixing\*.cs /recurse:Models\*.cs /recurse:UI\*.cs Program.cs

if %ERRORLEVEL% EQU 0 (
    echo [SUCCESS] FocusFade.exe compiled successfully with embedded icon!
) else (
    echo [ERROR] Build failed! Check compiler output.
)
pause
