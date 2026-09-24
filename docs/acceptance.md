# Приёмка и воспроизводимые команды

Это план проверок, не протокол успешного запуска. Каждый результат имеет состояние NOT_RUN / PASS / FAIL / BLOCKED с причиной, UTC, source revision/hash, средой, command, exit code, путями к логам и evidence. Если теста пока нет, ссылка на будущий test filter — план, запуск с 0 tests не PASS. Старые файлы не переиспользуются как новые результаты.

## Команды

PowerShell из корня `DrivingSchoolSim`. Вручную задайте `$dsUnity` абсолютным путём к установленному **6000.3.10f1**; путь не угадывается. Закройте Editor для этого проекта перед batch запуском. Команды Prepare/Build изменяют проект/выходы; выполняйте на согласованном checkout и проверяйте diff. C00 безопасна и не запускает генератор.

### C00 — ревизия, доступность и каталоги

```powershell
$dsProject = (Get-Location).Path
# Присвойте фактический путь к Unity.exe в $dsUnity перед C01–C03.
Test-Path -LiteralPath $dsUnity
Get-Content ProjectSettings/ProjectVersion.txt
Get-FileHash Assets/DrivingSchool/Code/Contracts/Contracts.cs -Algorithm SHA256
New-Item -ItemType Directory -Force artifacts/reports | Out-Null
```

### C01 — существующие EditMode tests

```powershell
$dsArgs = @('-batchmode','-nographics','-projectPath',$dsProject,'-runTests','-testPlatform','EditMode','-testResults',"$dsProject/artifacts/reports/editmode.xml",'-logFile',"$dsProject/artifacts/reports/editmode.log")
$dsProcess = Start-Process -FilePath $dsUnity -ArgumentList $dsArgs -WindowStyle Hidden -PassThru -Wait
$dsProcess.ExitCode
[xml]$dsResults = Get-Content artifacts/reports/editmode.xml
$dsResults.'test-run' | Select-Object result,total,passed,failed,skipped
```

Пути проекта сейчас без пробелов. Для другого checkout с пробелами передавайте корректно quoted arguments по правилам PowerShell/Start-Process. Не добавлять `-quit` к test run до проверки runner behavior. Ожидаются реальные тесты и failed0; базовый прочитанный каркас содержит12 `[Test]`. По мере добавления тестов число растёт. XML и log обязательны; exit0 без tests недостаточен.

### C02 — prepare/import

```powershell
$dsArgs = @('-batchmode','-projectPath',$dsProject,'-executeMethod','DrivingSchool.Editor.ProjectBuilder.Prepare','-quit','-logFile',"$dsProject/artifacts/reports/prepare.log")
$dsProcess = Start-Process -FilePath $dsUnity -ArgumentList $dsArgs -WindowStyle Hidden -PassThru -Wait
$dsProcess.ExitCode
Select-String -Path artifacts/reports/prepare.log -Pattern 'DS_PREPARE_PASS|error CS|Exception'
```

Нужны существующие три FBX по ожидаемым путям. Для T01 второй Prepare должен не создавать дубликаты и не терять ручные ресурсы; исходный builder это не гарантировал. Import dimensions/readability/materials проверяются в Editor отдельно от exit code.

### C03 — Windows build

```powershell
$dsArgs = @('-batchmode','-projectPath',$dsProject,'-executeMethod','DrivingSchool.Editor.ProjectBuilder.Build','-quit','-logFile',"$dsProject/artifacts/reports/build.log")
$dsProcess = Start-Process -FilePath $dsUnity -ArgumentList $dsArgs -WindowStyle Hidden -PassThru -Wait
$dsProcess.ExitCode
Test-Path Builds/Windows/DrivingSchoolSim.exe
Select-String -Path artifacts/reports/build.log -Pattern 'DS_BUILD_PASS|error CS|Exception'
```

Текущий BuildOptions.Development пригоден для диагностики; performance release gate отдельно требует согласованный release profile.

### C04 — существующий player smoke

```powershell
$dsArgs = @('--smoke','--capture',"$dsProject/artifacts/reports/player-smoke.png",'-logFile',"$dsProject/artifacts/reports/player-smoke.log")
$dsProcess = Start-Process -FilePath "$dsProject/Builds/Windows/DrivingSchoolSim.exe" -ArgumentList $dsArgs -WindowStyle Hidden -PassThru -Wait
$dsProcess.ExitCode
Select-String -Path artifacts/reports/player-smoke.log -Pattern 'DS_PLAYER_SMOKE_PASS|Exception'
```

Нужен графический сеанс. Это сохранение/загрузка sample-world и программный start-stop через direct Tick; не вождение, не GPU benchmark и не hardware test. Запись идёт в `Application.persistentDataPath/PrototypeWorlds/smoke-map.json`; существующий одноимённый smoke-save заменяется с backup. В изолированном test user profile предпочтительнее. Скриншот может не появиться при проблеме rendering; не принимать только log marker.

### C05 — воспроизводимый ручной сценарий

Запустить Showroom/нужную сцену из Editor, выбрать карточку и задокументировать точные inputs/poses и ожидаемый результат. Записать video/screenshots и measurement JSON. Для G27: модель, USB/device ID (без персональных данных), driver version, калибровка, bindings. Для VR: HMD/runtime/version, refresh/resolution, rendering mode. Для performance: hardware/driver/build/profile, route, seed, traffic counts, weather, mirrors, warm-up и duration. Не заменять аппаратную проверку клавиатурным mock.

## Критерии по ID

- **A01 platform/reproducibility (R01/R15):** C01–C03 на закреплённой версии, compile clean, ненулевые tests, prepare twice, asmdefs не потеряли границы. Package resolution/Unity лицензия и ошибки импорта сохраняются в log. T01.
- **A02 asset (R05/R15):** все G01–G08 из asset-standard с хешами, импортированными bounds/LOD counts, screenshots/mesh intersections и Unity collision check. Blender only закрывает лишь Blender подпункт. T02/T10.
- **A03 UI (R01):** девять экранов согласованного прототипа доступны в uGUI/TMP, layout1080p и1280×720, корректный Cyrillic, mouse/keyboard focus, Back/Escape, неработающие будущие функции подписаны. HTML prototype принимается отдельно; не считать его Unity UI. T03.
- **A04 input (R02):** neutral/full axes finite и в диапазоне, независимые pedals, обратная ось и min/max/deadzone, H gears−1/0/1…6, профиль reload, unplug/replug/focus loss не оставляет газ. Синтетический adapter suite и реальный G27 evidence раздельно. T04/T05.
- **A05 FFB (R03):** mock подтверждает clamp/NaN handling/Stop idempotence и all lifecycle paths; реальный wheel torque правильного знака, zero на pause/unplug/exit, восстановление только явным разрешением управления. Не запускать сильный torque без оператора у руля; начинать малой амплитудой. T06.
- **A06 drivetrain (R04):** engine phases, idle/crank/stall/restart, neutral no axle torque, reverse sign, pedal1 no clutch torque, bounded slip, gear transitions и automatic selector rules. На incline hill start/rollback/brakes/handbrake, контролируемый тормозной путь и yaw response сравниваются с утверждёнными calibration envelopes. FPS30/60/120 не меняет tick time. Все величины finite после длительного сценария. T07–T10.
- **A07 graph (R07/R15):** v1 fixture migrates to v2 equivalent; lane IDs/references/directions/curves валидны, crossings и stop lines имеют связи. Invalid finite/duplicate/broken successor/unsupported version отклоняются. T11.
- **A08 large world (R06):** synthetic10×10km traversal в обоих направлениях и через отрицательные cells, bounded resident chunks, no road collision gaps; origin shifts сохраняют canonical pose/evidence и velocity, desktop view скачок≤1px при заданной контрольной камере. Допуск collision jitter согласовать с solver. T12/T13.
- **A09 runtime editor (R07/R08):** в Windows player создать grid straight, curve, lane connection, object; undo/redo; save/reload в ту же graph revision; geometry/AI/rules видят изменения одновременно. Dangling link/invalid curve commit отклонён без порчи прежней карты. T14.
- **A10 persistence (R08/R15):** full-field roundtrip, backup/recovery, interrupted tmp, invalid JSON/unknown version, write denied/no space и conflict writes; исходный save не теряется и UI сообщает причину. T15.
- **A11 agents (R09):** небольшой scripted intersection, остановка перед красным/занятой зоной, lane successor continuity, pedestrians on crossing, seeded repeat; нет teleport при streaming. Отсутствие сложного поведения явно отмечено. T16/T17.
- **A12 rules (R10):** каждое добавленное семейство имеет positive/negative boundary fixtures, version, one event per violation transition, world coordinates/evidence/explanation/penalty, no double count across shift. Источник нормативных значений подтверждён. T18.
- **A13 mirrors (R05):** три зеркала с правильными L/R/motion/occlusion/UV, динамический объект виден из driver eye, нет рекурсии/утечек RT после enable/disable; mono и XR результаты отдельно. T23.
- **A14 PCVR (R01):** реальный HMD, корректный масштаб/pose/recenter/pause/disconnect, controller/UI navigation, обе глазные картинки и зеркала, нет camera attachment jitter. Target HMD frame budget задаётся до измерения. T24.
- **A15 weather (R11):** одинаковая controlled speed/route/seed на dry/wet/snow/ice; материал/визуальный профиль соответствует friction, поверхности finite, переход не вызывает скачка силы вне утверждённого envelope. Сохраняются initial conditions и distance/traction traces. T19.
- **A16 lessons (R12):** все8 IDs из requirements имеют brief, evaluator/version, start/success/failure/cancel/retry, пограничные тесты, result с assists/revisions/evidence. Не завершать при неподвижности на spawn; wrong order/out-of-zone не проходят. Городской маршрут отдельно. T20/T21.
- **A17 theory (R13):** верные индексы/IDs/revision/объяснение, author pack явно неофициальный; неизвестный источник не даёт official badge; verified import имеет provenance и права. Ошибки индексов/empty answers/duplicate ids отклоняются. T22.
- **A18 performance (R14):** Ryzen2700X, RTX2080,32GB,1920×1080, согласованный release profile,5min warm-up+10min drive, три зеркала и закреплённые traffic/weather counts. Цель steady-state p95 frame≤16.67ms, p99≤25ms; все spikes>50ms при streaming расследованы. Отдельные main/render/GPU timings, RAM/VRAM peak, GC и loading stalls; graphics quality fixes не скрываются. VR gate отдельно. T25.
- **A19 evidence (R15):** manifest references существуют, SHA256 совпадают с проверенным asset/code, status не смешивает proposed/implemented/checked; negative controls реально fail; new revision инвалидирует прежний PASS. T26.

Отказ приёмки не исправляется снижением requirement задним числом. Допустимо явно согласованное изменение целевого бюджета/профиля с новой ревизией и повторным измерением.
