# Все Unity-проверки, о которых просили агенты, одним запуском.
# Запуск из корня проекта, Unity Editor этого проекта закрыт, дерево git чистое:
#   powershell -ExecutionPolicy Bypass -File tools\verify-unity.ps1
# Что делает:
#   1. tools\check.ps1 — компиляция + все EditMode-тесты (если не собралось — дальше не идём);
#   2. генераторы: UI-префабы, полигон машины, транспорт, пешеходы (каждый — отдельный запуск Unity);
#   3. печатает сводку и что осталось проверить руками в Play Mode.
# Итог пишется в artifacts\reports\verify-<время>.md — его можно прислать Claude целиком.
# Генераторы меняют сцены/префабы/материалы: после прогона посмотрите `git status` и закоммитьте
# результат отдельным коммитом `gen: ...` (см. CLAUDE.md, раздел Git).

param(
    [string]$Unity = 'E:\unityroot\6000.3.10f1\Editor\Unity.exe',
    [switch]$SkipTests,
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
$reports = Join-Path $project 'artifacts\reports'
New-Item -ItemType Directory -Force $reports | Out-Null
$summary = Join-Path $reports "verify-$stamp.md"
$rows = New-Object System.Collections.Generic.List[string]

if (-not (Test-Path -LiteralPath $Unity)) { Write-Host "Не найден Unity.exe: $Unity" -ForegroundColor Red; exit 2 }
if (-not $AllowDirty -and (git -C $project status --porcelain)) {
    Write-Host 'В рабочей копии есть незакоммиченные изменения. Закоммитьте их или запустите с -AllowDirty.' -ForegroundColor Yellow
    exit 2
}

function Add-Row([string]$step, [string]$status, [string]$details) {
    $rows.Add("| $step | **$status** | $details |")
    $color = switch ($status) { 'PASS' { 'Green' } 'FAIL' { 'Red' } default { 'Yellow' } }
    Write-Host ("{0,-28} {1,-8} {2}" -f $step, $status, $details) -ForegroundColor $color
}

# 1. Компиляция и тесты
if ($SkipTests) {
    Add-Row 'Компиляция + EditMode' 'NOT_RUN' 'пропущено (-SkipTests)'
} else {
    Write-Host "`n=== 1. Компиляция и EditMode-тесты ===" -ForegroundColor Cyan
    & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'check.ps1') -Unity $Unity
    $code = $LASTEXITCODE
    $xml = Get-ChildItem $reports -Filter 'check-*.xml' | Sort-Object LastWriteTime | Select-Object -Last 1
    $log = Get-ChildItem $reports -Filter 'check-*.log' | Sort-Object LastWriteTime | Select-Object -Last 1
    switch ($code) {
        0 { Add-Row 'Компиляция + EditMode' 'PASS' "exit 0, $($xml.Name)" }
        1 { Add-Row 'Компиляция + EditMode' 'FAIL' "упали тесты, $($xml.Name)" }
        default {
            Add-Row 'Компиляция + EditMode' 'BLOCKED' "не собралось/не запустилось, $($log.Name)"
            $rows.Add(''); $rows.Add('Дальше не запускалось: без компиляции генераторы не работают.')
        }
    }
    if ($code -ge 2) {
        Set-Content -Path $summary -Encoding UTF8 -Value (@("# Unity-проверка $stamp", '', '| Шаг | Статус | Детали |', '|---|---|---|') + $rows)
        Write-Host "`nСводка: $summary"; exit 2
    }
}

# 2. Генераторы (executeMethod). Маркер — строка в логе при успехе.
$steps = @(
    @{ Name = 'UI: префабы меню/HUD';    Method = 'DrivingSchool.Editor.UIBuilder.BuildAll';                Marker = 'UI_PREFABS_BUILT' },
    @{ Name = 'Полигон машины (сцена)';  Method = 'DrivingSchool.Editor.VehicleTestRangeBuilder.Build';     Marker = 'VEHICLE_TEST_RANGE_BUILD_PASS' },
    @{ Name = 'Транспорт (импорт+демо)'; Method = 'DrivingSchool.Editor.TransitKitBuilder.Build';           Marker = 'TRANSIT_UNITY_PASS' },
    @{ Name = 'Пешеходы (импорт+шоурум)'; Method = 'DrivingSchool.Editor.PedestrianAssetBuilder.Build';     Marker = 'PEDESTRIAN_IMPORT_PASS' }
)
$i = 2
foreach ($s in $steps) {
    Write-Host "`n=== $i. $($s.Name) ===" -ForegroundColor Cyan; $i++
    $short = ($s.Method -split '\.')[-2]
    $log = Join-Path $reports "verify-$stamp-$short.log"
    $a = @('-batchmode', '-projectPath', "`"$project`"", '-executeMethod', $s.Method, '-quit', '-logFile', "`"$log`"")
    $p = Start-Process -FilePath $Unity -ArgumentList $a -WindowStyle Hidden -PassThru -Wait
    $hasMarker = (Test-Path $log) -and (Select-String -Path $log -Pattern $s.Marker -SimpleMatch -Quiet)
    $err = $null
    if (Test-Path $log) {
        $err = Select-String -Path $log -Pattern '(Exception|error CS\d+)' | Select-Object -First 1
    }
    $logName = Split-Path $log -Leaf
    if ($p.ExitCode -eq 0 -and $hasMarker) { Add-Row $s.Name 'PASS' "exit 0, $($s.Marker), $logName" }
    else {
        $why = if ($err) { ($err.Line.Trim() -replace '\|', '/').Substring(0, [Math]::Min(160, $err.Line.Trim().Length)) } else { "нет маркера $($s.Marker)" }
        Add-Row $s.Name 'FAIL' "exit $($p.ExitCode), $why, $logName"
    }
}

$manual = @(
    '',
    '## Осталось руками (Play Mode, смотрит человек)',
    '- [ ] Полигон машины: открыть `Scenes/VehicleTestRange.unity`, Play, **F8** — самопроверка ≈40 с, в логе `VEHICLE_SELFCHECK PASS`. Затем ручной список из `docs/vehicle-test-range.md` («Ручная приёмка»).',
    '- [ ] Меню: префаб `Prefabs/UI/MainMenu.prefab` в Game View 1920×1080 и 1280×720 — кириллица без квадратов, фокус с клавиатуры (T03).',
    '- [ ] Транспорт: демо-сцена — перёд/двери/колёса на месте; снимок `-executeMethod DrivingSchool.Editor.TransitKitBuilder.Capture` (без -nographics).',
    '- [ ] Пешеходы: `Scenes/PedestrianShowroom.unity`, пешеход идёт/бежит без скольжения, `artifacts/visual-review/pedestrians/unity-lineup.png`.',
    '',
    'После прогона: `git status` — сгенерированные сцены/префабы/материалы закоммитить отдельным коммитом `gen: ...`.'
)
$rev = git -C $project rev-parse --short HEAD
$branch = git -C $project rev-parse --abbrev-ref HEAD
Set-Content -Path $summary -Encoding UTF8 -Value (@("# Unity-проверка $stamp", '', "Ветка $branch, ревизия $rev", '', '| Шаг | Статус | Детали |', '|---|---|---|') + $rows + $manual)
Write-Host "`nСводка: $summary"
Write-Host 'Изменённые генераторами файлы:'; git -C $project status --short | Select-Object -First 30
