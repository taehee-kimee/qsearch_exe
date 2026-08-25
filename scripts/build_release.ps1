# Build and Release Script for QuizHelper

# Keep in step with the "v<major>" badge in MainWindow.xaml (checked below)
$version = "2.0.1"
$scriptDir = $PSScriptRoot
$projectDir = Join-Path $scriptDir "..\QuizHelper"
$releaseDir = Join-Path $scriptDir "..\Releases"

# Guard: the release version and the UI badge must agree on the major version,
# otherwise the shipped build advertises a version the app does not show.
$badgeMatch = Select-String -Path (Join-Path $projectDir "MainWindow.xaml") -Pattern 'Text="v(\d+)"'
if (-not $badgeMatch) {
    Write-Host "Could not find the version badge in MainWindow.xaml" -ForegroundColor Red
    exit 1
}
$badgeMajor = $badgeMatch.Matches[0].Groups[1].Value
$versionMajor = $version.Split('.')[0]
if ($badgeMajor -ne $versionMajor) {
    Write-Host "Version mismatch: MainWindow.xaml shows v$badgeMajor but the script version is $version" -ForegroundColor Red
    Write-Host "Update whichever is wrong before releasing." -ForegroundColor Red
    exit 1
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  QuizHelper Release Builder v$version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 1. Clean previous build
Write-Host "1. Cleaning previous build..." -ForegroundColor Yellow
if (Test-Path $releaseDir) {
    Remove-Item $releaseDir -Recurse -Force
}
# Clean publish directory to avoid stale files
if (Test-Path "$projectDir\publish") {
    Remove-Item "$projectDir\publish" -Recurse -Force
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
