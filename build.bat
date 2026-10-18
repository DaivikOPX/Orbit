@echo off
setlocal
cd /d "%~dp0"
echo ============================================
echo   Building Orbit for Windows
echo ============================================

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo Error: .NET Framework csc.exe not found at %CSC%
    pause
    exit /b 1
)

set REFS=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationFramework.dll,C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll,C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll,C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Xaml.dll,System.Windows.Forms.dll,System.Drawing.dll,System.Core.dll,System.dll,System.Xml.dll,System.Xml.Linq.dll,System.IO.Compression.dll,System.IO.Compression.FileSystem.dll

echo Compiling OrbitApp.cs ...
"%CSC%" /target:winexe /optimize+ /win32icon:Orbit.ico /out:Orbit.exe /r:%REFS% OrbitApp.cs

if %ERRORLEVEL% EQU 0 (
    echo [SUCCESS] Orbit.exe built successfully!
) else (
    echo [ERROR] Compilation failed.
)
endlocal
