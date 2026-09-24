# Проверка проекта одной командой: компиляция + EditMode-тесты Unity.
# Запуск из корня проекта (Editor этого проекта должен быть закрыт):
#   powershell -ExecutionPolicy Bypass -File tools\check.ps1
# Параметры:
#   -Unity  путь к Unity.exe (по умолчанию из CLAUDE.md)
#   -Filter фильтр тестов Unity (например "DrivingSchool.Tests.EngineModelTests")
# Код выхода: 0 = PASS, 1 = FAIL (упали тесты), 2 = BLOCKED (не собралось / не запустилось).

param(
    [string]$Unity = 'E:\unityroot\6000.3.10f1\Editor\Unity.exe',
    [string]$Filter = ''
)

$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
$reports = Join-Path $project 'artifacts\reports'
$xml = Join-Path $reports "check-$stamp.xml"
$log = Join-Path $reports "check-$stamp.log"
New-Item -ItemType Directory -Force $reports | Out-Null

function Finish([string]$status, [int]$code, [string]$note) {
    $rev = (git -C $project rev-parse --short HEAD 2>$null)
    $dirty = if (git -C $project status --porcelain 2>$null) { ' (+ незакоммиченные изменения)' } else { '' }
    Write-Host ''
    Write-Host "СТАТУС: $status" -ForegroundColor $(if ($code -eq 0) { 'Green' } elseif ($code -eq 1) { 'Red' } else { 'Yellow' })
    Write-Host "Ревизия: $rev$dirty"
    if ($note) { Write-Host $note }
    Write-Host "Лог: $log"
    if (Test-Path $xml) { Write-Host "Результаты: $xml" }
    exit $code
}

if (-not (Test-Path -LiteralPath $Unity)) { Finish 'BLOCKED' 2 "Не найден Unity.exe: $Unity (передайте -Unity <путь>)" }
if (Test-Path (Join-Path $project 'Temp\UnityLockfile')) {
    try { [IO.File]::Open((Join-Path $project 'Temp\UnityLockfile'), 'Open', 'ReadWrite', 'None').Close() }
    catch { Finish 'BLOCKED' 2 'Проект открыт в Unity Editor — закройте его и запустите снова.' }
}

$argsList = @('-batchmode', '-nographics', '-projectPath', "`"$project`"", '-runTests', '-testPlatform', 'EditMode',
              '-testResults', "`"$xml`"", '-logFile', "`"$log`"")
if ($Filter) { $argsList += @('-testFilter', $Filter) }

Write-Host "Unity: компиляция и EditMode-тесты... (обычно 1–5 минут)"
$p = Start-Process -FilePath $Unity -ArgumentList $argsList -WindowStyle Hidden -PassThru -Wait
$exit = $p.ExitCode

# Ошибки компиляции C#
$compileErrors = @()
if (Test-Path $log) {
    $compileErrors = Select-String -Path $log -Pattern 'error CS\d+' | ForEach-Object { $_.Line.Trim() } | Sort-Object -Unique
}
if ($compileErrors.Count -gt 0) {
    Write-Host "`nОшибки компиляции ($($compileErrors.Count)):" -ForegroundColor Red
    $compileErrors | Select-Object -First 30 | ForEach-Object { Write-Host "  $_" }
    Finish 'BLOCKED' 2 "Код не компилируется (exit $exit)."
}

if (-not (Test-Path $xml)) { Finish 'BLOCKED' 2 "Unity завершился с кодом $exit и не записал результаты тестов — смотрите лог." }

[xml]$r = Get-Content -LiteralPath $xml -Encoding UTF8
$run = $r.'test-run'
Write-Host ("`nТестов: {0}   прошло: {1}   упало: {2}   пропущено: {3}" -f $run.total, $run.passed, $run.failed, $run.skipped)

if ([int]$run.total -eq 0) { Finish 'BLOCKED' 2 'Не найдено ни одного теста — это не PASS.' }

$failed = $r.SelectNodes("//test-case[@result='Failed']")
if ($failed.Count -gt 0) {
    Write-Host "`nУпавшие тесты:" -ForegroundColor Red
    foreach ($t in $failed) {
        $msg = ($t.failure.message.'#cdata-section' -as [string])
        if (-not $msg) { $msg = [string]$t.failure.message }
        $msg = ($msg -split "`n")[0].Trim()
        Write-Host "  - $($t.fullname)"
        if ($msg) { Write-Host "      $msg" -ForegroundColor DarkGray }
    }
    Finish 'FAIL' 1 "Unity exit code: $exit"
}

Finish 'PASS' 0 "Unity exit code: $exit"
