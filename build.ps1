# Orbit Build Script (PowerShell)
$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $ScriptDir) { $ScriptDir = (Get-Location).Path }
Set-Location $ScriptDir

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "   Building Orbit for Windows (PowerShell)  " -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan

# Locate 64-bit or 32-bit .NET Framework 4.x csc.exe compiler
$netDir = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$csc = Join-Path $netDir "csc.exe"

if (-not (Test-Path $csc)) {
    $candidate = Get-ChildItem -Path "C:\Windows\Microsoft.NET" -Filter "csc.exe" -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match "v4\.0" } | Select-Object -First 1
    if ($candidate) {
        $csc = $candidate.FullName
        $netDir = $candidate.DirectoryName
    } else {
        Write-Error ".NET Framework 4.0/4.8 csc.exe compiler not found."
        exit 1
    }
}

$wpfDir = Join-Path $netDir "WPF"
$ico = Join-Path $ScriptDir "Orbit.ico"
$src = Join-Path $ScriptDir "OrbitApp.cs"
$out = Join-Path $ScriptDir "Orbit.exe"

$refs = @(
    (Join-Path $wpfDir "PresentationFramework.dll"),
    (Join-Path $wpfDir "PresentationCore.dll"),
    (Join-Path $wpfDir "WindowsBase.dll"),
    (Join-Path $netDir "System.Xaml.dll"),
    "System.Windows.Forms.dll",
    "System.Drawing.dll",
    "System.Core.dll",
    "System.dll",
    "System.Xml.dll",
    "System.Xml.Linq.dll",
    "System.IO.Compression.dll",
    "System.IO.Compression.FileSystem.dll"
)

Write-Host "Compiler: $csc" -ForegroundColor Gray
Write-Host ".NET Dir: $netDir" -ForegroundColor Gray
Write-Host "WPF Dir:  $wpfDir" -ForegroundColor Gray
Write-Host "Compiling $src -> $out ..." -ForegroundColor Yellow

$refArgs = "/r:" + ($refs -join ",")
$proc = Start-Process -FilePath $csc -ArgumentList "/target:winexe /optimize+ /win32icon:`"$ico`" /out:`"$out`" `"$refArgs`" `"$src`"" -NoNewWindow -Wait -PassThru

if ($proc.ExitCode -eq 0) {
    Write-Host "[SUCCESS] Orbit.exe compiled successfully!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "[ERROR] Compilation failed with exit code $($proc.ExitCode)" -ForegroundColor Red
    exit $proc.ExitCode
}
