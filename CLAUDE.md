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
- Город (тестовый район из модулей, ИИ-трафик, пешеходы): `docs/city.md`, карточки T27–T39, ADR-011…014 (единый диспетчер трафика — ADR-014).
- Актуальное состояние: `docs/audit-2026-09-23.md` (свежее) и `docs/current-state.md` (от 19.09).

## Жёсткие правила
1. **Чистые модули не ссылаются на UnityEngine.** Нужен Unity — пиши адаптер в `Presentation`/`Presentation/Physics`.
2. **Сгенерированное не правим руками.** `TrainingGroundBuilder`/`TrainingGroundLayout` перезаписывают сцену `Autodrome_Training.unity` и `course-v2.json`; `ProjectBuilder.Prepare` меняет сцены и настройки; `tools/setup_project.py` перезаписывает manifest, asmdef, примеры, `.gitignore`. Меняем генератор, а не результат. **Не запускать генераторы без явной просьбы.**
3. Не трогать `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `Builds/`, `.agents/` (архив прошлой команды агентов — не источник истины).
4. Единицы СИ, оси Unity: X вправо, Y вверх, Z вперёд. Сцепление: 1 = выжато (разомкнуто). Руль: −1…1. Педали: 0…1.
5. Игровые пороги ≠ правовые нормы. Любая ссылка на пункт ПДД или методику ГИБДД — с редакцией и источником; не выдумывать нормы. Официальные билеты — только из источника с правом использования.
6. Новый публичный тип/поле в `Contracts` — обновить `docs/data-formats.md`.

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

## Как работаем с задачами
- Одна задача = одна карточка (`docs/tasks/Txx.md` или новая) = один небольшой diff. Перед изменениями прочитать карточку, `docs/architecture.md` и затрагиваемые файлы целиком.
- Если задача упирается в решение, которого нет в документах, — остановиться и спросить, а не изобретать архитектуру.
- В конце: что изменено (файлы), как проверено (статусы выше), что осталось/риски. Логи — в `artifacts/reports/`, не в корень.

## Приоритеты (из аудита 23.09)
1. Git + вынести `Library/` из синхронизации OneDrive; зелёные тесты.
2. Честная машина: DriverCommand с поворотниками/светом/ремнём → солвер (EngineModel + сцепление + КПП + шины) → замена `TrainingVehicle`; вид из салона.
3. Правила как данные: таблица ошибок методики ГИБДД (1/2/3/4/7, незачёт ≥7), один RuleEvaluator в сцене, светофоры с фазами, roadgraph v2.
4. Город, ИИ-трафик, пешеходы, экзаменационный маршрут.
