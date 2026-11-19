# Build and Release Script for the AI edition (Geukkaeng Jokbo AI)
#
# Same source as the standard app, built with -p:Edition=AI (adds the #if AI_EDITION code).
# It is a separate app: package id QuizHelperAI, its own install folder, shortcut and update feed.
# Output goes to Releases_AI\ so the standard Releases\ folder is never touched.
#
# Upload (the AI app updates itself from the qsearch_exe repo, NOT qplay_search):
#   gh release create ai-v<version> -R taehee-kimee/qsearch_exe --title "<Korean app name> AI v<version>" Releases_AI/*

$scriptDir = $PSScriptRoot
$projectDir = Join-Path $scriptDir "..\QuizHelper"
$releaseDir = Join-Path $scriptDir "..\Releases_AI"
$publishDir = Join-Path $projectDir "publish_ai"

# The AI edition has its own version line, kept in QuizHelper.csproj as <AiVersion>.
$csprojPath = Join-Path $projectDir "QuizHelper.csproj"
$versionMatch = Select-String -Path $csprojPath -Pattern '<AiVersion>([^<]+)</AiVersion>'
if (-not $versionMatch) {
    Write-Host "No <AiVersion> found in QuizHelper.csproj - set it before releasing." -ForegroundColor Red
    exit 1
}
$version = $versionMatch.Matches[0].Groups[1].Value.Trim()

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  QuizHelper AI Release Builder v$version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 1. Clean previous publish output. Releases_AI is kept: vpk needs the previous
#    full package there to produce a delta for the next version.
Write-Host "1. Cleaning previous build..." -ForegroundColor Yellow
if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}
if (-not (Test-Path $releaseDir)) {
    New-Item -ItemType Directory -Path $releaseDir | Out-Null
}

# 2. Publish Project (AI edition)
Write-Host "2. Publishing project (Edition=AI)..." -ForegroundColor Yellow
dotnet publish "$projectDir\QuizHelper.csproj" -c Release -p:Edition=AI -o $publishDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed!" -ForegroundColor Red
    exit 1
}

# Make sure the AI code really is in this build.
$dllText = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes("$publishDir\QuizHelper.dll"))
if (-not $dllText.Contains("AiAskService")) {
    Write-Host "The published build has no AI code - Edition=AI was not applied." -ForegroundColor Red
    exit 1
}

# 3. Create Package with Velopack
Write-Host "3. Creating Velopack release package..." -ForegroundColor Yellow

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Host "vpk tool not found. Installing..." -ForegroundColor Yellow
    dotnet tool install -g vpk
}

# The Korean title is built from character codes on purpose: Windows PowerShell 5.1 reads a
# BOM-less script as ANSI, which turned a literal Korean title into mojibake in the shortcut
# and app names (v1.0.0, first upload).
$title = (-join [char[]](0xADF8, 0xAE7D, 0xC871, 0xBCF4)) + " AI"
vpk pack -u "QuizHelperAI" -v $version -p $publishDir -o $releaseDir --mainExe "QuizHelper.exe" --packTitle $title --icon "$projectDir\icon.ico"

if ($LASTEXITCODE -ne 0) {
    Write-Host "Packaging failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Build Success!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "Setup file created at: $releaseDir\QuizHelperAI-win-Setup.exe"
Write-Host ""
