# Orbit Build Script (PowerShell)
$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "   Building Orbit for Windows (PowerShell)  " -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    Write-Error ".NET Framework 4.0/4.8 csc.exe compiler not found at $csc"
    exit 1
}

$ico = Join-Path $ScriptDir "Orbit.ico"
$src = Join-Path $ScriptDir "OrbitApp.cs"
$out = Join-Path $ScriptDir "Orbit.exe"

$refs = @(
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationFramework.dll",
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll",
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll",
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Xaml.dll",
    "System.Windows.Forms.dll",
    "System.Drawing.dll",
    "System.Core.dll",
    "System.dll",
    "System.Xml.dll",
    "System.Xml.Linq.dll",
    "System.IO.Compression.dll",
    "System.IO.Compression.FileSystem.dll"
)

Write-Host "Compiling $src -> $out ..." -ForegroundColor Yellow
$proc = Start-Process -FilePath $csc -ArgumentList "/target:winexe /optimize+ /win32icon:`"$ico`" /out:`"$out`" `"/r:$($refs -join ',')`" `"$src`"" -NoNewWindow -Wait -PassThru

if ($proc.ExitCode -eq 0) {
    Write-Host "[SUCCESS] Orbit.exe compiled successfully!" -ForegroundColor Green
} else {
    Write-Host "[ERROR] Compilation failed with exit code $($proc.ExitCode)" -ForegroundColor Red
    exit $proc.ExitCode
}
