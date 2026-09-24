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
