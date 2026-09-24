# DrivingSchoolSim — инструкции для Claude и агентов

Учебный автосимулятор для подготовки к экзамену по ПДД РФ (категория B), эталон по функциональности — City Car Driving.
Язык общения, документации и комментариев к задачам — русский. Идентификаторы кода — английский.

## Стек и окружение
- Unity **6000.3.10f1**, URP 17.3, Input System 1.18 (activeInputHandler = Both), uGUI/TMP, OpenXR. Windows x64.
- Unity.exe: `E:\unityroot\6000.3.10f1\Editor\Unity.exe`.
- Целевое железо: Ryzen 2700X / RTX 2080 / 32 GB, 1920×1080 @ 60 fps. Руль Logitech G27 (900°, 3 педали, H-шифтер 6+R).

## Где что лежит
- `Assets/DrivingSchool/Code/<Module>/` — код, модуль = asmdef:
  - `Contracts` — DTO и порты (DriverCommand, VehicleState, IInputSource, RuleEvent…). **Без UnityEngine.**
  - `Simulation` (EngineModel, DrivetrainMath), `Rules` (RuleEvaluator), `Learning` (CourseSession) — чистый C#, `noEngineReferences: true`.
  - `World`, `Input` — зависят от Unity. `Presentation` — MonoBehaviour-обвязка. `Presentation/Physics` — Unity-адаптер физики.
  - `Editor` — генераторы сцен и ассетов. `Tests` — NUnit EditMode.
- `Assets/DrivingSchool/Data/Training/course-v2.json` — данные учебной площадки (генерируется, см. ниже).
- `Assets/StreamingAssets/Examples/` — примеры world/lesson/theory/vehicle JSON.
- `ArtSource/` — Blender-исходники, `tools/` — Python-скрипты (Blender-экспорт, setup).
- `docs/` — требования, архитектура, ADR, форматы данных, приёмка, план, карточки задач `docs/tasks/T01–T26.md`.
- Актуальное состояние: `docs/audit-2026-09-23.md` (свежее) и `docs/current-state.md` (от 19.09).
- Репозиторий: GitHub `vioteo/drivingschoolsim`, основная ветка `master`. История git — источник истины о версиях; бэкап-папки (`*_Backup`, `artifacts/backup-*`) устарели и будут удалены.

## Жёсткие правила
1. **Чистые модули не ссылаются на UnityEngine.** Нужен Unity — пиши адаптер в `Presentation`/`Presentation/Physics`.
2. **Сгенерированное не правим руками.** `TrainingGroundBuilder`/`TrainingGroundLayout` перезаписывают сцену `Autodrome_Training.unity` и `course-v2.json`; `ProjectBuilder.Prepare` меняет сцены и настройки; `tools/setup_project.py` перезаписывает manifest, asmdef, примеры (`.gitignore` больше не трогает). Меняем генератор, а не результат. **Не запускать генераторы без явной просьбы.** Генератор запускается только на чистом дереве (`git status` пуст), его результат — отдельный коммит `gen:` с командой запуска в сообщении; перед коммитом просмотреть `git diff` результата.
3. Не трогать `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `Builds/` (в `.gitignore`, никогда не коммитить) и `.agents/` (архив прошлой команды агентов, лежит в git только для истории — не источник истины, не редактировать).
4. Единицы СИ, оси Unity: X вправо, Y вверх, Z вперёд. Сцепление: 1 = выжато (разомкнуто). Руль: −1…1. Педали: 0…1.
5. Игровые пороги ≠ правовые нормы. Любая ссылка на пункт ПДД или методику ГИБДД — с редакцией и источником; не выдумывать нормы. Официальные билеты — только из источника с правом использования.
6. Новый публичный тип/поле в `Contracts` — обновить `docs/data-formats.md`.

## Git
- **Ветки.** В `master` напрямую не коммитим. Одна задача = одна ветка = один PR: `<тип>/<Txx>-кратко` (например `feat/T07-clutch`). Параллельные агенты — каждый в своей ветке/worktree, не трогают одни и те же файлы.
- **Коммиты.** Маленькие и атомарные. Формат: `<тип>: <что сделано> (Txx)`, типы `feat fix docs test art gen chore`. Описание можно по-русски. Не смешивать в одном коммите код, результат генератора и ассеты.
- **`.meta`** коммитится вместе со своим ассетом, всегда. Переименование/перенос ассета — через Unity или `git mv` ассета и его `.meta` вместе; иначе ломаются GUID-ссылки.
- **Сцены и префабы** (YAML, Force Text) руками не мержить. Конфликт в `.unity`/`.prefab` — взять одну сторону и перегенерировать/повторить правку в Unity. Одну сцену одновременно меняет одна ветка.
- **Бинарники.** Git LFS пока не настроен, FBX/PNG/blend лежат в обычной истории. Новые файлы > 10 МБ не добавлять без согласования. GLB и рендеры для просмотра — не в `Assets/`.
- **Не коммитить:** логи (`*.log` в `.gitignore`), временные скриншоты и распаковки в корне, `Library/` и прочий кеш. Отчёты тестов в `artifacts/reports/` коммитить только как доказательство приёмки задачи.
- **Запрещено без явной просьбы:** `push --force` и переписывание истории `master`, `git reset --hard`, `git clean -fdx` (сносит и игнорируемый `Library/`), удаление веток, `git lfs migrate`.
- Перед завершением задачи: `git status` чистый, всё нужное закоммичено и запушено в ветку задачи.

## Модели и импорт
Работа с 3D-моделями — строго по `docs/art-pipeline.md`. Коротко:
- Модель = воспроизводимый скрипт `tools/build_<kit>.py` (Blender 5, `-b`) → `ArtSource/*.blend` → FBX в `Assets/DrivingSchool/Art/<Kit>/` → префаб через `Code/Editor/<Kit>Builder.cs`. Ручные настройки импорта в Inspector не делать.
- Blender: метры, масштаб 1, Z вверх, лицо в −Y, трансформации применены. FBX: `axis_forward='-Z', axis_up='Y'`, `apply_scale_options='FBX_SCALE_UNITS'`. Unity: `bakeAxisConversion = true`, корень префаба identity + дочерний `Model`.
- Имена объектов (`Wheel_FL`, `Pedal_*`, `MirrorSurface_*`, `Socket_*`, `COL_*`, `*_LOD0`) — контракт с кодом, не переименовывать.
- Версии через git, не через `_v1/_reviewed` в имени. GLB — только для просмотра, в `Assets/` не класть.
- Приёмка ассета — проверка в Unity, а не только рендер Blender.

## Проверка работы
Статус пишется только так: **PASS / FAIL / NOT_RUN / BLOCKED** + команда + exit code + путь к логу. Компиляция ≠ тесты ≠ работает в Play Mode.

```powershell
$u='E:\unityroot\6000.3.10f1\Editor\Unity.exe'; $p=(Get-Location).Path
# EditMode тесты (Editor проекта должен быть закрыт)
& $u -batchmode -nographics -projectPath $p -runTests -testPlatform EditMode `
  -testResults "$p/artifacts/reports/editmode.xml" -logFile "$p/artifacts/reports/editmode.log"
```
Подробные команды C00–C05 — `docs/acceptance.md`. Новый тест сначала должен падать на неверном входе.
В облачной сессии (Linux, без Unity) Unity-проверки не запускаются — статус **NOT_RUN**, а не PASS; проверка переносится на машину с Unity.

## Как работаем с задачами
- Одна задача = одна карточка (`docs/tasks/Txx.md` или новая) = один небольшой diff. Перед изменениями прочитать карточку, `docs/architecture.md` и затрагиваемые файлы целиком.
- Если задача упирается в решение, которого нет в документах, — остановиться и спросить, а не изобретать архитектуру.
- В конце: что изменено (файлы, коммиты, ветка), как проверено (статусы выше), что осталось/риски. Логи — в `artifacts/reports/`, не в корень.

## Приоритеты (из аудита 23.09)
1. Гигиена: ✅ git и GitHub, ✅ проект вне OneDrive. Осталось: решение по Git LFS, удалить бэкап-папки и мусор из корня (`quad_*.png`, `schema_zoom.png`, `unpack_tmp.ps1`…), зелёные тесты.
2. Честная машина: DriverCommand с поворотниками/светом/ремнём → солвер (EngineModel + сцепление + КПП + шины) → замена `TrainingVehicle`; вид из салона.
3. Правила как данные: таблица ошибок методики ГИБДД (1/2/3/4/7, незачёт ≥7), один RuleEvaluator в сцене, светофоры с фазами, roadgraph v2.
4. Город, ИИ-трафик, пешеходы, экзаменационный маршрут.
