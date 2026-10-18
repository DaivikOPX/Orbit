# Orbit Comprehensive Conversion & Tool Verification Suite
param(
    [string]$OrbitExe = "C:\Users\daivi\OneDrive\Desktop\Orbit.exe",
    [string]$TestDir = "C:\Users\daivi\.gemini\antigravity\scratch\orbit-windows\test_suite"
)

$ErrorActionPreference = "Stop"
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   ORBIT FOR WINDOWS: COMPLETE CONVERSION & TOOL AUDIT    " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

if (Test-Path $TestDir) {
    Remove-Item $TestDir -Recurse -Force
}
New-Item -ItemType Directory -Path $TestDir | Out-Null

$results = [System.Collections.Generic.List[PSObject]]::new()

function Record-Result {
    param([string]$Category, [string]$TestName, [string]$OutputFile, [bool]$Passed, [string]$Details)
    $sz = 0
    if ($OutputFile -and (Test-Path $OutputFile)) {
        $sz = (Get-Item $OutputFile).Length
    }
    $statusStr = if ($Passed) { "PASS" } else { "FAIL" }
    $color = if ($Passed) { "Green" } else { "Red" }
    Write-Host "[$statusStr] $Category -> $TestName : $sz bytes ($Details)" -ForegroundColor $color

    $results.Add([PSCustomObject]@{
        Category   = $Category
        TestName   = $TestName
        OutputFile = $OutputFile
        Size       = $sz
        Passed     = $Passed
        Details    = $Details
    })
}

# --- STEP 1: GENERATE AUTHENTIC TEST ASSETS ---
Write-Host "`n[1/7] Generating Source Assets across all categories..." -ForegroundColor Yellow

# A. Image (PNG)
Add-Type -AssemblyName System.Drawing
$testBmp = New-Object System.Drawing.Bitmap 300, 300
$g = [System.Drawing.Graphics]::FromImage($testBmp)
$g.Clear([System.Drawing.Color]::FromArgb(15, 23, 42))
$pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(99, 102, 241), 6)
$g.DrawEllipse($pen, 50, 50, 200, 200)
$brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(168, 85, 247))
$g.FillEllipse($brush, 120, 120, 60, 60)
$g.Dispose()
$srcPng = Join-Path $TestDir "source_image.png"
$testBmp.Save($srcPng, [System.Drawing.Imaging.ImageFormat]::Png)
$testBmp.Dispose()

# B. Video (MP4 via ffmpeg)
$ffmpegExe = "C:\Users\daivi\OneDrive\Desktop\ffmpeg.exe"
$srcMp4 = Join-Path $TestDir "source_video.mp4"
& $ffmpegExe -hide_banner -loglevel error -y -f lavfi -i "testsrc=duration=2:size=320x240:rate=15" -f lavfi -i "sine=frequency=440:duration=2" -c:v libx264 -c:a aac $srcMp4

# C. Audio (WAV via ffmpeg)
$srcWav = Join-Path $TestDir "source_audio.wav"
& $ffmpegExe -hide_banner -loglevel error -y -f lavfi -i "sine=frequency=523:duration=2" -c:a pcm_s16le $srcWav

# D. Data (CSV)
$srcCsv = Join-Path $TestDir "source_data.csv"
@"
id,name,role,department,score
101,Alex Vance,Lead Architect,Core Systems,98.5
102,Elena Rostova,Senior Designer,Product UI,94.2
103,Marcus Brody,DevOps Engineer,Infrastructure,91.8
104,Chloe Decker,Security Analyst,SecOps,96.0
"@ | Set-Content -Path $srcCsv -Encoding UTF8

# E. Data (JSON)
$srcJson = Join-Path $TestDir "source_data.json"
@"
[
  { "id": "201", "product": "Quantum Laptop", "price": "1499", "status": "InStock" },
  { "id": "202", "product": "Wireless Earbuds", "price": "199", "status": "InStock" },
  { "id": "203", "product": "OLED Monitor", "price": "799", "status": "PreOrder" }
]
"@ | Set-Content -Path $srcJson -Encoding UTF8

# F. Data (XML)
$srcXml = Join-Path $TestDir "source_data.xml"
@"
<?xml version="1.0" encoding="UTF-8"?>
<inventory>
  <item><id>301</id><name>HoloLens</name><stock>45</stock></item>
  <item><id>302</id><name>Sensors</name><stock>120</stock></item>
</inventory>
"@ | Set-Content -Path $srcXml -Encoding UTF8

# G. Document (Markdown & TXT)
$srcMd = Join-Path $TestDir "source_doc.md"
@"
# Orbit Spatial Architecture
## Native Windows Utility
Orbit brings fluid spatial interactions and 300+ format conversions directly to Windows 11.
Features include:
- Zero-cloud execution
- Instant local conversions
- Low latency synthesized audio chimes
"@ | Set-Content -Path $srcMd -Encoding UTF8

$srcTxt = Join-Path $TestDir "source_doc.txt"
@"
Orbit System Report
Version: 1.0.0
Engine: Native C# WPF with FFmpeg Media Core
Platform: Windows 11 x64
All conversions run locally without telemetry or cloud dependencies.
"@ | Set-Content -Path $srcTxt -Encoding UTF8

Write-Host "All source files created successfully." -ForegroundColor Green

# --- STEP 2: TEST ALL IMAGE CONVERSIONS ---
Write-Host "`n[2/7] Verifying Image Conversions..." -ForegroundColor Yellow
$imageTargets = @("png", "jpg", "webp", "ico", "bmp", "tiff", "gif", "pdf")

foreach ($t in $imageTargets) {
    & $OrbitExe convert $srcPng --to $t | Out-Null
    $out = Join-Path $TestDir ("source_image_orbit." + $t)
    $ok = (Test-Path $out) -and ((Get-Item $out).Length -gt 0)
    
    # Additional verification
    $detail = "OK"
    if ($ok -and $t -eq "pdf") {
        $firstBytes = Get-Content $out -TotalCount 1
        if (!$firstBytes.StartsWith("%PDF")) { $ok = $false; $detail = "Invalid PDF Header"; }
    } elseif ($ok -and $t -eq "ico") {
        $b = [System.IO.File]::ReadAllBytes($out)
        if ($b[2] -ne 1) { $ok = $false; $detail = "Invalid ICO Header"; }
    }
    Record-Result "Image" "PNG -> $t" $out $ok $detail
}

# --- STEP 3: TEST ALL VIDEO CONVERSIONS ---
Write-Host "`n[3/7] Verifying Video Conversions..." -ForegroundColor Yellow
$videoTargets = @("mp4", "gif", "mp3", "webm", "mkv", "mov", "avi", "wav")

foreach ($t in $videoTargets) {
    & $OrbitExe convert $srcMp4 --to $t | Out-Null
    $out = Join-Path $TestDir ("source_video_orbit." + $t)
    $ok = (Test-Path $out) -and ((Get-Item $out).Length -gt 0)
    Record-Result "Video" "MP4 -> $t" $out $ok "OK"
}

# --- STEP 4: TEST ALL AUDIO CONVERSIONS ---
Write-Host "`n[4/7] Verifying Audio Conversions..." -ForegroundColor Yellow
$audioTargets = @("mp3", "wav", "flac", "m4a", "opus", "ogg")

foreach ($t in $audioTargets) {
    & $OrbitExe convert $srcWav --to $t | Out-Null
    $out = Join-Path $TestDir ("source_audio_orbit." + $t)
    $ok = (Test-Path $out) -and ((Get-Item $out).Length -gt 0)
    Record-Result "Audio" "WAV -> $t" $out $ok "OK"
}

# --- STEP 5: TEST ALL DATA CONVERSIONS ---
Write-Host "`n[5/7] Verifying Data Conversions..." -ForegroundColor Yellow
# CSV conversions
$csvTargets = @("json", "html", "md", "txt")
foreach ($t in $csvTargets) {
    & $OrbitExe convert $srcCsv --to $t | Out-Null
    $out = Join-Path $TestDir ("source_data_orbit." + $t)
    $ok = (Test-Path $out) -and ((Get-Item $out).Length -gt 0)
    $detail = "OK"
    if ($ok -and $t -eq "json") {
        try { Get-Content $out -Raw | ConvertFrom-Json | Out-Null; } catch { $ok = $false; $detail = "Invalid JSON"; }
    } elseif ($ok -and $t -eq "html") {
        if (!(Get-Content $out -Raw).Contains("<table")) { $ok = $false; $detail = "Missing Table Tag"; }
    }
    Record-Result "Data" "CSV -> $t" $out $ok $detail
}

# JSON conversions
$jsonTargets = @("csv", "html", "md", "txt")
foreach ($t in $jsonTargets) {
    & $OrbitExe convert $srcJson --to $t | Out-Null
    $out = Join-Path $TestDir ("source_data_orbit." + $t)
    $ok = (Test-Path $out) -and ((Get-Item $out).Length -gt 0)
    $detail = "OK"
    if ($ok -and $t -eq "csv") {
        $lines = Get-Content $out
        if ($lines.Count -lt 2) { $ok = $false; $detail = "CSV has no rows"; }
    }
    Record-Result "Data" "JSON -> $t" $out $ok $detail
}

# XML conversions
$xmlTargets = @("json", "csv")
foreach ($t in $xmlTargets) {
    & $OrbitExe convert $srcXml --to $t | Out-Null
    $out = Join-Path $TestDir ("source_data_orbit." + $t)
    $ok = (Test-Path $out) -and ((Get-Item $out).Length -gt 0)
    Record-Result "Data" "XML -> $t" $out $ok "OK"
}

# --- STEP 6: TEST ALL DOCUMENT CONVERSIONS ---
Write-Host "`n[6/7] Verifying Document Conversions..." -ForegroundColor Yellow
$docTargets = @("pdf", "docx", "html", "txt")
foreach ($t in $docTargets) {
    & $OrbitExe convert $srcMd --to $t | Out-Null
    $out = Join-Path $TestDir ("source_doc_orbit." + $t)
    $ok = (Test-Path $out) -and ((Get-Item $out).Length -gt 0)
    $detail = "OK"
    if ($ok -and $t -eq "pdf") {
        $first = Get-Content $out -TotalCount 1
        if (!$first.StartsWith("%PDF")) { $ok = $false; $detail = "Invalid PDF"; }
    } elseif ($ok -and $t -eq "docx") {
        # Verify valid ZIP
        try {
            [System.IO.Compression.ZipFile]::OpenRead($out).Dispose()
        } catch {
            $ok = $false; $detail = "Invalid DOCX Zip";
        }
    }
    Record-Result "Document" "MD -> $t" $out $ok $detail
}

# --- STEP 7: TEST ALL TOOLS ---
Write-Host "`n[7/7] Verifying Built-in Tools Suite..." -ForegroundColor Yellow
$tools = @("compress", "metadata", "grayscale", "rotate", "hash")
foreach ($tool in $tools) {
    & $OrbitExe tool $srcPng --action $tool | Out-Null
    if ($tool -eq "hash") {
        # Clipboard check
        Add-Type -AssemblyName System.Windows.Forms
        $clip = [System.Windows.Forms.Clipboard]::GetText()
        $ok = ($clip.Length -eq 64)
        Record-Result "Tool" "Tool: $tool" $srcPng $ok "SHA-256 length 64"
    } else {
        $expectedExt = switch ($tool) {
            "compress" { "_compressed.png" }
            "metadata" { "_clean.png" }
            "grayscale" { "_bw.png" }
            "rotate" { "_rotated.png" }
        }
        $out = Join-Path $TestDir ("source_image" + $expectedExt)
        $ok = (Test-Path $out) -and ((Get-Item $out).Length -gt 0)
        Record-Result "Tool" "Tool: $tool" $out $ok "OK"
    }
}

# Video tools: mute, snapshot
& $OrbitExe tool $srcMp4 --action mute | Out-Null
$outMute = Join-Path $TestDir "source_video_muted.mp4"
$okMute = (Test-Path $outMute) -and ((Get-Item $outMute).Length -gt 0)
Record-Result "Tool" "Video Tool: mute" $outMute $okMute "OK"

& $OrbitExe tool $srcMp4 --action snapshot | Out-Null
$outSnap = Join-Path $TestDir "source_video_cover.jpg"
$okSnap = (Test-Path $outSnap) -and ((Get-Item $outSnap).Length -gt 0)
Record-Result "Tool" "Video Tool: snapshot" $outSnap $okSnap "OK"

# --- SUMMARY REPORT ---
Write-Host "`n==========================================================" -ForegroundColor Cyan
$passedCount = ($results | Where-Object { $_.Passed }).Count
$totalCount = $results.Count
Write-Host "VERIFICATION RESULTS: $passedCount / $totalCount TESTS PASSED" -ForegroundColor $(if ($passedCount -eq $totalCount) { "Green" } else { "Red" })
Write-Host "==========================================================" -ForegroundColor Cyan
