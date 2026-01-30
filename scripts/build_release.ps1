# Build and Release Script for QuizHelper

$version = "1.0.5"
$scriptDir = $PSScriptRoot
$projectDir = Join-Path $scriptDir "..\QuizHelper"
$releaseDir = Join-Path $scriptDir "..\QuizHelper\Releases"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  QuizHelper Release Builder v$version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 1. Clean previous build
Write-Host "1. Cleaning previous build..." -ForegroundColor Yellow
if (Test-Path $releaseDir) {
    Remove-Item $releaseDir -Recurse -Force
}
New-Item -ItemType Directory -Path $releaseDir | Out-Null

# 2. Publish Project
Write-Host "2. Publishing project..." -ForegroundColor Yellow
dotnet publish "$projectDir\QuizHelper.csproj" -c Release -o "$projectDir\publish"

if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed!" -ForegroundColor Red
    exit 1
}

# 3. Create Package with Velopack
Write-Host "3. Creating Velopack release package..." -ForegroundColor Yellow

# Ensure vpk is installed
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Host "vpk tool not found. Installing..." -ForegroundColor Yellow
    dotnet tool install -g vpk
}

# Pack the release
vpk pack -u "QuizHelper" -v $version -p "$projectDir\publish" -o $releaseDir --mainExe "QuizHelper.exe"

if ($LASTEXITCODE -ne 0) {
    Write-Host "Packaging failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Build Success!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "Setup file created at: $releaseDir\QuizHelper-win-Setup.exe"
Write-Host ""
