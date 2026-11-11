# 수집 문제 족보 병합 루틴을 Windows 작업 스케줄러에 등록합니다.
# 매일 23:30 실행 (그 시간에 PC가 꺼져 있었으면 다음에 켰을 때 실행)
# 해제: Unregister-ScheduledTask -TaskName 'QuizHelper 수집문제 족보병합' -Confirm:$false

$taskName = 'QuizHelper 수집문제 족보병합'
$script = Join-Path $PSScriptRoot 'merge_collected.py'
$repo = Split-Path $PSScriptRoot -Parent
$python = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\python.exe'
if (-not (Test-Path $python)) { $python = (Get-Command python).Source }

$action = New-ScheduledTaskAction -Execute $python -Argument "-I `"$script`" --push" -WorkingDirectory $repo
$trigger = New-ScheduledTaskTrigger -Daily -At '23:30'
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 30)

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -Force `
    -Description "그깽족보: 수집한 문제를 설치형·웹 족보에 합치고 GitHub에 올림. 로그: %APPDATA%\QuizHelper\collected\merge_log.txt" | Out-Null

$info = Get-ScheduledTask -TaskName $taskName | Get-ScheduledTaskInfo
Write-Host "등록 완료: $taskName (다음 실행: $($info.NextRunTime))" -ForegroundColor Green
