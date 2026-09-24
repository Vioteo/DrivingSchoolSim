# Контракты и форматы данных

Источник истины текущих полей: `Assets/DrivingSchool/Code/Contracts/Contracts.cs`. Ниже отражён прочитанный каркас; при изменении кода обновить документ. JsonUtility сериализует public fields DTO; JSON пример не становится контрактом без класса и validator. Имена регистрозависимы. Все величины SI, кроме явно обозначенных Kph, Rpm и Deg.

## Реальные контракты v1

- `DriverCommand` struct: `long sequence`; `float steering, throttle, brake, clutch`; `bool handbrake, ignition, starter`; `int requestedGear`. Steering −1…1, педали 0…1, clutch1=disengaged, gear −1 reverse/0 neutral/1…6 forward. `void Validate()`. Не содержит automatic selector, indicators/lights, horn или pause.
- `VehicleState` struct: `long tick`, `double simulationSeconds`, `float signedSpeedMps, engineRpm, steeringRadians, clutchTorqueNm`; `int gear`; `EnginePhase engine`; `bool leftIndicator, rightIndicator, lowBeam, highBeam, brakeLight`. Нет pose, wheel states, pedal feedback или validate method. `EnginePhase`: Off, Ignition, Cranking, Running, Stalled.
- `IInputSource`: `DriverCommand Read(long tick)`, readonly `bool IsConnected`. Политику arbitration ещё реализовать.
- `IForceFeedbackOutput : IDisposable`: readonly `bool IsAvailable`, `void SetNormalizedTorque(float torque)`, `void Stop()`. Torque ±1, backend clamps; не Nm. Stop идемпотентен на focus loss/pause/disconnect/dispose.
- `WorldDocument`: `int schemaVersion=1`, `string id,name`, `int chunkSizeM=256`, массивы `RoadNode[] nodes`, `RoadSegment[] segments`, `Lane[] lanes`, `WorldObject[] objects`, `District[] districts` (по умолчанию пустые).
- `RoadNode`: `string id`, `double x,y,z`. `RoadSegment`: `string id,fromNode,toNode`, `float widthM,speedLimitKph`, `int laneCount`. Это связи, без spline handles.
- `Lane`: `string id,segmentId,fromNode,toNode`, `int index`, `float widthM`, `string[] successors`. Индекс сейчас проверяется 0…segment.laneCount−1, уникальность пары/направления не проверяется. Sample повторно использует 0/1 для каждого направления; семантика индекса требует решения в v2.
- `WorldObject`: `string id,catalogId`, `double x,y,z`, `float yawDeg`. Нет scale/rotation pitch-roll или параметров instance. `District`: `string id`, `double minX,minZ`, `float sizeM`. Только квадрат, validator не проверяет его содержимое.
- `RuleEvent`: `string id,ruleId,ruleRevision,participantId,evidenceId,explanationKey`; `double simulationSeconds,x,y,z`; `int penalty`. Самостоятельного serializer/validator/evidence store ещё нет.
- `LessonDefinition`: `int schemaVersion=1`, `string id,title,worldId,contentStatus`; `float timeLimitSeconds,targetDistanceM,stopSpeedMps,requiredStopSeconds`. Нет упражнений type/revision/zones. Значение demonstration из sample — текст, не enum.
- `SessionPhase`: Briefing, Ready, Running, Passed, Failed, Cancelled. `SessionResult`: `string lessonId,reason`; `SessionPhase phase`; `float elapsedSeconds`; `string[] enabledAssists`; `RuleEvent[] events`. Массивы по умолчанию пусты; LessonSession пока их не заполняет.
- `TheoryContentPack`: `int schemaVersion=1`; `string id,revision,source`; `bool isOfficial`; `TheoryQuestion[] questions`. `TheoryQuestion`: `string id,text,explanation,ruleReference,topic`; `string[] answers`; `int correctIndex`. Нет media links, jurisdiction, подтверждения provenance или validators.

## Граф дорог v2 (T11, T27)

Источник истины: `Assets/DrivingSchool/Code/Contracts/WorldDocumentV2.cs`. Логика — `Simulation/RoadGraph/` (pure C#): `WorldValidatorV2`, `WorldMigration` (v1 → v2), `ConflictZoneBuilder`, `Polyline`. Формат совместим с JsonUtility: массивы простых DTO, enum как int.

- `Vec3d` struct `double x,y,z`. `CubicCurve` struct `p0..p3`.
- `WorldDocumentV2`: `schemaVersion=2`, `id,name,revision`, `chunkSizeM=256` и массивы: `nodes` (`RoadNode` из v1), `segments`, `lanes`, `junctions`, `connections`, `conflictZones`, `stopLines`, `crossings`, `signalGroups`, `signalPlans`, `signals`, `boundaries`, `signs`, `approaches`, `sidewalks`, `zones`, `spawnPoints`, `objects`, `districts`. Все id уникальны во всём документе.
- `RoadSegmentV2`: `id,fromNode,toNode`, `curve`, `speedLimitKph, laneWidthM`, `lanesForward, lanesBackward`.
- `LaneV2`: `id,segmentId`, `index` (> 0 по направлению сегмента, < 0 против; |1| — у оси), `widthM, speedLimitKph`, `centerline` (шаг ≤ 2 м после миграции), `successors` (прямое продолжение вне перекрёстков, начало преемника совпадает с концом полосы ±5 см), `leftNeighborId, rightNeighborId, oncomingLaneId`, `allowedManeuvers` (`LaneManeuver` flags: Straight/Right/Left/UTurn; None = без ограничений).
- `Junction`: `id,nodeId,signalPlanId`, `connectionIds`. `LaneConnection`: `id,junctionId,fromLaneId,toLaneId,signalGroupId`, `maneuver` (ровно один флаг), `speedLimitKph`, `centerline` (начинается в конце `fromLane`, заканчивается в начале `toLane`). Полосы и связи — одно пространство путей для маршрутов.
- `ConflictZone`: `id,junctionId,connectionA,connectionB`, диапазоны `fromSA..toSA`, `fromSB..toSB` вдоль связей, `merge` (обе ведут в одну полосу). Строится `ConflictZoneBuilder`: центры ближе 2,4 м (игровой параметр) или общий выезд; связи из одной полосы не конфликтуют.
- `StopLine`: `id,laneId,s`. `PedestrianCrossing`: `id,signalGroupId`, концы `a,b`, `widthM`, `laneIds` (пересекаемые полосы/связи — проверяется геометрически), `sidewalkIds`.
- `SignalGroup`: `id,junctionId`, `kind` (Vehicle/VehicleArrow/Pedestrian), `connectionIds, crossingIds`. `SignalPlan`: `id,junctionId,offsetSeconds`, `stages`. `SignalStage`: `greenGroupIds`, `greenSeconds, greenFlashSeconds, amberSeconds, allRedSeconds, redAmberSeconds`. `TrafficSignalAttachment`: `id,signalGroupId,catalogId`, поза.
- `LaneBoundary`: `id,laneId`, `side` (Left/Right), `type` (`MarkingType`: None/Solid/Dashed/DoubleSolid/SolidDashed/DashedSolid), `fromS..toS`; участки одной стороны полосы не перекрываются.
- `SignPlacement`: `id`, `code` (ГОСТ Р 52290-2004, строка), `value`, `catalogId`, `plaques`, поза, `laneIds`, `atS`, `untilNextJunction`, `zoneEndLaneId, zoneEndS`. Неизвестный код отклоняется, если валидатору передан справочник знаков.
- `JunctionApproach`: `id,junctionId,laneId,stopLineId`, `priority` (Equal/Main/Secondary/Signalized), `sourceSignIds`. Main требует знак 2.1 или 2.3.x, Secondary — 2.4 или 2.5, Signalized — план светофора у перекрёстка.
- `SidewalkPath`: `id,widthM`, `points`, `linkedIds` (тротуары и переходы). `Zone`: `id,laneId`, `kind` (Parking/NoStopping/NoParking/KeepJunctionClear), `fromS..toS`. `SpawnPoint`: `id,pathId`, `role` (Vehicle → полоса/связь, Pedestrian → тротуар), `edge`, `s`.
- Миграция v1 → v2: узлы степени ≥ 3 становятся перекрёстками, полосы у них обрезаются на половину ширины самой широкой дороги, successors v1 через узел превращаются в кубические `LaneConnection`. Геометрия перекрёстка схематическая, топология точная. Разметка, знаки и приоритет в v1 отсутствуют и после миграции пусты.

## Рантайм графа и светофоры (T31, T32, T16)

- `SignalAspect` (Contracts): Off, Red, RedAmber, Amber, Green, GreenFlashing, AmberFlashing. Пешеходные группы — только Red, Green, GreenFlashing, Off. `TrafficSignalView.Aspect` расширен теми же значениями в том же порядке (новые добавлены в конец, сохранённые в сценах значения не меняются).
- `RoadGraphIndex` (Simulation/RoadGraph): пути (полосы и связи) по id, `Next`/`Previous`, сетка 16 м, покрытие знаков, `SpeedLimitAt(path, s)` (минимум из 3.24 и лимита пути).
- `LaneLocator.Locate(x, z, heading, previous) → LanePosition` (`PathId, S, D, HeadingError, OffRoad, AgainstDirection`). Гистерезис 0,3 м, допуск за краем полосы 0,6 м — игровые параметры.
- `SignalController` (Simulation/Traffic): аспект — функция времени симуляции и плана; режимы Normal / FlashingAmber / Off; `TimeToChange`. `ValidatePlan` запрещает одновременный зелёный пересекающимся прямым направлениям и прямому направлению с пешеходами на его переходе; разрешённые повороты могут делить зелёный (уступают по правилам).
- `LaneFollowerAgent` + `DriverProfile` (Simulation/Traffic): кинематика `(path, s, d, v, a)` по маршруту, IDM, торможение к меньшему ограничению впереди, остановка у стоп-линии/препятствия; параметры профиля — игровые.

## Раскладка района (T29)

Источник истины: `Assets/DrivingSchool/Code/Contracts/DistrictLayout.cs`; компилятор — `Simulation/RoadGraph/DistrictCompiler.cs`, шаблоны модулей — `Simulation/RoadGraph/RoadKitTemplates.cs`, справочник знаков — `SignCatalog.cs`.

- `DistrictLayout`: `schemaVersion=1`, `id,name,revision`, `instances`, `joins`, `openSockets`, `signs`, `approaches`, `signalPlans`.
- `ModuleInstance`: `id` (без `/`), `catalogId` (модуль Road Kit), поза корня префаба `x,y,z`, `yawDeg` (0 = +Z, 90 = +X), `speedLimitKph` (0 = по шаблону, 60).
- `SocketJoin`: `instanceA,socketA,instanceB,socketB` — сокеты должны совпасть с точностью 1 см и смотреть навстречу (0,1°), профили (число полос в каждую сторону и ширина) равны. Модули не сдвигаются: несовпадение — ошибка с величиной зазора.
- `SocketRef` в `openSockets`: край района; для въезжающих полос создаются точки появления машин. Сокет, не соединённый и не отмеченный открытым, — ошибка «Dangling socket».
- `LayoutSign`: `id,code,value,instanceId,laneId` (локальный id полосы в шаблоне), `plaques` (id префабов табличек), `atS`, `untilNextJunction`. Ставится на 3,4 м правее центра полосы (центр тротуара Road Kit v1), лицом к потоку; зона «до перекрёстка» кончается на стоп-линии.
- `LayoutApproach`: `instanceId,socket,priority,signIds` для въезда перекрёстка; без записи въезд равнозначный.
- `LayoutSignalPlan`: `instanceId,offsetSeconds`, `stages` (`LayoutSignalStage`: `greenSockets` — въезды с зелёным, `walkSockets` — переходы через рукава с зелёным для пешеходов, длительности как в `SignalStage`). Группы: `<inst>/sg.<socket>` (все связи въезда), `<inst>/pg.<socket>` (переход); головы светофоров ставятся компилятором. План обязан давать зелёный каждому въезду.
- Итоговые id графа: `<instanceId>/<localId>`; отпечаток графа — `GraphFingerprint.Compute` (SHA256 канонического дампа, не зависит от форматирования JSON).

## Файлы примеров

`world.json` v1: training-district, 5 nodes, 4 segments, 16 lanes, spawn-car и demo district500m. Successors не моделируют точную геометрию манёвров, приоритет или конфликтные зоны. `lesson.json`: start-stop-demo, 120s, target20m, stop≤0.14m/s в течение2s. `theory.json`: одна авторская демонстрация; `isOfficial=false`.

`vehicle.json` — **проектный JSON без C# DTO**: schemaVersion1, id ds01, massKg1350, lengthM4.5, bodyWidthM1.8, mirrorWidthM2.25, heightM1.5, wheelbaseM2.72, trackM1.71, wheelRadiusM0.327, centreOfMassM [0,0.51,−0.1], steeringWheelDegrees900, gearRatios [3.6,2.1,1.4,1.05,0.84,0.69], reverseRatio−3.5, finalDrive4.1, idleRpm850, redlineRpm6500, engineInertiaKgm2 0.2, maxClutchTorqueNm240, calibrationStatus design-target-not-measured. После доработки геометрии размеры могут отличаться: сверить measurement report, не менять silent. Модель источника Blender использует другую осевую раскладку до экспорта; центр масс JSON задан как Unity SI target.

`artifacts/visual-review/data/masterplan.json`: sizeM10000, прямоугольники районов и polyline routes; status masterplan-not-built-city. Это визуальная схема, не WorldDocument, не streaming manifest и не импортируемая без преобразования карта.

## Спроектированные расширения: не передавать текущему загрузчику

World v2 сначала оформляется T11. Нужны document revision, units/axis declaration, stable IDs, cubic curves/control points в double, lane direction/index convention, lane centreline/width, connections с геометрией и conflict groups, stop lines/signals/crossings и привязки regulatory objects. Библиотека catalog имеет версии. Chunks ссылаются на объекты graph; derived meshes/collider caches не принадлежат canonical document. Формат может быть split manifest+chunks; конкретный DTO утверждается после небольшого fixture roundtrip, не этим текстом.

Vehicle definition v1 DTO должен валидировать finite и physical ranges всех полей, документировать знак reverse и нулевой neutral ratio, определить engine torque curve и параметры brakes/tyres/suspension отдельно от художественной геометрии. Не выводить массу и сцепление из bounds mesh.

Lesson v2: revision/evaluatorId, versioned criteria/zones, result с revisions world/vehicle/rules/content и enabledAssists. Для восьми evaluator входы фиксируются по отдельности. Recipe/порог вправе быть авторским и должен иметь contentStatus. Теория: отдельный provenance record с source URI, retrieval date, revision, jurisdiction, permission note и подтверждением official status. Не добавлять эти поля в existing sample до готовности мигратора.

## Правила чтения и сохранения

1. Прочитать header/version и размеры файла до materialization; будущая версия отклоняется без перезаписи.
2. Проверить finite, обязательность, IDs, references, geometry, допустимость catalog и лимиты элементов; bad fields возвращают путь/причину пользователю.
3. Миграция только в новую копию, затем validator, temporary write, commit/backup. Статус совместимости и исходная версия сохраняются в логе.
4. Roundtrip сравнивает всю каноническую модель, а не только world id/count. Незнакомые поля нельзя молча стереть старым writer; при отсутствии forward preservation такой файл блокируется.
5. File name — plain name; текущий Resolve запрещает пустое, '..', '/' и '\\'. Повреждённый JSON/.tmp/.bak, нет места/доступа и конфликт записей — отдельные отрицательные сценарии T15.

UTF-8, invariant числовая культура, finite JSON numbers. Evidence хранит абсолютные world positions и revision, чтобы origin shift не искажал событие. Визуальная строка объяснения локализуется по explanationKey; она не заменяет machine-readable event.
