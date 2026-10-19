# Automated FFmpeg Downloader for Saturn
$ErrorActionPreference = "Stop"
$rootDir = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $rootDir "ffmpeg.exe"

if (Test-Path $dest) {
    Write-Host "[OK] ffmpeg.exe is already present at: $dest" -ForegroundColor Green
    exit 0
}

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host " Saturn FFmpeg Downloader" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

# Check if winget is available
if (Get-Command winget -ErrorAction SilentlyContinue) {
    Write-Host "Attempting installation via WinGet..." -ForegroundColor Yellow
    try {
        winget install Gyan.FFmpeg --accept-source-agreements --accept-package-agreements
        Write-Host "[SUCCESS] FFmpeg installed via WinGet!" -ForegroundColor Green
        exit 0
    } catch {
        Write-Warning "WinGet installation failed. Falling back to direct download..."
    }
}

# Direct download fallback (Gyan.dev essentials zip)
$zipUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
$tempZip = Join-Path $env:TEMP "ffmpeg-essentials.zip"
$tempExtract = Join-Path $env:TEMP "ffmpeg-extract"

Write-Host "Downloading release from $zipUrl..." -ForegroundColor Yellow
Invoke-WebRequest -Uri $zipUrl -OutFile $tempZip -UseBasicParsing

Write-Host "Extracting ffmpeg.exe..." -ForegroundColor Yellow
if (Test-Path $tempExtract) { Remove-Item $tempExtract -Recurse -Force }
Expand-Archive -Path $tempZip -DestinationPath $tempExtract -Force

$foundFfmpeg = Get-ChildItem -Path $tempExtract -Filter "ffmpeg.exe" -Recurse | Select-Object -First 1
if ($foundFfmpeg) {
    Copy-Item $foundFfmpeg.FullName -Destination $dest -Force
    Write-Host "[SUCCESS] ffmpeg.exe installed directly to $dest!" -ForegroundColor Green
} else {
    Write-Error "Could not locate ffmpeg.exe in extracted archive."
}

# Cleanup
Remove-Item $tempZip -Force -ErrorAction SilentlyContinue
Remove-Item $tempExtract -Recurse -Force -ErrorAction SilentlyContinue
