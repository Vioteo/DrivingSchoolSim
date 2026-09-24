# Фактическое состояние: аудит 18.09, обновление 19.09.2026

Аудит основан на чтении `Contracts.cs`, `DrivetrainMath.cs`, `LessonSession.cs`, `WorldRepository.cs`, обеих файлов Presentation, `ProjectBuilder.cs`, `ContractTests.cs`, asmdef, manifest и `tools/setup_project.py`. Обновление учитывает `artifacts/unity-compile.log` и `artifacts/reports/shell-*`. Арт параллельно дорабатывается; старые отчёты не переносят статус на новые экспорты.

Термины статуса: **реализовано в исходниках** — код существует; **подготовлено** — интерфейс, пример или ресурс существует; **спроектировано** — решение в документах; **проверено** — есть воспроизводимый результат именно данной ревизии и среды. Этот документационный аудит не запускал runtime-проверки; подтверждённые координатором результаты ниже имеют отдельный ограниченный объём.

## Проверено и текущая работа

- **Unity compile: PASS в пределах запуска.** [Лог](../artifacts/unity-compile.log) заканчивается `Application will terminate with return code 0`. Это результат batch компиляции; он не означает прохождение NUnit, Prepare twice, Windows Build или runtime smoke. Тесты ещё не прогонялись.
- **Shell repair: принят пользователем в ограниченном объёме.** [Отчёт](../artifacts/reports/shell-repair.md), [browser review](../artifacts/reports/shell-browser-check.json), [coverage](../artifacts/reports/shell-coverage-check.json), [export check](../artifacts/reports/shell-export-check.json). Закрыты непредусмотренные просветы спереди и снизу, сохранён исходник, проверены реальные before/after renders и экспорт. Не утверждается единая watertight-модель, полный ход колёс, вся кинематика педалей или Unity collision readiness.
- **Cabin-v3: в работе.** Пока нет финальной приёмки этой ревизии. Порядок независимой проверки задан в [model-iteration](model-iteration.md).
- **HTML UI: в проверке отдельным исполнителем.** Девять экранов — визуальная спецификация; перенос в uGUI/TMP и аппаратные/Unity проверки остаются будущими.
- **G27 / FFB / PCVR / 1080p60 / full suspension sweep: NOT_RUN.** Нельзя закрывать соответствующие acceptance gates компиляцией или веб-просмотром.

## Реализовано в исходниках

- `DriverCommand.Validate`: диапазоны steering −1…1, throttle/brake/clutch 0…1, finite float, requestedGear −1…6. Нет проверки sequence. Clutch=1 означает полностью разомкнутое сцепление.
- `DrivetrainMath`: две статические формулы крутящего момента. Нет двигателя, шин, подвески, автомобиля Rigidbody, коробки или интегратора. Проверки параметров не отсекают NaN/Infinity последовательно.
- `LessonSession`: Briefing → Ready → Running → Passed/Failed/Cancelled; достижение дистанции и непрерывная остановка. `forwardDisplacementM` — абсолютное смещение от старта, не delta distance. Timeout проверяется раньше успешного завершения. Нет восьми упражнений, правил, ассистов, сохранения прогресса. Конструктор не проверяет все числовые значения на finite.
- `WorldRepository`: JSON через Unity JsonUtility; простые имена файлов, tmp, File.Replace с .bak при перезаписи; нет автоматического восстановления, блокировки конкурентной записи и обработки сбоев/лимитов размера.
- `WorldValidator`: версия 1, chunkSizeM строго 256, уникальные node/segment/lane/object IDs, finite координаты, связи и часть размерностей. **District содержимое не проверяется**. Не запрещены повторяющиеся индексы полос; не проверяется сумма ширин; нет геометрии кривых/перекрёстков.
- `ModelDemonstrator`: IMGUI sliders и вращения по именам. Все педали используют одно число, обе стрелки приборов одно rpm, передние колёса одинаковый угол ±32°, руль ±450°. Нет дорожной физики, независимых педалей, Ackermann, вращения шин и хода подвески. Driver eye запоминается в мировых координатах на старте; для движущегося автомобиля это нельзя оставлять.
- `PlanarMirror`: отдельная камера и RT 512 × 256 на зеркало, обlique projection, отключение стерео; код прямо ограничен mono-view. Нет XR per-eye проверки, бюджета частоты и доказательства корректной UV/нормали.
- `ProjectBuilder`: импорт FBX, URP assets, Showroom/District/Autodrome сцены, Windows Development Build. Prepare задаёт 1280 × 720, fixedDeltaTime=0.01, DX11; текущие настройки не являются целевым 1080p benchmark. Координатор добавил LoadAssetAtPath для существующих Renderer/URP assets перед CreateAsset: частичное исправление идемпотентности. Повторный Prepare всей сцены и сохранность ручных ресурсов ещё не доказаны. Проверка наличия `.fbx` обязательна.
- `ContractTests`: 12 EditMode тестов математических примеров, команд, сессии, roundtrip/backup и двух ошибок мира. Файл тестов существует; успешный прогон не засвидетельствован данным аудитом.

## Подготовлено

`IInputSource` и `IForceFeedbackOutput` существуют только как интерфейсы. У DS.Input и DS.Rules есть asmdef, но прочитанный каркас не содержит их реализаций. `VehicleState` не публикуется solver. RuleEvent и TheoryContentPack не подключены к runtime.

Демонстрационные JSON: world с 5 узлами, 4 сегментами и 16 полосами; одно занятие start-stop; один авторский вопрос; vehicle с проектными параметрами. `vehicle.json` **не имеет DTO/загрузчика**. `masterplan.json` — план территории, не стриминг и не построенный город.

Геометрический отчёт `DS_Sedan_A-geometry.json` содержит `trianglesBase`; это не измерение финальных треугольников после модификаторов и Unity импорта. `sedan-fit.json` revision 2 содержит численные упрощённые проверки и `fullSuspensionSweep: pending`. Его общий PASS не доказывает полный ход, пересечения meshes, коллизии, салон в Unity или зеркала.

## Существенные разрывы

1. Генератор `setup_project.py` перезаписывает manifest, asmdef, примеры, masterplan и `.gitignore`. Его нельзя повторно запускать после ручной разработки без аудита diff.
2. InputSystem пакет и import namespace не реализуют клавиатуру/G27. Для ламп/поворотников уже есть state-поля, но нет команд в DriverCommand. Automatic selector P/R/N/D также отсутствует; не кодировать его перегрузкой requestedGear.
3. Корневая сборка DS.Simulation запрещает Engine references. Unity Rigidbody adapter потребует отдельной сборки, а не добавления UnityEngine внутрь pure solver.
4. Roadgraph v1 — топология прямых связей без явных полосных кривых, stop lines, сигналов и pedestrian crossings. Визуальная дорожная сетка не генерируется из него.
5. Окружение получает MeshCollider только для объектов с точными именами RoadSurface/IntersectionSurface/HillRamp. Контактность всей сцены и автомобиля не гарантирована.
6. Подготовка ищет `Assets/DrivingSchool/Art/DS_*.fbx`; текущий `build_art.py` также экспортирует GLB для visual-review. Целевой каталог `Art/Exports` требует явной миграции путей.
7. Renderer доступ к Camera/TMP/OpenXR, loader, actions, VR origin, inputs и mirror stereo нуждается в реальной настройке. Package manifest не подтверждает её.
8. Unity scenes/Build outputs/логи, которые могут появиться позднее, принимаются только с привязкой к ревизии исходника. Веб-рендер и Blender screenshot не доказывают Unity runtime.
