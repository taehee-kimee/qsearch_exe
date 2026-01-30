# CSV 데이터 정규화 스크립트
# 모든 CSV 파일을 TAB 구분자 형식으로 변환합니다.
# 사용법: .\scripts\normalize_csv.ps1

param(
    [string]$DataPath = "..\data"
)

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$dataFolder = Join-Path $scriptPath $DataPath

if (-not (Test-Path $dataFolder)) {
    Write-Host "Error: Data folder not found: $dataFolder" -ForegroundColor Red
    exit 1
}

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  CSV Data Normalization Script" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

$csvFiles = Get-ChildItem -Path $dataFolder -Filter "*.csv"
$totalConverted = 0
$totalSkipped = 0
$totalErrors = 0

foreach ($file in $csvFiles) {
    Write-Host "Processing: $($file.Name)" -ForegroundColor Yellow
    
    try {
        # UTF-8로 파일 읽기
        $lines = Get-Content -Path $file.FullName -Encoding UTF8
        $newLines = @()
        $convertedCount = 0
        $lineNumber = 0
        
        foreach ($line in $lines) {
            $lineNumber++
            
            # 빈 줄은 건너뛰기
            if ([string]::IsNullOrWhiteSpace($line)) {
                continue
            }
            
            # 이미 TAB으로 구분되어 있는지 확인
            if ($line -match "`t") {
                $newLines += $line
                continue
            }
            
            # 헤더 라인 처리 (Question Answer 형태)
            if ($line -match "^Question\s+Answer\s*$") {
                $newLines += "Question`tAnswer"
                $convertedCount++
                continue
            }
            
            # 마지막 '?' 위치 찾기
            $lastQuestionIdx = $line.LastIndexOf('?')
            
            if ($lastQuestionIdx -gt 0 -and $lastQuestionIdx -lt ($line.Length - 1)) {
                # '?' 뒤에 뭔가 있는 경우
                $question = $line.Substring(0, $lastQuestionIdx + 1).Trim()
                $answer = $line.Substring($lastQuestionIdx + 1).Trim()
                
                if (-not [string]::IsNullOrWhiteSpace($answer)) {
                    $newLines += "$question`t$answer"
                    $convertedCount++
                    continue
                }
            }
            
            # 변환 불가능한 라인 - 경고 출력
            $preview = $line
            if ($line.Length -gt 60) {
                $preview = $line.Substring(0, 60) + "..."
            }
            Write-Host "  Warning [Line $lineNumber]: Could not parse: $preview" -ForegroundColor DarkYellow
            $newLines += $line
        }
        
        # 파일 저장 (UTF-8 without BOM for better compatibility)
        [System.IO.File]::WriteAllLines($file.FullName, $newLines, [System.Text.UTF8Encoding]::new($false))
        
        if ($convertedCount -gt 0) {
            Write-Host "  Converted $convertedCount lines" -ForegroundColor Green
            $totalConverted += $convertedCount
        } else {
            Write-Host "  Already normalized (no changes needed)" -ForegroundColor Gray
            $totalSkipped++
        }
    }
    catch {
        Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
        $totalErrors++
    }
    
    Write-Host ""
}

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Summary" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "Files processed: $($csvFiles.Count)"
Write-Host "Lines converted: $totalConverted" -ForegroundColor Green
Write-Host "Files already normalized: $totalSkipped" -ForegroundColor Gray
if ($totalErrors -gt 0) {
    Write-Host "Errors: $totalErrors" -ForegroundColor Red
}
Write-Host ""
Write-Host "Done!" -ForegroundColor Green
