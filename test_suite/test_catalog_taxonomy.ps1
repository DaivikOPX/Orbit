# Verify Taxonomy & Brain for all file types
$ErrorActionPreference = "Stop"

[System.Reflection.Assembly]::LoadFrom("D:\orbit\Saturn.exe") | Out-Null

function Test-ExtCategory {
    param(
        [string]$Ext,
        [string]$ExpectedPrimaryAction,
        [string[]]$ForbiddenActions
    )

    $petals = [SaturnWindows.SaturnCatalog]::GetPetalsForExtension($Ext, "convert")
    Write-Host "`nExtension: .$Ext (Total Petals: $($petals.Count))" -ForegroundColor Cyan
    foreach ($p in $petals) {
        Write-Host "  -> $($p.Title) ($($p.ExtOrId)) mode=$($p.Mode)" -ForegroundColor Gray
    }

    $first = $petals[0].Title
    if ($first -ne $ExpectedPrimaryAction) {
        throw "Failed: Expected primary action '$ExpectedPrimaryAction' for .$Ext but got '$first'"
    }

    foreach ($forbidden in $ForbiddenActions) {
        $found = $petals | Where-Object { $_.ExtOrId -eq $forbidden }
        if ($found) {
            throw "Failed: Found forbidden action '$forbidden' for .$Ext!"
        }
    }
    Write-Host "PASS: .$Ext correctly categorized with '$ExpectedPrimaryAction' and no forbidden actions!" -ForegroundColor Green
}

$imageFormats = @("png", "jpg", "jpeg", "webp", "ico", "bmp", "tiff", "gif")

# 1. Executables
Test-ExtCategory "exe" "RUN AS ADMIN" $imageFormats
Test-ExtCategory "msi" "RUN AS ADMIN" $imageFormats
Test-ExtCategory "bat" "RUN AS ADMIN" $imageFormats
Test-ExtCategory "ps1" "RUN AS ADMIN" $imageFormats

# 2. System Binaries & DLLs
Test-ExtCategory "dll" "CHECKSUM" $imageFormats
Test-ExtCategory "sys" "CHECKSUM" $imageFormats

# 3. Archives
Test-ExtCategory "zip" "EXTRACT HERE" $imageFormats
Test-ExtCategory "tar" "EXTRACT HERE" $imageFormats

# 4. Code / Scripts
Test-ExtCategory "cs" "EDIT CODE" $imageFormats
Test-ExtCategory "py" "EDIT CODE" $imageFormats
Test-ExtCategory "js" "EDIT CODE" $imageFormats

# 5. Real Image (MUST have image formats!)
$pngPetals = [SaturnWindows.SaturnCatalog]::GetPetalsForExtension("png", "convert")
$pngHasJpg = $pngPetals | Where-Object { $_.ExtOrId -eq "jpg" }
if (-not $pngHasJpg) { throw "PNG must have JPG conversion!" }
Write-Host "`nPASS: .png correctly offers real image conversions (JPG, WEBP, etc.)!" -ForegroundColor Green

# 6. Test CLI Protection Against Converting EXE
Write-Host "`nTesting CLI protection against converting .exe to image..." -ForegroundColor Cyan
$dummyExe = "D:\orbit\test_suite\test_output\dummy_app.exe"
[System.IO.File]::WriteAllBytes($dummyExe, [byte[]]@(0x4D, 0x5A, 0x90, 0x00))
try {
    [SaturnWindows.SaturnEngine]::ExecuteConversion($dummyExe, "png")
    throw "Should have thrown InvalidOperationException!"
} catch [System.InvalidOperationException] {
    Write-Host "PASS: Caught expected InvalidOperationException: $($_.Exception.Message)" -ForegroundColor Green
}

Write-Host "`n=======================================================" -ForegroundColor Cyan
Write-Host "   ALL FILE TAXONOMY & DOMAIN BRAIN TESTS PASSED!     " -ForegroundColor Green
Write-Host "=======================================================" -ForegroundColor Cyan
